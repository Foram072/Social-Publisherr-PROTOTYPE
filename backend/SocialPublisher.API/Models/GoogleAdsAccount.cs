using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SocialPublisher.API.Models
{
    /// <summary>
    /// Represents a connected Google Ads customer account.
    /// Stored separately from a YouTube channel because the OAuth scopes differ.
    /// </summary>
    public class GoogleAdsAccount
    {
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// Google Ads Customer ID (e.g. "123-456-7890").
        /// </summary>
        public string CustomerId { get; set; } = string.Empty;

        /// <summary>
        /// Human readable name of the account (as returned by the API).
        /// </summary>
        public string AccountName { get; set; } = string.Empty;

        /// <summary>
        /// Currency code used by the account (USD, EUR, …).
        /// </summary>
        public string CurrencyCode { get; set; } = "USD";

        /// <summary>
        /// Time‑zone of the account (e.g. "America/New_York").
        /// </summary>
        public string TimeZone { get; set; } = string.Empty;

        /// <summary>
        /// OAuth access token (encrypted in the DB – see the TokenEncryption service).
        /// </summary>
        public string AccessToken { get; set; } = string.Empty;

        /// <summary>
        /// Refresh token that can be used to obtain a new access token.
        /// </summary>
        public string RefreshToken { get; set; } = string.Empty;

        /// <summary>
        /// When the access token expires (UTC).
        /// </summary>
        public DateTime? TokenExpiresAt { get; set; }

        /// <summary>
        /// Foreign key to the client / brand workspace that owns this account.
        /// </summary>
        public int ClientId { get; set; }
        public SocialClient Client { get; set; } = null!;

        /// <summary>
        /// Marks whether the connection is active. If false the user must reconnect.
        /// </summary>
        public bool IsConnected { get; set; }
    }
}
