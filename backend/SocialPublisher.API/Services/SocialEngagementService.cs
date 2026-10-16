using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SocialPublisher.API.Data;
using SocialPublisher.API.Models;

namespace SocialPublisher.API.Services
{
    public interface ISocialEngagementService
    {
        Task<List<PostCommentDto>> GetPostCommentsAsync(int postId, CancellationToken cancellationToken);
        Task<ReplyCommentResponse> ReplyToCommentAsync(string commentId, ReplyCommentRequest request, CancellationToken cancellationToken);
    }

    public class SocialEngagementService : ISocialEngagementService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<SocialEngagementService> _logger;

        public SocialEngagementService(
            ApplicationDbContext dbContext,
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<SocialEngagementService> logger)
        {
            _dbContext = dbContext;
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<List<PostCommentDto>> GetPostCommentsAsync(int postId, CancellationToken cancellationToken)
        {
            var post = await _dbContext.SocialPosts
                .Include(p => p.PublishResults)
                    .ThenInclude(r => r.SocialAccount)
                .FirstOrDefaultAsync(p => p.Id == postId, cancellationToken);

            if (post == null)
                throw new ArgumentException($"Post with ID {postId} not found.");

            var allComments = new List<PostCommentDto>();

            foreach (var publish in post.PublishResults.Where(r => r.Status == "Published" && !string.IsNullOrWhiteSpace(r.ExternalPostId)))
            {
                var platform = publish.SocialAccount.Platform.ToLowerInvariant();
                var account = publish.SocialAccount;

                try
                {
                    switch (platform)
                    {
                        case "facebook":
                            allComments.AddRange(await GetFacebookCommentsAsync(publish.ExternalPostId!, account, cancellationToken));
                            break;

                        case "instagram":
                            allComments.AddRange(await GetInstagramCommentsAsync(publish.ExternalPostId!, account, cancellationToken));
                            break;

                        case "linkedin":
                            allComments.AddRange(await GetLinkedInCommentsAsync(publish.ExternalPostId!, account, cancellationToken));
                            break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not fetch comments for {Platform} post {ExtId}", platform, publish.ExternalPostId);
                }
            }

            return allComments.OrderByDescending(c => c.CreatedTime ?? DateTime.MinValue).ToList();
        }

        public async Task<ReplyCommentResponse> ReplyToCommentAsync(string commentId, ReplyCommentRequest request, CancellationToken cancellationToken)
        {
            var platform = request.Platform.ToLowerInvariant().Trim();
            var account = await _dbContext.SocialAccounts.FirstOrDefaultAsync(a => a.Platform == platform, cancellationToken);

            if (account == null || !account.IsConnected)
                return new ReplyCommentResponse { Success = false, Message = $"Account for {platform} is not connected." };

            switch (platform)
            {
                case "facebook":
                    return await ReplyFacebookCommentAsync(commentId, request.Message, account, cancellationToken);

                case "instagram":
                    return await ReplyInstagramCommentAsync(commentId, request.Message, account, cancellationToken);

                case "linkedin":
                    return await ReplyLinkedInCommentAsync(commentId, request.Message, account, cancellationToken);

                default:
                    return new ReplyCommentResponse { Success = false, Message = $"Platform {platform} is not supported." };
            }
        }

        private async Task<List<PostCommentDto>> GetFacebookCommentsAsync(string postId, SocialAccount account, CancellationToken cancellationToken)
        {
            var list = new List<PostCommentDto>();
            var token = account.AccessToken;
            if (string.IsNullOrWhiteSpace(token)) return list;

            var url = $"https://graph.facebook.com/v25.0/{postId}/comments?fields=id,from,message,created_time,like_count,comments&access_token={token}";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) return list;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    var comment = new PostCommentDto
                    {
                        CommentId = item.GetProperty("id").GetString() ?? "",
                        Platform = "facebook",
                        Message = item.TryGetProperty("message", out var msg) ? msg.GetString() ?? "" : "",
                        CreatedTime = item.TryGetProperty("created_time", out var ct) && ct.TryGetDateTime(out var dt) ? dt : null,
                        LikeCount = item.TryGetProperty("like_count", out var lc) ? lc.GetInt64() : 0
                    };

                    if (item.TryGetProperty("from", out var from) && from.TryGetProperty("name", out var fn))
                    {
                        comment.AuthorName = fn.GetString() ?? "Facebook User";
                    }

                    list.Add(comment);
                }
            }

