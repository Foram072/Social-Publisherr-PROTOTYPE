using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SocialPublisher.API.Models
{
    /// <summary>
    /// Represents an authorized OAuth connection with a social provider (Meta, LinkedIn, Google/YouTube).
    /// Holds the user/admin credentials and token lifecycle without coupling to specific pages/channels.
    /// </summary>
    public class SocialConnection
    {
        [Key]
        public int Id { get; set; }

        public int? ClientId { get; set; }
        [ForeignKey("ClientId")]
        public SocialClient? Client { get; set; }

        [Required]
        [MaxLength(50)]
        public string Provider { get; set; } = string.Empty; // e.g. facebook, instagram, linkedin, youtube, google

        [MaxLength(255)]
        public string? ProviderUserId { get; set; } // The provider user/member ID who authorized the app

        [MaxLength(255)]
        public string? ProviderUserName { get; set; } // e.g. "John Doe"

        [MaxLength(255)]
        public string? ProviderUserEmail { get; set; }

        public string AccessToken { get; set; } = string.Empty;

        public string? RefreshToken { get; set; }

        public DateTime? TokenExpiresAt { get; set; }

        public string? GrantedScopes { get; set; }

        public bool IsActive { get; set; } = true;

        public string? OAuthState { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        // Navigation property: one OAuth connection can yield multiple connected accounts (e.g., multiple Pages, IG accounts)
        public ICollection<SocialAccount> Accounts { get; set; } = new List<SocialAccount>();
    }
}
