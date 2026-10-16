using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SocialPublisher.API.Models;

namespace SocialPublisher.API.Services
{
    public class InstagramPublisher : ISocialPublisher
    {
        public string PlatformName => "instagram";

        private const int MaxCaptionLength = 2200;
        private const int MaxPollAttempts = 20;
        private static readonly TimeSpan PollDelay = TimeSpan.FromSeconds(5);

        private static readonly string[] VideoExtensions = { ".mp4", ".mov" };
        private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".webp" };

        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<InstagramPublisher> _logger;

        public InstagramPublisher(HttpClient httpClient, IConfiguration configuration, ILogger<InstagramPublisher> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
        }

        private string GraphBase =>
            $"https://graph.facebook.com/{(_configuration["SocialMedia:GraphApiVersion"] ?? "v25.0")}";

        public async Task<PublishResultDto> PublishAsync(SocialPost post, SocialAccount account, CancellationToken cancellationToken)
        {
            var result = new PublishResultDto { Platform = PlatformName };
            var startedAt = DateTimeOffset.UtcNow;
            HashSet<string>? baseline = null;   // IDs of posts that existed BEFORE we started

            try
            {
                // ── Validation ────────────────────────────────────────────────
                if (string.IsNullOrWhiteSpace(post.Content) || post.Content.Length > MaxCaptionLength)
                    return Fail(result, $"Instagram caption must be between 1 and {MaxCaptionLength} characters.");

                if (string.IsNullOrWhiteSpace(account.ExternalAccountId) || string.IsNullOrWhiteSpace(account.AccessToken))
                    return Fail(result, "Instagram account is not configured. Set SocialMedia:Instagram:AccountId and AccessToken.");

                var baseUrl = _configuration["SocialMedia:PublicBaseUrl"];
                if (string.IsNullOrWhiteSpace(baseUrl) ||
                    baseUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
                    baseUrl.Contains("127.0.0.1"))
                {
                    return Fail(result,
                        "Instagram must download media from a public URL. Set SocialMedia:PublicBaseUrl to your public " +
                        "tunnel address (e.g. https://xxxx.ngrok-free.app) and restart the API.");
                }

                // Instagram only supports images and videos. Anything else (e.g. PDF) is skipped.
                var usableMedia = (post.Media ?? new List<SocialPostMedia>())
                    .Where(m => IsVideo(m) || IsImage(m))
                    .ToList();

                if (usableMedia.Count == 0)
                    return Fail(result, "Instagram requires at least one image or video (PDF/documents are not supported on Instagram).");

                if (usableMedia.Count > 10)
                    return Fail(result, "Instagram supports a maximum of 10 media items in a carousel.");

                // Remember which posts already exist, so we can later tell OUR post apart from old ones.
                baseline = await GetRecentMediaIdsAsync(account, cancellationToken);

                var publicBase = baseUrl.TrimEnd('/');
                string containerId;

                if (usableMedia.Count == 1)
                {
                    // ── Single image OR single video ──────────────────────────
                    var media = usableMedia[0];
                    var url = publicBase + media.FileUrl;

                    containerId = (IsVideo(media)
                        ? await CreateVideoContainerAsync(account, post.Content, url, cancellationToken)
                        : await CreateImageContainerAsync(account, post.Content, url, cancellationToken))
                        ?? string.Empty;
                }
                else
                {
                    // ── Carousel (image + video, or many images/videos) ───────
                    var childContainerIds = new List<string>();

                    // 1. One child container per media item (no caption)
                    foreach (var media in usableMedia)
                    {
                        var url = publicBase + media.FileUrl;

                        var childId = IsVideo(media)
                            ? await CreateVideoContainerAsync(account, null, url, cancellationToken, isCarouselItem: true)
                            : await CreateImageContainerAsync(account, null, url, cancellationToken, isCarouselItem: true);

                        if (string.IsNullOrEmpty(childId))
                            return Fail(result, "Failed to create a child container for the carousel.");

                        childContainerIds.Add(childId);
                    }

                    // 2. Wait until every child is ready
                    foreach (var childId in childContainerIds)
                    {
                        var ready = await WaitForContainerAsync(childId, account.AccessToken, cancellationToken);
                        if (!ready)
                            return Fail(result, $"Carousel item {childId} failed to process or timed out.");
                    }

                    // 3. Create the carousel container (with caption)
                    containerId = await CreateCarouselContainerAsync(account, post.Content, childContainerIds, cancellationToken)
                                  ?? string.Empty;
                }

                if (string.IsNullOrEmpty(containerId))
                    return Fail(result, "Instagram did not return a container ID.");

                // ── Wait until the main container is ready ────────────────────
                var mainReady = await WaitForContainerAsync(containerId, account.AccessToken, cancellationToken);
                if (!mainReady)
                    return Fail(result, "Instagram is still processing the media. Wait a moment and press Retry.");

                // ── Publish ───────────────────────────────────────────────────
                var publishForm = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["creation_id"] = containerId,
                    ["access_token"] = account.AccessToken
                });

