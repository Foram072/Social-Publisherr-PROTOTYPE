using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
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
    public interface ISocialAnalyticsService
    {
        Task<PostAnalyticsDto> GetPostAnalyticsAsync(int postId, CancellationToken cancellationToken);
        Task<BrandAnalyticsOverviewDto> GetBrandOverviewAnalyticsAsync(int clientId, CancellationToken cancellationToken);
    }

    public class SocialAnalyticsService : ISocialAnalyticsService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<SocialAnalyticsService> _logger;

        public SocialAnalyticsService(
            ApplicationDbContext dbContext,
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<SocialAnalyticsService> logger)
        {
            _dbContext = dbContext;
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<PostAnalyticsDto> GetPostAnalyticsAsync(int postId, CancellationToken cancellationToken)
        {
            var post = await _dbContext.SocialPosts
                .Include(p => p.PublishResults)
                    .ThenInclude(r => r.SocialAccount)
                .FirstOrDefaultAsync(p => p.Id == postId, cancellationToken);

            if (post == null)
                throw new ArgumentException($"Post with ID {postId} not found.");

            var dto = new PostAnalyticsDto
            {
                PostId = post.Id,
                Title = post.Title,
                Content = post.Content,
                CreatedAt = post.CreatedAt,
                PlatformMetrics = new List<PlatformMetricDto>()
            };

            foreach (var publish in post.PublishResults.Where(r => r.Status == "Published" && !string.IsNullOrWhiteSpace(r.ExternalPostId)))
            {
                var platform = publish.SocialAccount.Platform.ToLowerInvariant();
                var account = publish.SocialAccount;
                var metric = new PlatformMetricDto
                {
                    Platform = platform,
                    ExternalPostId = publish.ExternalPostId,
                    PostUrl = publish.PostUrl
                };

                try
                {
                    switch (platform)
                    {
                        case "facebook":
                            await PopulateFacebookMetricsAsync(metric, publish.ExternalPostId!, account, cancellationToken);
                            break;

                        case "instagram":
                            await PopulateInstagramMetricsAsync(metric, publish.ExternalPostId!, account, cancellationToken);
                            break;

                        case "linkedin":
                            await PopulateLinkedInMetricsAsync(metric, publish.ExternalPostId!, account, cancellationToken);
                            break;

                        case "pinterest":
                            await PopulatePinterestMetricsAsync(metric, publish.ExternalPostId!, account, cancellationToken);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not fetch real-time analytics for {Platform} post {ExtId}", platform, publish.ExternalPostId);
                    metric.Notes = $"Insights unavailable: {ex.Message}";
                }

                dto.PlatformMetrics.Add(metric);
            }

            return dto;
        }

        public async Task<BrandAnalyticsOverviewDto> GetBrandOverviewAnalyticsAsync(int clientId, CancellationToken cancellationToken)
        {
            var client = await _dbContext.SocialClients
                .Include(c => c.Accounts)
                .Include(c => c.Posts)
                    .ThenInclude(p => p.PublishResults)
                        .ThenInclude(r => r.SocialAccount)
                .FirstOrDefaultAsync(c => c.Id == clientId, cancellationToken);

            if (client == null)
                throw new ArgumentException($"Client workspace with ID {clientId} not found.");

            var posts = client.Posts.ToList();

            var overview = new BrandAnalyticsOverviewDto
            {
                ClientId = client.Id,
                ClientName = client.Name,
                TotalPosts = posts.Count,
                PublishedPosts = posts.Count(p => p.Status == "Published" || p.Status == "PartiallyPublished"),
                ScheduledPosts = posts.Count(p => p.Status == "Scheduled")
            };

            // Aggregate metrics by platform for this brand
            var platforms = new[] { "facebook", "instagram", "linkedin" };
            foreach (var platform in platforms)
            {
                var boundAccount = client.Accounts.FirstOrDefault(a => a.Platform.ToLower() == platform && a.IsConnected);
                var platformPosts = posts
                    .SelectMany(p => p.PublishResults)
                    .Where(r => r.SocialAccount != null && r.SocialAccount.Platform.ToLower() == platform && r.Status == "Published")
                    .ToList();

                var pSummary = new BrandPlatformSummaryDto
                {
                    Platform = platform,
                    AccountName = boundAccount?.AccountName ?? $"{char.ToUpper(platform[0]) + platform[1..]} Account",
                    IsConnected = boundAccount != null && boundAccount.IsConnected,
                    PostsCount = platformPosts.Count
                };

                // Fetch real-time sample or estimate metrics for recent published posts
                foreach (var pub in platformPosts.Take(10))
                {
                    if (string.IsNullOrWhiteSpace(pub.ExternalPostId) || boundAccount == null) continue;
                    var dummyMetric = new PlatformMetricDto();
                    try
                    {
                        if (platform == "facebook")
                            await PopulateFacebookMetricsAsync(dummyMetric, pub.ExternalPostId, boundAccount, cancellationToken);
                        else if (platform == "instagram")
                            await PopulateInstagramMetricsAsync(dummyMetric, pub.ExternalPostId, boundAccount, cancellationToken);
                        else if (platform == "linkedin")
                            await PopulateLinkedInMetricsAsync(dummyMetric, pub.ExternalPostId, boundAccount, cancellationToken);

                        pSummary.Likes += dummyMetric.Likes;
                        pSummary.CommentsCount += dummyMetric.CommentsCount;
                        pSummary.Shares += dummyMetric.Shares;
                        pSummary.Impressions += dummyMetric.Impressions;
                        pSummary.Reach += dummyMetric.Reach;
                    }
                    catch
                    {
                        // Fallback gracefully if API rate limited
                    }
                }

                overview.TotalLikes += pSummary.Likes;
                overview.TotalComments += pSummary.CommentsCount;
                overview.TotalShares += pSummary.Shares;
                overview.TotalImpressions += pSummary.Impressions;
                overview.TotalReach += pSummary.Reach;

                overview.Platforms.Add(pSummary);
            }

            overview.TotalEngagement = overview.TotalLikes + overview.TotalComments + overview.TotalShares;

            // Paid Advertising Campaigns (Read-Only sample for CEO Phase 5)
            // Shows Meta Ads & LinkedIn Ads structure
            overview.PaidAdCampaigns = new List<PaidAdMetricsDto>
            {
                new PaidAdMetricsDto
                {
                    Platform = "meta",
                    CampaignName = $"{client.Name} - Awareness & Leads Q4",
                    Status = "Active",
                    Spend = 450.00m,
                    Impressions = 32400,
                    Clicks = 1120,
                    Leads = 38,
                    Conversions = 22,
                    ROAS = 3.4m,
                    Currency = "USD"
                },
                new PaidAdMetricsDto
                {
                    Platform = "linkedin",
                    CampaignName = $"{client.Name} - B2B Retargeting",
                    Status = "Active",
                    Spend = 320.00m,
                    Impressions = 14200,
                    Clicks = 460,
                    Leads = 19,
                    Conversions = 11,
                    ROAS = 2.8m,
                    Currency = "USD"
                }
            };

            return overview;
        }

        private async Task PopulateFacebookMetricsAsync(PlatformMetricDto metric, string postId, SocialAccount account, CancellationToken cancellationToken)
        {
            var token = account.AccessToken;
            if (string.IsNullOrWhiteSpace(token)) return;

            // Fetch engagement counts: likes.summary(true), comments.summary(true), shares
            var url = $"https://graph.facebook.com/v25.0/{postId}?fields=shares,reactions.summary(total_count),comments.summary(total_count)&access_token={token}";
            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("reactions", out var reactions) &&
                    reactions.TryGetProperty("summary", out var rSummary) &&
                    rSummary.TryGetProperty("total_count", out var rCount))
                {
                    metric.Likes = rCount.GetInt64();
                }

                if (root.TryGetProperty("comments", out var comments) &&
                    comments.TryGetProperty("summary", out var cSummary) &&
                    cSummary.TryGetProperty("total_count", out var cCount))
                {
                    metric.CommentsCount = cCount.GetInt64();
                }

                if (root.TryGetProperty("shares", out var shares) &&
                    shares.TryGetProperty("count", out var sCount))
                {
                    metric.Shares = sCount.GetInt64();
                }
            }

            // Fetch insights: post_impressions, post_engaged_users
            try
            {
                var insightUrl = $"https://graph.facebook.com/v25.0/{postId}/insights?metric=post_impressions,post_engaged_users&access_token={token}";
                var insightRes = await _httpClient.GetAsync(insightUrl, cancellationToken);
                if (insightRes.IsSuccessStatusCode)
                {
                    var insightJson = await insightRes.Content.ReadAsStringAsync(cancellationToken);
                    using var iDoc = JsonDocument.Parse(insightJson);
                    if (iDoc.RootElement.TryGetProperty("data", out var dataArr) && dataArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in dataArr.EnumerateArray())
                        {
                            var name = item.GetProperty("name").GetString();
                            var val = item.GetProperty("values")[0].GetProperty("value").GetInt64();
                            if (name == "post_impressions") metric.Impressions = val;
                            if (name == "post_engaged_users") metric.Reach = val;
                        }
                    }
                }
            }
            catch { /* Ignore insights permissions restrictions if page token lacks read_insights */ }
        }

        private async Task PopulateInstagramMetricsAsync(PlatformMetricDto metric, string mediaId, SocialAccount account, CancellationToken cancellationToken)
        {
            var token = account.AccessToken;
            if (string.IsNullOrWhiteSpace(token)) return;

            // Fetch basic metrics: like_count, comments_count
            var url = $"https://graph.facebook.com/v25.0/{mediaId}?fields=like_count,comments_count&access_token={token}";
            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("like_count", out var likes))
                    metric.Likes = likes.GetInt64();

                if (root.TryGetProperty("comments_count", out var comments))
                    metric.CommentsCount = comments.GetInt64();
            }

            // Fetch Instagram Insights: impressions, reach, saved
            try
            {
                var insightUrl = $"https://graph.facebook.com/v25.0/{mediaId}/insights?metric=impressions,reach,saved&access_token={token}";
                var insightRes = await _httpClient.GetAsync(insightUrl, cancellationToken);
                if (insightRes.IsSuccessStatusCode)
                {
                    var insightJson = await insightRes.Content.ReadAsStringAsync(cancellationToken);
                    using var iDoc = JsonDocument.Parse(insightJson);
                    if (iDoc.RootElement.TryGetProperty("data", out var dataArr) && dataArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in dataArr.EnumerateArray())
                        {
                            var name = item.GetProperty("name").GetString();
                            var val = item.GetProperty("values")[0].GetProperty("value").GetInt64();
                            if (name == "impressions") metric.Impressions = val;
                            if (name == "reach") metric.Reach = val;
                        }
                    }
                }
            }
            catch { /* Insights might require professional account or specific permissions */ }
        }

        private async Task PopulateLinkedInMetricsAsync(PlatformMetricDto metric, string urn, SocialAccount account, CancellationToken cancellationToken)
        {
            var token = account.AccessToken;
            if (string.IsNullOrWhiteSpace(token)) return;

            // Normalize URN
            var formattedUrn = urn.Trim();
            if (!formattedUrn.StartsWith("urn:li:"))
                formattedUrn = $"urn:li:share:{formattedUrn}";

            // ── Attempt 1: Direct share/ugcPost statistics ──────────────────────────
            string apiUrl;
            if (formattedUrn.StartsWith("urn:li:ugcPost:"))
            {
                var ugcId = formattedUrn.Substring("urn:li:ugcPost:".Length);
                apiUrl = $"https://api.linkedin.com/v2/ugcPosts/{ugcId}?fields=totalShareStatistics";
            }
            else
            {
                var shareId = formattedUrn.Substring("urn:li:share:".Length);
                apiUrl = $"https://api.linkedin.com/v2/shares/{shareId}?fields=totalShareStatistics";
            }

            _logger.LogInformation("LinkedIn analytics attempt 1: {Url}", apiUrl);
            var (ok1, body1) = await TryLinkedInGetAsync(apiUrl, token, cancellationToken);

            if (ok1 && TryExtractShareStatistics(body1, metric))
                return; // ✅ Got real data

            // ── Attempt 2: Organization entity share statistics ──────────────────────
            var companyId = _configuration["SocialMedia:LinkedIn:CompanyId"];
            if (!string.IsNullOrWhiteSpace(companyId))
            {
                var encodedOrg = Uri.EscapeDataString($"urn:li:organization:{companyId}");
                var encodedShare = Uri.EscapeDataString(formattedUrn);
                var orgUrl = $"https://api.linkedin.com/v2/organizationalEntityShareStatistics?q=organizationalEntity&organizationalEntity={encodedOrg}&shares[0]={encodedShare}";

                _logger.LogInformation("LinkedIn analytics attempt 2 (org stats): {Url}", orgUrl);
                var (ok2, body2) = await TryLinkedInGetAsync(orgUrl, token, cancellationToken);

                if (ok2)
                {
                    try
                    {
                        using var doc2 = JsonDocument.Parse(body2);
                        var root2 = doc2.RootElement;
                        if (root2.TryGetProperty("elements", out var elements) && elements.GetArrayLength() > 0)
                        {
                            var firstEl = elements[0];
                            if (firstEl.TryGetProperty("totalShareStatistics", out var orgStats))
                            {
                                if (orgStats.TryGetProperty("impressionCount", out var imp)) metric.Impressions = imp.GetInt64();
                                if (orgStats.TryGetProperty("likeCount", out var lk)) metric.Likes = lk.GetInt64();
                                if (orgStats.TryGetProperty("commentCount", out var cmt)) metric.CommentsCount = cmt.GetInt64();
                                if (orgStats.TryGetProperty("shareCount", out var sh)) metric.Shares = sh.GetInt64();
                                if (orgStats.TryGetProperty("clickCount", out var cl)) metric.Clicks = cl.GetInt64();
                                if (orgStats.TryGetProperty("uniqueImpressionsCount", out var reach)) metric.Reach = reach.GetInt64();
                                return; // ✅ Got org data
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse org share statistics response");
                    }
                }
            }

            // ── No data available: guide user to re-authorize ────────────────────────
            _logger.LogWarning("LinkedIn analytics unavailable for {Urn}. Token lacks r_member_social or r_organization_social scope.", formattedUrn);
            metric.Notes = "Analytics unavailable: your LinkedIn token needs the 'r_member_social' scope. Please disconnect and reconnect your LinkedIn account in the Accounts tab to get real-time analytics.";
        }

        private async Task<(bool Success, string Body)> TryLinkedInGetAsync(string url, string token, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("X-Restli-Protocol-Version", "2.0.0");

            var response = await _httpClient.SendAsync(req, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogInformation("LinkedIn GET [{Status}] {Url}: {Body}", (int)response.StatusCode, url, body.Length > 500 ? body[..500] : body);
            return (response.IsSuccessStatusCode, body);
        }

        private static bool TryExtractShareStatistics(string json, PlatformMetricDto metric)
        {
            if (string.IsNullOrWhiteSpace(json)) return false;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("totalShareStatistics", out var stats)) return false;

                if (stats.TryGetProperty("impressionCount", out var imp)) metric.Impressions = imp.GetInt64();
                if (stats.TryGetProperty("likeCount", out var lk)) metric.Likes = lk.GetInt64();
                if (stats.TryGetProperty("commentCount", out var cmt)) metric.CommentsCount = cmt.GetInt64();
                if (stats.TryGetProperty("shareCount", out var sh)) metric.Shares = sh.GetInt64();
                if (stats.TryGetProperty("clickCount", out var cl)) metric.Clicks = cl.GetInt64();
                if (stats.TryGetProperty("uniqueImpressionsCount", out var reach)) metric.Reach = reach.GetInt64();
                return true;
            }
            catch { return false; }
        }

        private async Task PopulatePinterestMetricsAsync(PlatformMetricDto metric, string pinId, SocialAccount account, CancellationToken cancellationToken)
        {
            var token = account.AccessToken;
            if (string.IsNullOrWhiteSpace(token)) return;

            // Pinterest v5 Pin analytics endpoint: GET https://api.pinterest.com/v5/pins/{pin_id}/analytics
            // Or get pin details: GET https://api.pinterest.com/v5/pins/{pin_id}
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.pinterest.com/v5/pins/{pinId}");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var response = await _httpClient.SendAsync(req, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    // Pinterest v5 pin metrics summary (PIN_CLICK, IMPRESSION, SAVE)
                    if (root.TryGetProperty("pin_metrics", out var pinMetrics))
                    {
                        if (pinMetrics.TryGetProperty("lifetime_metrics", out var lifetime))
                        {
                            if (lifetime.TryGetProperty("impression", out var imp)) metric.Impressions = imp.GetInt64();
                            if (lifetime.TryGetProperty("pin_click", out var cl)) metric.Clicks = cl.GetInt64();
                            if (lifetime.TryGetProperty("save", out var sv)) metric.Shares = sv.GetInt64();
                            if (lifetime.TryGetProperty("save_rate", out var _)) { }
                        }
                    }
                }
                else
                {
                    metric.Notes = $"Pinterest metrics response: {(int)response.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not fetch Pinterest metrics for pin {PinId}", pinId);
            }
        }
    }
}
