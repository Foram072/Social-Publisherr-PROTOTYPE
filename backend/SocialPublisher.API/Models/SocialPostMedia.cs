using System.ComponentModel.DataAnnotations;

namespace SocialPublisher.API.Models
{
    public class SocialPostMedia
    {
        [Key]
        public int Id { get; set; }
        public int? PostId { get; set; }
        public SocialPost? Post { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string FileUrl { get; set; } = string.Empty;
        public string MediaType { get; set; } = string.Empty; // e.g., Image, Video
    }
}
