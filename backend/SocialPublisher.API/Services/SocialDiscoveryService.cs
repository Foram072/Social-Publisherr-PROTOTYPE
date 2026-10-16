using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SocialPublisher.API.Data;
using SocialPublisher.API.Models;

namespace SocialPublisher.API.Services
{
    public interface ISocialDiscoveryService
    {
        Task<OAuthDiscoveryResponseDto> DiscoverAssetsAsync(int connectionId);
        Task<SocialAccount> BindAccountToClientAsync(BindSocialAccountRequest request);
    }

    public class SocialDiscoveryService : ISocialDiscoveryService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<SocialDiscoveryService> _logger;
        private const string GraphVersion = "v20.0";

        public SocialDiscoveryService(
            ApplicationDbContext context,
            IHttpClientFactory httpClientFactory,
            ILogger<SocialDiscoveryService> logger)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<OAuthDiscoveryResponseDto> DiscoverAssetsAsync(int connectionId)
        {
            var connection = await _context.SocialConnections.FirstOrDefaultAsync(c => c.Id == connectionId);
            if (connection == null)
                throw new ArgumentException($"Connection with ID {connectionId} not found.");

            var response = new OAuthDiscoveryResponseDto
            {
                ConnectionId = connection.Id,
                Provider = connection.Provider,
                ProviderUserName = connection.ProviderUserName
            };

            var existingAccounts = await _context.SocialAccounts
                .Include(a => a.Client)
                .ToListAsync();

            switch (connection.Provider.ToLowerInvariant())
            {
                case "facebook":
                case "instagram":
                    await DiscoverMetaAssetsAsync(connection, response, existingAccounts);
                    break;

                case "linkedin":
                    await DiscoverLinkedInAssetsAsync(connection, response, existingAccounts);
                    break;

                case "pinterest":
                    await DiscoverPinterestAssetsAsync(connection, response, existingAccounts);
                    break;

                default:
                    _logger.LogWarning("Provider {Provider} does not support asset discovery yet", connection.Provider);
                    break;
            }

            return response;
        }

        private async Task DiscoverMetaAssetsAsync(
            SocialConnection connection,
            OAuthDiscoveryResponseDto response,
            List<SocialAccount> existingAccounts)
        {
            var client = _httpClientFactory.CreateClient();
            using var req = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://graph.facebook.com/{GraphVersion}/me/accounts?fields=id,name,category,access_token,picture{{url}},instagram_business_account{{id,name,username,profile_picture_url}}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.AccessToken);

            var res = await client.SendAsync(req);
            var content = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning("Meta asset discovery failed: {Content}", content);
                return;
            }

            using var doc = JsonDocument.Parse(content);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return;

