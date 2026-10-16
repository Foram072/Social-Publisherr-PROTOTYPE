using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SocialPublisher.API.Models
{
    public class SocialPost
    {
        [Key]
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string Status { get; set; } = "Draft"; // e.g., Draft, Processing, PartiallyPublished, Published, Failed, Scheduled
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ScheduledAt { get; set; }
        public string? ScheduledPlatforms { get; set; }
        public int? ClientId { get; set; }
        public SocialClient? Client { get; set; }

        public ICollection<SocialPostMedia> Media { get; set; } = new List<SocialPostMedia>();
        public ICollection<SocialPostPublish> PublishResults { get; set; } = new List<SocialPostPublish>();
    }
}
