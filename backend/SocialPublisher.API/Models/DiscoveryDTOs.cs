using System.Collections.Generic;

namespace SocialPublisher.API.Models
{
    /// <summary>
    /// Represents an asset (Facebook Page, Instagram Profile, LinkedIn Company/Profile)
    /// discovered during OAuth authorization that can be linked to a client workspace.
    /// </summary>
    public class DiscoveredSocialAssetDto
    {
        public string ExternalId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty; // facebook, instagram, linkedin
        public string? Category { get; set; }
        public string? AvatarUrl { get; set; }
        public string? AccessToken { get; set; } // Page-specific access token for Facebook, or user token for IG/LinkedIn
        public bool IsAlreadyConnected { get; set; }
        public int? ConnectedClientId { get; set; }
        public string? ConnectedClientName { get; set; }
    }

    /// <summary>
    /// Discovery response returned after OAuth completes or during account discovery.
    /// </summary>
    public class OAuthDiscoveryResponseDto
    {
        public int ConnectionId { get; set; }
        public string Provider { get; set; } = string.Empty;
        public string? ProviderUserName { get; set; }
        public List<DiscoveredSocialAssetDto> DiscoveredAssets { get; set; } = new();
    }

    /// <summary>
    /// Request DTO for binding discovered assets to a specific client workspace (Zoho Social flow).
    /// </summary>
    public class BindSocialAccountRequest
    {
        public int ClientId { get; set; }
        public int ConnectionId { get; set; }
        public string Platform { get; set; } = string.Empty;
        public string ExternalAccountId { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public string? AccessToken { get; set; }
    }
}
