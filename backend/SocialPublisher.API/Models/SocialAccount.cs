using System;
using System.ComponentModel.DataAnnotations;

namespace SocialPublisher.API.Models
{
    public class SocialAccount
    {
        [Key]
        public int Id { get; set; }
        public string Platform { get; set; } = string.Empty; // e.g. facebook, instagram, linkedin
        public string AccountName { get; set; } = string.Empty;
        public string ExternalAccountId { get; set; } = string.Empty;
        public string AccessToken { get; set; } = string.Empty;
        public DateTime? TokenExpiresAt { get; set; }
        public bool IsConnected { get; set; }

        // ── OAuth flow fields (added for OAuth implementation) ──────────────
        // Temporary state token used during the OAuth redirect (CSRF protection)
        public string? OAuthState { get; set; }
        // Long-lived refresh token (LinkedIn provides one; Facebook uses long-lived tokens)
        public string? RefreshToken { get; set; }

        // ── Multi-tenant / Agency Client & OAuth Connection associations ──
        public int? ClientId { get; set; }
        public SocialClient? Client { get; set; }

        public int? ConnectionId { get; set; }
        public SocialConnection? Connection { get; set; }

        [MaxLength(500)]
        public string? AvatarUrl { get; set; }

        // Navigation property for related publish records
        public ICollection<SocialPostPublish> PublishResults { get; set; } = new List<SocialPostPublish>();
    }
}

