using System;
using System.Collections.Generic;

namespace SocialPublisher.API.Models
{
    public class PostAnalyticsDto
    {
        public int PostId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public List<PlatformMetricDto> PlatformMetrics { get; set; } = new List<PlatformMetricDto>();
    }

    public class PlatformMetricDto
    {
        public string Platform { get; set; } = string.Empty;
        public string? ExternalPostId { get; set; }
        public string? PostUrl { get; set; }
        public long Impressions { get; set; }
        public long Reach { get; set; }
        public long Likes { get; set; }
        public long CommentsCount { get; set; }
        public long Shares { get; set; }
        public long Clicks { get; set; }
        public string? Notes { get; set; }
    }

    public class BrandAnalyticsOverviewDto
    {
        public int ClientId { get; set; }
        public string ClientName { get; set; } = string.Empty;
        public int TotalPosts { get; set; }
        public int PublishedPosts { get; set; }
        public int ScheduledPosts { get; set; }
        public long TotalImpressions { get; set; }
        public long TotalReach { get; set; }
        public long TotalEngagement { get; set; } // Likes + Comments + Shares
        public long TotalLikes { get; set; }
        public long TotalComments { get; set; }
        public long TotalShares { get; set; }
        public List<BrandPlatformSummaryDto> Platforms { get; set; } = new List<BrandPlatformSummaryDto>();
        public List<PaidAdMetricsDto> PaidAdCampaigns { get; set; } = new List<PaidAdMetricsDto>();
    }

    public class BrandPlatformSummaryDto
    {
        public string Platform { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public bool IsConnected { get; set; }
        public int PostsCount { get; set; }
        public long Impressions { get; set; }
        public long Reach { get; set; }
        public long Likes { get; set; }
        public long CommentsCount { get; set; }
        public long Shares { get; set; }
    }

    public class PaidAdMetricsDto
    {
        public string Platform { get; set; } = string.Empty;
        public string CampaignName { get; set; } = string.Empty;
        public string Status { get; set; } = "Active";
        public decimal Spend { get; set; }
        public long Impressions { get; set; }
        public long Clicks { get; set; }
        public decimal CTR => Impressions > 0 ? Math.Round(((decimal)Clicks / Impressions) * 100, 2) : 0;
        public decimal CPC => Clicks > 0 ? Math.Round(Spend / Clicks, 2) : 0;
        public long Leads { get; set; }
        public long Conversions { get; set; }
        public decimal ROAS { get; set; }
        public string Currency { get; set; } = "USD";
    }

    public class PostCommentDto
    {
        public string CommentId { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public string AuthorName { get; set; } = string.Empty;
        public string? AuthorProfileUrl { get; set; }
        public string Message { get; set; } = string.Empty;
        public DateTime? CreatedTime { get; set; }
        public long LikeCount { get; set; }
        public List<PostCommentDto> Replies { get; set; } = new List<PostCommentDto>();
    }

    public class ReplyCommentRequest
    {
        public string Platform { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    public class ReplyCommentResponse
    {
        public bool Success { get; set; }
        public string? ReplyId { get; set; }
        public string? Message { get; set; }
    }
}