                using var publishResp = await _httpClient.PostAsync(
                    $"{GraphBase}/{account.ExternalAccountId}/media_publish", publishForm, cancellationToken);
                var publishBody = await publishResp.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogInformation("Instagram media_publish ({Status}): {Body}", (int)publishResp.StatusCode, publishBody);

                if (publishResp.IsSuccessStatusCode)
                {
                    var mediaId = ReadString(publishBody, "id");
                    if (string.IsNullOrEmpty(mediaId))
                    {
                        var found = await VerifyPublishedAsync(account, post.Content, baseline, startedAt, cancellationToken);
                        if (found != null) return MarkPublished(result, found.Value.Id, found.Value.Permalink);
                        return Fail(result, "Instagram did not return a media ID after publishing.");
                    }

                    var permalink = await GetPermalinkAsync(account, mediaId, cancellationToken);
                    return MarkPublished(result, mediaId, permalink);
                }

                // media_publish returned an error. Instagram sometimes errors even though the post
                // went live, so look for a NEW post on the account before reporting failure.
                var recovered = await VerifyPublishedAsync(account, post.Content, baseline, startedAt, cancellationToken);
                if (recovered != null)
                {
                    _logger.LogWarning("media_publish returned an error but the post is live (id {Id}). Treating as success.", recovered.Value.Id);
                    return MarkPublished(result, recovered.Value.Id, recovered.Value.Permalink);
                }

