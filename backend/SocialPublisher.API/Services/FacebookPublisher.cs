using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SocialPublisher.API.Models;

namespace SocialPublisher.API.Services
{
    public class FacebookPublisher : ISocialPublisher
    {
        public string PlatformName => "facebook";

        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<FacebookPublisher> _logger;

        public FacebookPublisher(HttpClient httpClient, IConfiguration configuration, ILogger<FacebookPublisher> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
        }

        // Same setting the AccountsController uses. Default keeps today's behaviour (v19.0).
        private string GraphBase =>
            $"https://graph.facebook.com/{_configuration["SocialMedia:GraphApiVersion"] ?? "v19.0"}";

        public async Task<PublishResultDto> PublishAsync(SocialPost post, SocialAccount account, CancellationToken cancellationToken)
        {
            var result = new PublishResultDto { Platform = PlatformName };

            try
            {
                // Step 1: Resolve the correct Page Access Token
                var pageToken = await ResolvePageAccessTokenAsync(account, cancellationToken);

                if (pageToken == null)
                {
                    result.Status = "Failed";
                    result.Error = "Could not retrieve Page Access Token. Ensure the token has 'pages_manage_posts' and 'pages_read_engagement' permissions.";
                    return result;
                }

                var pageId = account.ExternalAccountId;
                var publicBase = _configuration["SocialMedia:PublicBaseUrl"]?.TrimEnd('/') ?? "";

                // Step 2: Determine if we have media and what type
                var images = post.Media?.Where(m => m.MediaType == "Image").ToList() ?? new List<SocialPostMedia>();
                var videos = post.Media?.Where(m => m.MediaType == "Video").ToList() ?? new List<SocialPostMedia>();

                if (videos.Count > 0 && images.Count > 0)
                {
                    result.Status = "Failed";
                    result.Error = "Facebook Graph API does not support combining images and a video in a single post. Please select only images, or a single video.";
                    return result;
                }

                if (videos.Count > 1)
                {
                    result.Status = "Failed";
                    result.Error = "Facebook allows only one video per post. Remove the extra videos.";
                    return result;
                }

                string? externalId;

                if (videos.Count == 1)
                {
                    var video = videos[0];
                    externalId = await PublishVideoAsync(pageId, pageToken, post.Content, publicBase + video.FileUrl, cancellationToken);
                }
                else if (images.Count > 0)
                {
                    if (images.Count == 1)
                    {
                        var imageUrl = publicBase + images[0].FileUrl;
                        externalId = await PublishSinglePhotoAsync(pageId, pageToken, post.Content, imageUrl, cancellationToken);
                    }
                    else
                    {
                        externalId = await PublishMultiplePhotosAsync(pageId, pageToken, post.Content, images, publicBase, cancellationToken);
                    }
                }
                else
                {
                    externalId = await PublishTextAsync(pageId, pageToken, post.Content, cancellationToken);
                }

                if (externalId == null)
                {
                    result.Status = "Failed";
                    result.Error = "Facebook did not return a post ID.";
                    return result;
                }

                result.Status = "Published";
                result.ExternalPostId = externalId;

                // Build the post URL (format: pageId_postId)
                if (externalId.Contains("_"))
                {
                    var parts = externalId.Split('_');
                    result.PostUrl = $"https://www.facebook.com/{parts[0]}/posts/{parts[1]}";
                }
                else
                {
                    result.PostUrl = $"https://www.facebook.com/{pageId}/posts/{externalId}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Facebook publishing failed");
                result.Status = "Failed";
                result.Error = ex.Message;
            }

            return result;
        }

        // ── Text-only post ────────────────────────────────────────────────────────
        private async Task<string?> PublishTextAsync(string pageId, string pageToken, string message, CancellationToken ct)
        {
            var url = $"{GraphBase}/{pageId}/feed";
            var payload = new { message, access_token = pageToken };
            var response = await _httpClient.PostAsJsonAsync(url, payload, ct);
            var content = await response.Content.ReadAsStringAsync(ct);
            _logger.LogInformation("Facebook /feed response: {Content}", content);

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Facebook /feed error: {content}");

            using var doc = JsonDocument.Parse(content);
            return doc.RootElement.GetProperty("id").GetString();
        }

        // ── Single Photo post ─────────────────────────────────────────────────────
        private async Task<string?> PublishSinglePhotoAsync(string pageId, string pageToken, string caption, string imageUrl, CancellationToken ct)
        {
            var url = $"{GraphBase}/{pageId}/photos";

            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["url"] = imageUrl,
                ["caption"] = caption,
                ["access_token"] = pageToken
            });

