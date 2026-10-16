using System;
using System.ComponentModel.DataAnnotations;

namespace SocialPublisher.API.Models
{
    public class SocialPostPublish
    {
        [Key]
        public int Id { get; set; }
        public int PostId { get; set; }
        public SocialPost Post { get; set; } = null!;
        
        public int SocialAccountId { get; set; }
        public SocialAccount SocialAccount { get; set; } = null!;

        public string Status { get; set; } = "Pending"; // e.g. Pending, Published, Failed
        public string? ExternalPostId { get; set; }
        public string? PostUrl { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime? PublishedAt { get; set; }
    }
}