                return Fail(result, ExtractError(publishBody));
            }
            catch (OperationCanceledException)
            {
                return Fail(result, "Request timed out or was cancelled. Check Instagram before retrying to avoid a duplicate post.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Instagram publishing failed");

                // An error at any step may still have ended with the post being live. Verify first.
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40));
                    var recovered = await VerifyPublishedAsync(account, post.Content, baseline, startedAt, cts.Token);
                    if (recovered != null)
                    {
                        _logger.LogWarning("Error occurred but the post is live (id {Id}). Treating as success.", recovered.Value.Id);
                        return MarkPublished(result, recovered.Value.Id, recovered.Value.Permalink);
                    }
                }
                catch (Exception verifyEx)
                {
                    _logger.LogWarning(verifyEx, "Could not verify whether the Instagram post was published.");
                }

                return Fail(result, ex.Message);
            }
        }

        // ── Media type helpers ────────────────────────────────────────────────────

        private static string Ext(SocialPostMedia m)
        {
            try
            {
                var path = (m.FileUrl ?? string.Empty).Split('?')[0];
                return Path.GetExtension(path).ToLowerInvariant();
            }
            catch { return string.Empty; }
        }

        private static bool IsVideo(SocialPostMedia m) =>
            m.MediaType == "Video" || VideoExtensions.Contains(Ext(m));

        private static bool IsImage(SocialPostMedia m) =>
            !IsVideo(m) && ImageExtensions.Contains(Ext(m));

        // ── Verification helpers ──────────────────────────────────────────────────

        /// <summary>Returns the IDs of the account's latest posts (or null if we could not read them).</summary>
        private async Task<HashSet<string>?> GetRecentMediaIdsAsync(SocialAccount account, CancellationToken ct)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get,
                    $"{GraphBase}/{account.ExternalAccountId}/media?fields=id&limit=25");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);

                using var resp = await _httpClient.SendAsync(req, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);
                if (!resp.IsSuccessStatusCode) return null;

                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                    return null;

                var ids = new HashSet<string>();
                foreach (var item in data.EnumerateArray())
                    if (item.TryGetProperty("id", out var i) && i.GetString() is string id) ids.Add(id);
                return ids;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read existing Instagram posts.");
                return null;
            }
        }

        /// <summary>
        /// Checks a few times (Instagram can be slow to list a new post) for a NEW post with our caption.
        /// "New" = its ID was not on the account before we started.
        /// </summary>
        private async Task<(string Id, string? Permalink)?> VerifyPublishedAsync(
            SocialAccount account, string caption, HashSet<string>? baseline, DateTimeOffset startedAt, CancellationToken ct)
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                await Task.Delay(TimeSpan.FromSeconds(4), ct);
                var found = await FindNewPostAsync(account, caption, baseline, startedAt, ct);
                if (found != null) return found;
            }
            return null;
        }

        private async Task<(string Id, string? Permalink)?> FindNewPostAsync(
            SocialAccount account, string caption, HashSet<string>? baseline, DateTimeOffset startedAt, CancellationToken ct)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get,
                    $"{GraphBase}/{account.ExternalAccountId}/media?fields=id,caption,timestamp,permalink&limit=10");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);

                using var resp = await _httpClient.SendAsync(req, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Could not list recent Instagram posts: {Body}", body);
                    return null;
                }

                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                    return null;

                var wanted = NormalizeCaption(caption);

                foreach (var item in data.EnumerateArray())
                {
                    var id = item.TryGetProperty("id", out var i) ? i.GetString() : null;
                    if (string.IsNullOrEmpty(id)) continue;

                    // Must be a post that did NOT exist before this attempt started.
                    if (baseline != null)
                    {
                        if (baseline.Contains(id)) continue;
                    }
                    else
                    {
                        // Fallback if we could not read the "before" list: rely on the timestamp.
                        var ts = item.TryGetProperty("timestamp", out var t) && t.ValueKind == JsonValueKind.String
                            ? ParseInstagramTime(t.GetString()) : null;
                        if (ts == null || ts.Value < startedAt.AddSeconds(-30)) continue;
                    }

                    var itemCaption = item.TryGetProperty("caption", out var c) && c.ValueKind == JsonValueKind.String
                        ? c.GetString() : null;
                    if (NormalizeCaption(itemCaption) != wanted) continue;

                    var permalink = item.TryGetProperty("permalink", out var p) && p.ValueKind == JsonValueKind.String
                        ? p.GetString() : null;

                    return (id, permalink);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while checking recent Instagram posts.");
            }
            return null;
        }

        private static string NormalizeCaption(string? s) =>
            (s ?? string.Empty).Replace("\r\n", "\n").Trim();

        // Instagram returns times like 2026-10-09T10:15:30+0000 (no colon in the offset).
        private static DateTimeOffset? ParseInstagramTime(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var fixedValue = Regex.Replace(value, @"([+-]\d{2})(\d{2})$", "$1:$2");
            return DateTimeOffset.TryParse(fixedValue, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt)
                ? dt : null;
        }

        private async Task<string?> GetPermalinkAsync(SocialAccount account, string mediaId, CancellationToken ct)
        {
            try
            {
                using var linkReq = new HttpRequestMessage(HttpMethod.Get, $"{GraphBase}/{mediaId}?fields=permalink");
                linkReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
                using var linkResp = await _httpClient.SendAsync(linkReq, ct);
                var linkBody = await linkResp.Content.ReadAsStringAsync(ct);
                if (linkResp.IsSuccessStatusCode)
                    return ReadString(linkBody, "permalink");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read Instagram permalink (post is already published).");
            }
            return null;
        }

        // ── Container creation helpers ────────────────────────────────────────────

        private async Task<string?> CreateImageContainerAsync(SocialAccount account, string? caption, string imageUrl, CancellationToken ct, bool isCarouselItem = false)
        {
            var dict = new Dictionary<string, string>
            {
                ["image_url"] = imageUrl,
                ["access_token"] = account.AccessToken
            };

            if (caption != null) dict["caption"] = caption;
            if (isCarouselItem) dict["is_carousel_item"] = "true";

            var form = new FormUrlEncodedContent(dict);

            using var resp = await _httpClient.PostAsync($"{GraphBase}/{account.ExternalAccountId}/media", form, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogInformation("Instagram create image container ({Status}): {Body}", (int)resp.StatusCode, body);

            if (!resp.IsSuccessStatusCode)
                throw new Exception(ExtractError(body));

            return ReadString(body, "id");
        }

        private async Task<string?> CreateVideoContainerAsync(SocialAccount account, string? caption, string videoUrl, CancellationToken ct, bool isCarouselItem = false)
        {
            var dict = new Dictionary<string, string>
            {
                // Carousel item videos use VIDEO; standalone videos are published as REELS.
                ["media_type"] = isCarouselItem ? "VIDEO" : "REELS",
                ["video_url"] = videoUrl,
                ["access_token"] = account.AccessToken
            };

            if (caption != null) dict["caption"] = caption;

            if (isCarouselItem)
                dict["is_carousel_item"] = "true";
            else
                dict["share_to_feed"] = "true";

            var form = new FormUrlEncodedContent(dict);

            using var resp = await _httpClient.PostAsync($"{GraphBase}/{account.ExternalAccountId}/media", form, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogInformation("Instagram create video container ({Status}): {Body}", (int)resp.StatusCode, body);

            if (!resp.IsSuccessStatusCode)
                throw new Exception(ExtractError(body));

            return ReadString(body, "id");
        }

        private async Task<string?> CreateCarouselContainerAsync(SocialAccount account, string caption, List<string> childrenIds, CancellationToken ct)
        {
            var dict = new Dictionary<string, string>
            {
                ["media_type"] = "CAROUSEL",
                ["caption"] = caption,
                ["children"] = string.Join(",", childrenIds),
                ["access_token"] = account.AccessToken
            };

            var form = new FormUrlEncodedContent(dict);

            using var resp = await _httpClient.PostAsync($"{GraphBase}/{account.ExternalAccountId}/media", form, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogInformation("Instagram create carousel container ({Status}): {Body}", (int)resp.StatusCode, body);

            if (!resp.IsSuccessStatusCode)
                throw new Exception(ExtractError(body));

            return ReadString(body, "id");
        }

        private async Task<bool> WaitForContainerAsync(string containerId, string accessToken, CancellationToken ct)
        {
            for (var attempt = 0; attempt < MaxPollAttempts; attempt++)
            {
                using var statusReq = new HttpRequestMessage(HttpMethod.Get,
                    $"{GraphBase}/{containerId}?fields=status_code,status");
                statusReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                using var statusResp = await _httpClient.SendAsync(statusReq, ct);
                var statusBody = await statusResp.Content.ReadAsStringAsync(ct);
                _logger.LogInformation("Instagram container status {Id}: {Body}", containerId, statusBody);

                if (!statusResp.IsSuccessStatusCode)
                    throw new Exception(ExtractError(statusBody));

                var statusCode = ReadString(statusBody, "status_code");

                if (statusCode == "FINISHED") return true;

                if (statusCode == "ERROR" || statusCode == "EXPIRED")
                {
                    var detail = ReadString(statusBody, "status");
                    throw new Exception($"Instagram could not process the media ({statusCode}). {detail}".Trim());
                }

                await Task.Delay(PollDelay, ct);
            }
            return false;
        }

        // ── Small helpers ─────────────────────────────────────────────────────────

        private static PublishResultDto MarkPublished(PublishResultDto result, string mediaId, string? permalink)
        {
            result.Status = "Published";
            result.ExternalPostId = mediaId;
            result.PostUrl = permalink;
            result.Error = null;
            return result;
        }

        private static PublishResultDto Fail(PublishResultDto result, string message)
        {
            result.Status = "Failed";
            result.Error = message;
            return result;
        }

        private static string? ReadString(string json, string property)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty(property, out var value) &&
                    value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString();
                }
            }
            catch (JsonException) { }
            return null;
        }

        private static string ExtractError(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("error", out var err))
                {
                    var userMsg = err.TryGetProperty("error_user_msg", out var u) ? u.GetString() : null;
                    var msg = err.TryGetProperty("message", out var m) ? m.GetString() : null;
                    int? code = err.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : null;
                    var text = userMsg ?? msg ?? json;
                    return code.HasValue ? $"Instagram API error {code}: {text}" : $"Instagram API error: {text}";
                }
            }
            catch (JsonException) { }
            return $"Instagram API error: {json}";
        }
    }
}