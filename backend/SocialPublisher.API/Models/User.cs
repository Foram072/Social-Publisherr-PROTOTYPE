using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SocialPublisher.API.Models
{
    /// <summary>
    /// Represents an Organization / Portal (Agency Workspace), e.g. "Acme Marketing Agency".
    /// Zoho Social hierarchy: Organization -> Brands (SocialClients) -> Social Channels.
    /// </summary>
    public class Organization
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? Country { get; set; }

        [MaxLength(100)]
        public string? TimeZone { get; set; } = "UTC";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public ICollection<User> Members { get; set; } = new List<User>();
        public ICollection<SocialClient> Brands { get; set; } = new List<SocialClient>();
    }

    /// <summary>
    /// User account in Wiseliy Social.
    /// </summary>
    public class User
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(150)]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [MaxLength(150)]
        public string Email { get; set; } = string.Empty;

        public string? PasswordHash { get; set; }

        public string Salt { get; set; } = string.Empty;

        public string? GoogleId { get; set; }

        public string Role { get; set; } = "Owner"; // Owner, Admin, Member

        public int? OrganizationId { get; set; }
        public Organization? Organization { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