            return list;
        }

        private async Task<List<PostCommentDto>> GetInstagramCommentsAsync(string mediaId, SocialAccount account, CancellationToken cancellationToken)
        {
            var list = new List<PostCommentDto>();
            var token = account.AccessToken;
            if (string.IsNullOrWhiteSpace(token)) return list;

            var url = $"https://graph.facebook.com/v25.0/{mediaId}/comments?fields=id,text,timestamp,username,like_count&access_token={token}";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) return list;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    var comment = new PostCommentDto
                    {
                        CommentId = item.GetProperty("id").GetString() ?? "",
                        Platform = "instagram",
                        Message = item.TryGetProperty("text", out var txt) ? txt.GetString() ?? "" : "",
                        AuthorName = item.TryGetProperty("username", out var u) ? u.GetString() ?? "Instagram User" : "Instagram User",
                        CreatedTime = item.TryGetProperty("timestamp", out var ts) && ts.TryGetDateTime(out var dt) ? dt : null,
                        LikeCount = item.TryGetProperty("like_count", out var lc) ? lc.GetInt64() : 0
                    };

                    list.Add(comment);
                }
            }

            return list;
        }

        private async Task<List<PostCommentDto>> GetLinkedInCommentsAsync(string urn, SocialAccount account, CancellationToken cancellationToken)
        {
            var list = new List<PostCommentDto>();
            var token = account.AccessToken;
            if (string.IsNullOrWhiteSpace(token)) return list;

            var formattedUrn = urn.StartsWith("urn:li:") ? urn : $"urn:li:share:{urn}";
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.linkedin.com/v2/socialActions/{Uri.EscapeDataString(formattedUrn)}/comments");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("X-Restli-Protocol-Version", "2.0.0");

            var response = await _httpClient.SendAsync(req, cancellationToken);
            if (!response.IsSuccessStatusCode) return list;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("elements", out var elements) && elements.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in elements.EnumerateArray())
                {
                    var comment = new PostCommentDto
                    {
                        CommentId = item.TryGetProperty("$URN", out var u) ? u.GetString() ?? "" : (item.TryGetProperty("id", out var id) ? id.GetString() ?? "" : ""),
                        Platform = "linkedin",
                        AuthorName = item.TryGetProperty("actor", out var act) ? act.GetString() ?? "LinkedIn Member" : "LinkedIn Member",
                        Message = item.TryGetProperty("message", out var m) && m.TryGetProperty("text", out var t) ? t.GetString() ?? "" : ""
                    };
                    list.Add(comment);
                }
            }

            return list;
        }

        private async Task<ReplyCommentResponse> ReplyFacebookCommentAsync(string commentId, string message, SocialAccount account, CancellationToken cancellationToken)
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["message"] = message,
                ["access_token"] = account.AccessToken
            });

            var response = await _httpClient.PostAsync($"https://graph.facebook.com/v25.0/{commentId}/comments", content, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);
                return new ReplyCommentResponse
                {
                    Success = true,
                    ReplyId = doc.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null,
                    Message = "Reply posted successfully to Facebook."
                };
            }

            var err = await response.Content.ReadAsStringAsync(cancellationToken);
            return new ReplyCommentResponse { Success = false, Message = $"Facebook error: {err}" };
        }

        private async Task<ReplyCommentResponse> ReplyInstagramCommentAsync(string commentId, string message, SocialAccount account, CancellationToken cancellationToken)
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["message"] = message,
                ["access_token"] = account.AccessToken
            });

            var response = await _httpClient.PostAsync($"https://graph.facebook.com/v25.0/{commentId}/replies", content, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);
                return new ReplyCommentResponse
                {
                    Success = true,
                    ReplyId = doc.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null,
                    Message = "Reply posted successfully to Instagram."
                };
            }

            var err = await response.Content.ReadAsStringAsync(cancellationToken);
            return new ReplyCommentResponse { Success = false, Message = $"Instagram error: {err}" };
        }

        private async Task<ReplyCommentResponse> ReplyLinkedInCommentAsync(string commentUrn, string message, SocialAccount account, CancellationToken cancellationToken)
        {
            // Parse parent post URN from comment or send reply to socialActions
            using var req = new HttpRequestMessage(HttpMethod.Post, $"https://api.linkedin.com/v2/socialActions/{Uri.EscapeDataString(commentUrn)}/comments");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
            req.Headers.Add("X-Restli-Protocol-Version", "2.0.0");

            var payload = new
            {
                actor = $"urn:li:person:{account.ExternalAccountId}",
                message = new { text = message }
            };

            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(req, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return new ReplyCommentResponse { Success = true, Message = "Reply posted successfully to LinkedIn." };
            }

            var err = await response.Content.ReadAsStringAsync(cancellationToken);
            return new ReplyCommentResponse { Success = false, Message = $"LinkedIn error: {err}" };
        }
    }
}