            var response = await _httpClient.PostAsync(url, form, ct);
            var content = await response.Content.ReadAsStringAsync(ct);
            _logger.LogInformation("Facebook single /photos response: {Content}", content);

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Facebook /photos error: {content}");

            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("post_id", out var postId))
                return postId.GetString();
            return doc.RootElement.GetProperty("id").GetString();
        }

        // ── Multiple Photos post ──────────────────────────────────────────────────
        private async Task<string?> PublishMultiplePhotosAsync(string pageId, string pageToken, string message, List<SocialPostMedia> images, string publicBase, CancellationToken ct)
        {
            var photoIds = new List<string>();

            // 1. Upload each photo as unpublished
            foreach (var image in images)
            {
                var url = $"{GraphBase}/{pageId}/photos";
                var form = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["url"] = publicBase + image.FileUrl,
                    ["published"] = "false",
                    ["access_token"] = pageToken
                });

                var response = await _httpClient.PostAsync(url, form, ct);
                var content = await response.Content.ReadAsStringAsync(ct);
                _logger.LogInformation("Facebook unpublished /photos response: {Content}", content);

                if (!response.IsSuccessStatusCode)
                    throw new Exception($"Facebook unpublished /photos error: {content}");

                using var doc = JsonDocument.Parse(content);
                photoIds.Add(doc.RootElement.GetProperty("id").GetString()!);
            }

            // 2. Publish post with attached_media
            var feedUrl = $"{GraphBase}/{pageId}/feed";

            var attachedMediaList = photoIds.Select(id => new { media_fbid = id }).ToList();
            var payload = new
            {
                message = message,
                attached_media = attachedMediaList,
                access_token = pageToken
            };

            var feedResponse = await _httpClient.PostAsJsonAsync(feedUrl, payload, ct);
            var feedContent = await feedResponse.Content.ReadAsStringAsync(ct);
            _logger.LogInformation("Facebook multiple photos /feed response: {Content}", feedContent);

            if (!feedResponse.IsSuccessStatusCode)
                throw new Exception($"Facebook multiple photos /feed error: {feedContent}");

            using var feedDoc = JsonDocument.Parse(feedContent);
            return feedDoc.RootElement.GetProperty("id").GetString();
        }

        // ── Video post ────────────────────────────────────────────────────────────
        private async Task<string?> PublishVideoAsync(string pageId, string pageToken, string description, string videoUrl, CancellationToken ct)
        {
            var url = $"{GraphBase}/{pageId}/videos";

            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["file_url"] = videoUrl,
                ["description"] = description,
                ["access_token"] = pageToken
            });

            var response = await _httpClient.PostAsync(url, form, ct);
            var content = await response.Content.ReadAsStringAsync(ct);
            _logger.LogInformation("Facebook /videos response: {Content}", content);

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Facebook /videos error: {content}");

            using var doc = JsonDocument.Parse(content);
            return doc.RootElement.GetProperty("id").GetString();
        }

        // ── Resolve Page Access Token ─────────────────────────────────────────────
        // SECURITY: the /me/accounts response contains every Page's access token,
        // so it is never logged, and the user token is sent in a header, not in the URL.
        private async Task<string?> ResolvePageAccessTokenAsync(SocialAccount account, CancellationToken cancellationToken)
        {
            try
            {
                var pageId = account.ExternalAccountId;
                var userToken = account.AccessToken;

                using var request = new HttpRequestMessage(HttpMethod.Get, $"{GraphBase}/me/accounts");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userToken);

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                var content = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    // Body is NOT logged (may contain sensitive data). Status code is enough to diagnose.
                    _logger.LogWarning("Failed to fetch page accounts (HTTP {Status}). Using the stored token directly.", (int)response.StatusCode);
                    return userToken;
                }

                using var doc = JsonDocument.Parse(content);
                var dataArray = doc.RootElement.GetProperty("data");

                foreach (var page in dataArray.EnumerateArray())
                {
                    var id = page.GetProperty("id").GetString();
                    if (id == pageId)
                    {
                        var pageToken = page.GetProperty("access_token").GetString();
                        _logger.LogInformation("Found matching page token for page {PageId}", pageId);
                        return pageToken;
                    }
                }

                _logger.LogWarning("Page ID {PageId} not found in /me/accounts. Falling back to the stored token.", pageId);
                return userToken;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resolving page access token");
                return account.AccessToken;
            }
        }
    }
}