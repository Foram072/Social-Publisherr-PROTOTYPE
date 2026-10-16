using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SocialPublisher.API.Models
{
    /// <summary>
    /// Represents an agency client workspace / brand (e.g., "ABC Interiors", "Nike EMEA").
    /// In Zoho Social / multi-tenant social tools, each client workspace groups its own connected social accounts.
    /// </summary>
    public class SocialClient
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Description { get; set; }

        [MaxLength(255)]
        public string? TimeZone { get; set; } = "UTC";

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Organization / Portal Link (Zoho Social)
        public int? OrganizationId { get; set; }
        public Organization? Organization { get; set; }

        // Navigation properties
        public ICollection<SocialConnection> Connections { get; set; } = new List<SocialConnection>();
        public ICollection<SocialAccount> Accounts { get; set; } = new List<SocialAccount>();
        public ICollection<SocialPost> Posts { get; set; } = new List<SocialPost>();
    }
}
