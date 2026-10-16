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