            foreach (var page in data.EnumerateArray())
            {
                var pageId = page.GetProperty("id").GetString() ?? "";
                var pageName = page.GetProperty("name").GetString() ?? "Facebook Page";
                var pageCategory = page.TryGetProperty("category", out var cat) ? cat.GetString() : null;
                var pageToken = page.TryGetProperty("access_token", out var pat) ? pat.GetString() : connection.AccessToken;
                
                string? pageAvatar = null;
                if (page.TryGetProperty("picture", out var pic) &&
                    pic.TryGetProperty("data", out var picData) &&
                    picData.TryGetProperty("url", out var picUrl))
                {
                    pageAvatar = picUrl.GetString();
                }

                var existingPage = existingAccounts.Find(a => a.Platform == "facebook" && a.ExternalAccountId == pageId);

                // Add Facebook Page asset
                response.DiscoveredAssets.Add(new DiscoveredSocialAssetDto
                {
                    Platform = "facebook",
                    ExternalId = pageId,
                    Name = pageName,
                    Category = pageCategory,
                    AvatarUrl = pageAvatar,
                    AccessToken = pageToken,
                    IsAlreadyConnected = existingPage != null && existingPage.IsConnected,
                    ConnectedClientId = existingPage?.ClientId,
                    ConnectedClientName = existingPage?.Client?.Name
                });

                // Check for linked Instagram Business Account
                if (page.TryGetProperty("instagram_business_account", out var igAccount) &&
                    igAccount.ValueKind == JsonValueKind.Object)
                {
                    var igId = igAccount.GetProperty("id").GetString() ?? "";
                    var igUsername = igAccount.TryGetProperty("username", out var u) ? u.GetString() : null;
                    var igName = igAccount.TryGetProperty("name", out var n) ? n.GetString() : igUsername ?? "Instagram Account";
                    var igPic = igAccount.TryGetProperty("profile_picture_url", out var pp) ? pp.GetString() : null;

                    var existingIg = existingAccounts.Find(a => a.Platform == "instagram" && a.ExternalAccountId == igId);

                    response.DiscoveredAssets.Add(new DiscoveredSocialAssetDto
                    {
                        Platform = "instagram",
                        ExternalId = igId,
                        Name = $"@{igUsername ?? igName}",
                        Category = $"Linked to Facebook Page: {pageName}",
                        AvatarUrl = igPic,
                        AccessToken = pageToken, // IG publishing uses the linked FB Page access token
                        IsAlreadyConnected = existingIg != null && existingIg.IsConnected,
                        ConnectedClientId = existingIg?.ClientId,
                        ConnectedClientName = existingIg?.Client?.Name
                    });
                }
            }
        }

        private async Task DiscoverLinkedInAssetsAsync(
            SocialConnection connection,
            OAuthDiscoveryResponseDto response,
            List<SocialAccount> existingAccounts)
        {
            var client = _httpClientFactory.CreateClient();

            // 1. Discover personal profile via userinfo
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.linkedin.com/v2/userinfo");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.AccessToken);

                var res = await client.SendAsync(req);
                var content = await res.Content.ReadAsStringAsync();

                if (res.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(content);
                    var sub = doc.RootElement.TryGetProperty("sub", out var s) ? s.GetString() : connection.ProviderUserId;
                    var name = doc.RootElement.TryGetProperty("name", out var nm) ? nm.GetString() : connection.ProviderUserName ?? "LinkedIn Profile";
                    var pic = doc.RootElement.TryGetProperty("picture", out var p) ? p.GetString() : null;

                    if (!string.IsNullOrEmpty(sub))
                    {
                        var existingLi = existingAccounts.Find(a => a.Platform == "linkedin" && a.ExternalAccountId == sub);

                        response.DiscoveredAssets.Add(new DiscoveredSocialAssetDto
                        {
                            Platform = "linkedin",
                            ExternalId = sub,
                            Name = $"{name} (Personal Profile)",
                            Category = "LinkedIn Profile",
                            AvatarUrl = pic,
                            AccessToken = connection.AccessToken,
                            IsAlreadyConnected = existingLi != null && existingLi.IsConnected,
                            ConnectedClientId = existingLi?.ClientId,
                            ConnectedClientName = existingLi?.Client?.Name
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not discover LinkedIn personal profile");
            }

            // 2. Discover Organization / Company Pages if permission is granted
            try
            {
                using var req = new HttpRequestMessage(
                    HttpMethod.Get,
                    "https://api.linkedin.com/v2/organizationalEntityAcls?q=roleAssignee&role=ADMINISTRATOR&state=APPROVED");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.AccessToken);
                req.Headers.Add("X-Restli-Protocol-Version", "2.0.0");

                var res = await client.SendAsync(req);
                var content = await res.Content.ReadAsStringAsync();

                if (res.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(content);
                    if (doc.RootElement.TryGetProperty("elements", out var elements))
                    {
                        foreach (var el in elements.EnumerateArray())
                        {
                            if (el.TryGetProperty("organizationalTarget", out var target))
                            {
                                var orgUrn = target.GetString() ?? ""; // e.g. urn:li:organization:12345
                                var orgId = orgUrn.Replace("urn:li:organization:", "");

                                var existingOrg = existingAccounts.Find(a => a.Platform == "linkedin" && (a.ExternalAccountId == orgUrn || a.ExternalAccountId == orgId));

                                response.DiscoveredAssets.Add(new DiscoveredSocialAssetDto
                                {
                                    Platform = "linkedin",
                                    ExternalId = orgId,
                                    Name = $"LinkedIn Organization ({orgId})",
                                    Category = "Company Page",
                                    AvatarUrl = null,
                                    AccessToken = connection.AccessToken,
                                    IsAlreadyConnected = existingOrg != null && existingOrg.IsConnected,
                                    ConnectedClientId = existingOrg?.ClientId,
                                    ConnectedClientName = existingOrg?.Client?.Name
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "LinkedIn organization discovery skipped or not permitted for this token");
            }
        }

        private async Task DiscoverPinterestAssetsAsync(
            SocialConnection connection,
            OAuthDiscoveryResponseDto response,
            List<SocialAccount> existingAccounts)
        {
            var client = _httpClientFactory.CreateClient();

            // 1. Fetch user account profile (GET https://api.pinterest.com/v5/user_account)
            string? username = null;
            string? profileImage = null;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.pinterest.com/v5/user_account");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.AccessToken);

                var res = await client.SendAsync(req);
                var content = await res.Content.ReadAsStringAsync();

                if (res.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(content);
                    username = doc.RootElement.TryGetProperty("username", out var u) ? u.GetString() : null;
                    profileImage = doc.RootElement.TryGetProperty("profile_image", out var img) ? img.GetString() : null;

                    var userId = username ?? connection.ProviderUserId ?? "pinterest_user";
                    var existingUser = existingAccounts.Find(a => a.Platform == "pinterest" && a.ExternalAccountId == userId);

                    response.DiscoveredAssets.Add(new DiscoveredSocialAssetDto
                    {
                        Platform = "pinterest",
                        ExternalId = userId,
                        Name = $"@{username ?? "Pinterest Account"} (Account)",
                        Category = "Pinterest Profile",
                        AvatarUrl = profileImage,
                        AccessToken = connection.AccessToken,
                        IsAlreadyConnected = existingUser != null && existingUser.IsConnected,
                        ConnectedClientId = existingUser?.ClientId,
                        ConnectedClientName = existingUser?.Client?.Name
                    });
                }
                else
                {
                    _logger.LogWarning("Pinterest user_account fetch returned {Status}: {Content}", (int)res.StatusCode, content);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not discover Pinterest user profile");
            }

            // 2. Fetch boards (GET https://api.pinterest.com/v5/boards)
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.pinterest.com/v5/boards");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.AccessToken);

                var res = await client.SendAsync(req);
                var content = await res.Content.ReadAsStringAsync();

                if (res.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(content);
                    if (doc.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in items.EnumerateArray())
                        {
                            var boardId = item.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                            var boardName = item.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : "Board";
                            var privacy = item.TryGetProperty("privacy", out var priv) ? priv.GetString() : "PUBLIC";

                            if (!string.IsNullOrEmpty(boardId))
                            {
                                var existingBoard = existingAccounts.Find(a => a.Platform == "pinterest" && a.ExternalAccountId == boardId);

                                response.DiscoveredAssets.Add(new DiscoveredSocialAssetDto
                                {
                                    Platform = "pinterest",
                                    ExternalId = boardId,
                                    Name = $"{boardName} (Board)",
                                    Category = $"Pinterest Board ({privacy})",
                                    AvatarUrl = profileImage,
                                    AccessToken = connection.AccessToken,
                                    IsAlreadyConnected = existingBoard != null && existingBoard.IsConnected,
                                    ConnectedClientId = existingBoard?.ClientId,
                                    ConnectedClientName = existingBoard?.Client?.Name
                                });
                            }
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("Pinterest boards fetch returned {Status}: {Content}", (int)res.StatusCode, content);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not discover Pinterest boards");
            }
        }

        public async Task<SocialAccount> BindAccountToClientAsync(BindSocialAccountRequest request)
        {
            var client = await _context.SocialClients.FirstOrDefaultAsync(c => c.Id == request.ClientId);
            if (client == null)
                throw new ArgumentException($"Client workspace with ID {request.ClientId} not found.");

            var connection = await _context.SocialConnections.FirstOrDefaultAsync(c => c.Id == request.ConnectionId);
            if (connection == null)
                throw new ArgumentException($"Connection with ID {request.ConnectionId} not found.");

            var platformLower = request.Platform.Trim().ToLowerInvariant();

            // Find if account already exists for this client and external asset
            var existing = await _context.SocialAccounts.FirstOrDefaultAsync(a =>
                a.ClientId == request.ClientId &&
                a.Platform == platformLower &&
                a.ExternalAccountId == request.ExternalAccountId);

            if (existing != null)
            {
                // Update credentials
                existing.AccountName = request.AccountName;
                existing.AccessToken = request.AccessToken ?? connection.AccessToken;
                existing.AvatarUrl = request.AvatarUrl;
                existing.IsConnected = true;
                existing.ConnectionId = connection.Id;
                existing.TokenExpiresAt = connection.TokenExpiresAt;
                existing.RefreshToken = connection.RefreshToken;

                await _context.SaveChangesAsync();
                return existing;
            }

            // Create new SocialAccount mapping for this client
            var newAccount = new SocialAccount
            {
                ClientId = request.ClientId,
                ConnectionId = connection.Id,
                Platform = platformLower,
                AccountName = request.AccountName,
                ExternalAccountId = request.ExternalAccountId,
                AccessToken = request.AccessToken ?? connection.AccessToken,
                AvatarUrl = request.AvatarUrl,
                IsConnected = true,
                TokenExpiresAt = connection.TokenExpiresAt,
                RefreshToken = connection.RefreshToken
            };

            _context.SocialAccounts.Add(newAccount);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Successfully bound {Platform} account '{AccountName}' (Asset {ExternalId}) to client workspace '{ClientName}'",
                platformLower, request.AccountName, request.ExternalAccountId, client.Name);

            return newAccount;
        }
    }
}
