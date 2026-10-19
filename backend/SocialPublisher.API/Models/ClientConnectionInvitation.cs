using System;
using System.ComponentModel.DataAnnotations;

namespace SocialPublisher.API.Models
{
    /// <summary>
    /// Represents an invitation token sent by an agency to a client
    /// to connect their own social media accounts directly to a brand workspace.
    /// </summary>
    public class ClientConnectionInvitation
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int ClientId { get; set; }
        public SocialClient? Client { get; set; }

        [Required]
        [MaxLength(100)]
        public string Token { get; set; } = Guid.NewGuid().ToString("N");

        [MaxLength(255)]
        public string? RecipientEmail { get; set; }

        [MaxLength(255)]
        public string? RecipientName { get; set; }

        public string? Message { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddDays(7);

        public bool IsUsed { get; set; } = false;

        public DateTime? UsedAt { get; set; }

        public string CreatedBy { get; set; } = "Agency Admin";
    }

    /// <summary>
    /// Represents post approval audit logs and feedback history.
    /// </summary>
    public class SocialPostApproval
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int PostId { get; set; }
        public SocialPost? Post { get; set; }

        [Required]
        [MaxLength(50)]
        public string Action { get; set; } = "Submitted"; // Submitted, Approved, Rejected

        public string? ReviewerName { get; set; }

        public string? Comments { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
