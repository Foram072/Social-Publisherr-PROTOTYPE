using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SocialPublisher.API.Data;
using SocialPublisher.API.Models;

using SocialPublisher.API.Services;

namespace SocialPublisher.API.Controllers
{
    [ApiController]
    [Route("api/social/accounts")]
    public class AccountsController : ControllerBase
    {
        // Only these platforms can be connected. Anything else is rejected before any config is read.
        private static readonly HashSet<string> SupportedPlatforms =
            new(StringComparer.Ordinal) { "facebook", "instagram", "linkedin", "pinterest" };

        // How long a connect attempt stays valid (the user must finish consent within this time).
        private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<AccountsController> _logger;
        private readonly ISocialDiscoveryService _discoveryService;

        public AccountsController(
            ApplicationDbContext context,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            ILogger<AccountsController> logger,
            ISocialDiscoveryService discoveryService)
        {
            _context = context;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _discoveryService = discoveryService;
        }

        // Graph API version. Default keeps today's behaviour (v19.0).
        // To upgrade without a code change, add "SocialMedia": { "GraphApiVersion": "vXX.X" } to configuration.
        private string GraphVersion => _configuration["SocialMedia:GraphApiVersion"] ?? "v19.0";

        // ════════════════════════════════════════════════════════════════════
        // EXISTING ENDPOINT — NOT CHANGED
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns all configured social accounts with their connection status.
        /// Tokens are never returned.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAccounts()
        {
            var rows = await _context.SocialAccounts
                .Select(a => new
                {
                    a.Id,
                    a.Platform,
                    a.AccountName,
                    a.IsConnected,
                    a.TokenExpiresAt
                })
                .ToListAsync();

            var now = DateTime.UtcNow;

            // Same fields as before (id, platform, accountName, isConnected) plus status info.
            // Tokens are never returned.
            var accounts = rows.Select(a =>
            {
                string status;
                if (!a.IsConnected)
                    status = "NotConnected";
                else if (a.TokenExpiresAt.HasValue && a.TokenExpiresAt.Value <= now)
                    status = "NeedsReconnection";
                else if (a.TokenExpiresAt.HasValue && a.TokenExpiresAt.Value <= now.AddDays(7))
                    status = "ExpiringSoon";
                else
                    status = "Connected";

                int? daysLeft = a.TokenExpiresAt.HasValue
                    ? (int)Math.Floor((a.TokenExpiresAt.Value - now).TotalDays)
                    : null;

                return new
                {
                    id = a.Id,
                    platform = a.Platform,
                    accountName = a.AccountName,
                    isConnected = a.IsConnected,
                    status,
                    daysLeft
                };
            }).ToList();

            return Ok(accounts);
        }

        /// <summary>
        /// Disconnects an account: removes the saved tokens and marks it "Not Connected",
        /// so the Connect button appears again. (The login is removed from Wiseliy only;
        /// the user can also remove Wiseliy's access inside the social platform's own settings.)
        /// </summary>
        [HttpPost("{id:int}/disconnect")]
        public async Task<IActionResult> Disconnect(int id)
        {
            var account = await _context.SocialAccounts.FirstOrDefaultAsync(a => a.Id == id);
            if (account == null)
                return NotFound(new { error = "Account not found." });

            account.IsConnected = false;
            account.AccessToken = string.Empty;
            account.RefreshToken = null;
            account.TokenExpiresAt = null;
            account.OAuthState = null;

            await _context.SaveChangesAsync();
            _logger.LogInformation("Account {Id} ({Platform}) disconnected", account.Id, account.Platform);

            return Ok(new { success = true });
        }

        // ════════════════════════════════════════════════════════════════════
        // Zoho Social Discovery & Binding Endpoints
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Discovers manageable assets (Facebook Pages, Instagram Business profiles, LinkedIn Company Pages)
        /// using an authorized OAuth connection. (Zoho Social Step: "Select accounts to connect").
        /// </summary>
        [HttpGet("discover/{connectionId:int}")]
        public async Task<IActionResult> DiscoverAssets(int connectionId)
        {
            try
            {
                var discoveryResult = await _discoveryService.DiscoverAssetsAsync(connectionId);
                return Ok(discoveryResult);
            }
            catch (ArgumentException ex)
            {
                return NotFound(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error discovering assets for connection {ConnId}", connectionId);
                return StatusCode(500, new { error = "Failed to discover accounts. Please try reconnecting." });
            }
        }

        /// <summary>
        /// Binds a specific discovered social asset (Page, IG account, Company) to a specific client workspace.
        /// (Zoho Social Step: "User selects the Facebook Page belonging to Client ABC").
        /// </summary>
        [HttpPost("bind")]
        public async Task<IActionResult> BindAccount([FromBody] BindSocialAccountRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Platform) || string.IsNullOrWhiteSpace(request.ExternalAccountId))
                return BadRequest(new { error = "Platform and ExternalAccountId are required." });

            try
            {
                var account = await _discoveryService.BindAccountToClientAsync(request);
                return Ok(new
                {
                    success = true,
                    accountId = account.Id,
                    clientId = account.ClientId,
                    platform = account.Platform,
                    accountName = account.AccountName,
                    externalAccountId = account.ExternalAccountId
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to bind account {Platform} {Id} to client {ClientId}", request.Platform, request.ExternalAccountId, request.ClientId);
                return StatusCode(500, new { error = "An error occurred while saving the account." });
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // OAuth Flow Endpoints
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Step 1 of OAuth: Redirects the user's browser to the social media login page.
        /// Supports: facebook, instagram, linkedin
        /// Supports optional clientId for multi-client / agency brand workspace connection (Zoho Social flow).
        /// </summary>
        [HttpGet("connect/{platform}")]
        public async Task<IActionResult> Connect(string platform, [FromQuery] int? clientId = null)
        {
            var platformLower = (platform ?? string.Empty).Trim().ToLowerInvariant();

            if (!SupportedPlatforms.Contains(platformLower))
                return BadRequest(new { error = $"Platform '{WebUtility.HtmlEncode(platform ?? "")}' is not supported for OAuth." });

            var key = char.ToUpper(platformLower[0]) + platformLower.Substring(1);

            var oauthClientId = _configuration[$"SocialMedia:{key}:ClientId"];
            if (string.IsNullOrWhiteSpace(oauthClientId) || oauthClientId == "YOUR_CLIENT_ID_HERE")
                return BadRequest(new { error = $"ClientId for {platformLower} is not configured on the server." });

            var callbackBase = _configuration["SocialMedia:CallbackBaseUrl"] ?? "http://localhost:5293";
            var redirectUri = $"{callbackBase}/api/social/accounts/callback/{platformLower}";

            // Cryptographically random state that carries its own expiry time and optional clientId
            var state = GenerateState(clientId);

            // Save state temporarily in the DB (for backward compatibility)
            var account = await _context.SocialAccounts.FirstOrDefaultAsync(a => a.Platform == platformLower);
            if (account != null)
            {
                account.OAuthState = state;
                await _context.SaveChangesAsync();
            }

            var encodedState = Uri.EscapeDataString(state);
            var encodedRedirect = Uri.EscapeDataString(redirectUri);

            string oauthUrl;
            switch (platformLower)
            {
                case "facebook":
                    oauthUrl = $"https://www.facebook.com/{GraphVersion}/dialog/oauth" +
                               $"?client_id={Uri.EscapeDataString(oauthClientId)}" +
                               $"&redirect_uri={encodedRedirect}" +
                               $"&scope=pages_show_list,pages_manage_posts,pages_read_engagement" +
                               $"&state={encodedState}" +
                               $"&response_type=code";
                    break;

                case "instagram":
                    oauthUrl = $"https://www.facebook.com/{GraphVersion}/dialog/oauth" +
                               $"?client_id={Uri.EscapeDataString(oauthClientId)}" +
                               $"&redirect_uri={encodedRedirect}" +
                               $"&scope=instagram_basic,instagram_content_publish,pages_show_list,pages_read_engagement,business_management" +
                               $"&state={encodedState}" +
                               $"&response_type=code";
                    break;

                case "pinterest":
                    // Pinterest OAuth scopes: boards:read,boards:write,pins:read,pins:write,user_accounts:read
                    var pinterestScopes = _configuration["SocialMedia:Pinterest:Scopes"] ?? "boards:read,boards:write,pins:read,pins:write,user_accounts:read";
                    oauthUrl = "https://www.pinterest.com/oauth/" +
                               $"?response_type=code" +
                               $"&client_id={Uri.EscapeDataString(oauthClientId)}" +
                               $"&redirect_uri={encodedRedirect}" +
                               $"&state={encodedState}" +
                               $"&scope={Uri.EscapeDataString(pinterestScopes)}";
                    break;

                default: // linkedin
                    // Only request scopes your LinkedIn app is approved for (Developer Portal > your app > Auth tab).
                    // To add more later, set SocialMedia:LinkedIn:Scopes in appsettings.json - no code change needed.
                    var linkedInScopes = _configuration["SocialMedia:LinkedIn:Scopes"] ?? "openid profile w_member_social";

                    oauthUrl = "https://www.linkedin.com/oauth/v2/authorization" +
                               $"?response_type=code" +
                               $"&client_id={Uri.EscapeDataString(oauthClientId)}" +
                               $"&redirect_uri={encodedRedirect}" +
                               $"&state={encodedState}" +
                               $"&scope={Uri.EscapeDataString(linkedInScopes)}";
                    break;
            }

            return Redirect(oauthUrl);
        }

        /// <summary>
        /// Step 2 of OAuth: the social platform redirects the browser here after login.
        /// The backend validates the state, exchanges the code for a token and saves it.
        /// </summary>
        [HttpGet("callback/{platform}")]
        public async Task<IActionResult> Callback(string platform, string? code, string? state, string? error)
        {
            var platformLower = (platform ?? string.Empty).Trim().ToLowerInvariant();

            if (!SupportedPlatforms.Contains(platformLower))
                return Content(BuildResultHtml(false, "social", "This platform is not supported."), "text/html");

            var key = char.ToUpper(platformLower[0]) + platformLower.Substring(1);

            // The state is mandatory. A missing state can never pass (this was the old bypass).
            var account = await _context.SocialAccounts.FirstOrDefaultAsync(a => a.Platform == platformLower);
            if (account == null || !IsStateValid(account.OAuthState, state))
            {
                _logger.LogWarning("Invalid or expired OAuth state for {Platform}", platformLower);
                return Content(BuildResultHtml(false, platformLower, "Security check failed or the request expired. Please try connecting again."), "text/html");
            }

            // From here on the state is single-use: clear it whatever the outcome.
            account.OAuthState = null;
            await _context.SaveChangesAsync();

            // User denied permission on the social media login screen
            if (!string.IsNullOrEmpty(error))
            {
                _logger.LogWarning("OAuth denied for {Platform}: {Error}", platformLower, error);

                var friendly = error == "unauthorized_scope_error"
                    ? "This app asked for a permission that is not approved for it yet. Your existing connection (if any) was not changed. Ask your administrator to check the app's permissions."
                    : "Authorization was cancelled or denied.";

                return Content(BuildResultHtml(false, platformLower, friendly), "text/html");
            }

            if (string.IsNullOrEmpty(code))
                return Content(BuildResultHtml(false, platformLower, "No authorization code was received."), "text/html");

            try
            {
                var clientId = _configuration[$"SocialMedia:{key}:ClientId"]!;
                var clientSecret = _configuration[$"SocialMedia:{key}:ClientSecret"]!;
                var callbackBase = _configuration["SocialMedia:CallbackBaseUrl"] ?? "http://localhost:5293";
                var redirectUri = $"{callbackBase}/api/social/accounts/callback/{platformLower}";

                // Exchange the temporary code for a real access token
                var tokenData = await ExchangeCodeForTokenAsync(platformLower, code, clientId, clientSecret, redirectUri);

                account.AccessToken = tokenData.AccessToken;
                account.IsConnected = true;
                account.RefreshToken = tokenData.RefreshToken;
                account.TokenExpiresAt = tokenData.ExpiresAt;

                if (platformLower == "facebook")
                    account.ExternalAccountId = await ResolveFacebookPageIdAsync(tokenData.AccessToken)
                                                ?? account.ExternalAccountId;

                if (platformLower == "instagram")
                    account.ExternalAccountId = await ResolveInstagramAccountIdAsync(tokenData.AccessToken)
                                                ?? account.ExternalAccountId;

                if (platformLower == "linkedin")
                {
                    var memberId = await ResolveLinkedInMemberIdAsync(tokenData.AccessToken);
                    if (string.IsNullOrEmpty(memberId))
                        throw new UserFacingException("Could not read your LinkedIn member ID. Check that your LinkedIn app has the product 'Sign In with LinkedIn using OpenID Connect'.");

                    account.ExternalAccountId = memberId;
                }

                if (platformLower == "pinterest")
                {
                    var pinUser = await ResolvePinterestUserAsync(tokenData.AccessToken);
                    if (!string.IsNullOrEmpty(pinUser))
                        account.ExternalAccountId = pinUser;
                }

                var targetClientId = TryExtractClientIdFromState(state);

                // Save or update SocialConnection entity (Zoho Social connection model)
                var connection = await _context.SocialConnections.FirstOrDefaultAsync(c =>
                    c.Provider == platformLower &&
                    (targetClientId == null || c.ClientId == targetClientId));

                if (connection == null)
                {
                    connection = new SocialConnection
                    {
                        ClientId = targetClientId ?? account.ClientId,
                        Provider = platformLower,
                        AccessToken = tokenData.AccessToken,
                        RefreshToken = tokenData.RefreshToken,
                        TokenExpiresAt = tokenData.ExpiresAt,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.SocialConnections.Add(connection);
                }
                else
                {
                    connection.AccessToken = tokenData.AccessToken;
                    connection.RefreshToken = tokenData.RefreshToken;
                    connection.TokenExpiresAt = tokenData.ExpiresAt;
                    connection.IsActive = true;
                    connection.UpdatedAt = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync();
                account.ConnectionId = connection.Id;
                await _context.SaveChangesAsync();

                _logger.LogInformation("OAuth completed successfully for {Platform} (ConnectionId: {ConnId})", platformLower, connection.Id);

                return Content(BuildResultHtml(true, platformLower, null, connection.Id, targetClientId), "text/html");
            }
            catch (UserFacingException ex)
            {
                _logger.LogWarning("OAuth failed for {Platform}: {Message}", platformLower, ex.Message);
                return Content(BuildResultHtml(false, platformLower, ex.Message), "text/html");
            }
            catch (Exception ex)
            {
                // Full details go to the server log only; the browser gets a generic message.
                _logger.LogError(ex, "Failed to exchange OAuth code for {Platform}", platformLower);
                return Content(BuildResultHtml(false, platformLower, "The connection could not be completed. Please try again or contact support."), "text/html");
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // State helpers
        // ────────────────────────────────────────────────────────────────────

        /// <summary>Format: base64url(32 random bytes) + "." + unix-expiry-seconds + ["." + clientId]</summary>
        private static string GenerateState(int? clientId = null)
        {
            var random = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            var expiry = DateTimeOffset.UtcNow.Add(StateLifetime).ToUnixTimeSeconds();
            return clientId.HasValue 
                ? $"{random}.{expiry}.{clientId.Value}"
                : $"{random}.{expiry}";
        }

        private static bool IsStateValid(string? stored, string? received)
        {
            if (string.IsNullOrEmpty(received))
                return false;

            // 1. If stored matches received, verify expiry
            if (!string.IsNullOrEmpty(stored))
            {
                var a = Encoding.UTF8.GetBytes(stored);
                var b = Encoding.UTF8.GetBytes(received);
                if (a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b))
                {
                    var parts = stored.Split('.');
                    if (parts.Length >= 2 && long.TryParse(parts[1], out var expStored))
                    {
                        return DateTimeOffset.UtcNow.ToUnixTimeSeconds() <= expStored;
                    }
                }
            }

            // 2. Self-validating state fallback (in case user opened connect dialog twice or multi-tab)
            var recvParts = received.Split('.');
            if (recvParts.Length >= 2 && long.TryParse(recvParts[1], out var expReceived))
            {
                return DateTimeOffset.UtcNow.ToUnixTimeSeconds() <= expReceived;
            }

            return false;
        }

        private static int? TryExtractClientIdFromState(string? state)
        {
            if (string.IsNullOrEmpty(state)) return null;
            var parts = state.Split('.');
            if (parts.Length >= 3 && int.TryParse(parts[2], out var cid))
                return cid;
            return null;
        }

        private static string Base64UrlEncode(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        // ────────────────────────────────────────────────────────────────────
        // Token exchange and account discovery
        // ────────────────────────────────────────────────────────────────────

        private async Task<(string AccessToken, string? RefreshToken, DateTime? ExpiresAt)>
            ExchangeCodeForTokenAsync(string platform, string code, string clientId, string clientSecret, string redirectUri)
        {
            var client = _httpClientFactory.CreateClient();

            if (platform == "facebook" || platform == "instagram")
            {
                // POST body instead of query string, so the secret and code never appear in URLs/logs.
                var tokenUrl = $"https://graph.facebook.com/{GraphVersion}/oauth/access_token";

                var response = await client.PostAsync(tokenUrl, new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["redirect_uri"] = redirectUri,
                    ["client_secret"] = clientSecret,
                    ["code"] = code
                }));
                var content = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    throw new Exception($"Facebook token exchange failed ({(int)response.StatusCode}): {content}");

                using var doc = JsonDocument.Parse(content);
                var token = doc.RootElement.GetProperty("access_token").GetString()!;

                // Exchange for a long-lived token (about 60 days)
                var llResponse = await client.PostAsync(tokenUrl, new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "fb_exchange_token",
                    ["client_id"] = clientId,
                    ["client_secret"] = clientSecret,
                    ["fb_exchange_token"] = token
                }));
                var llContent = await llResponse.Content.ReadAsStringAsync();

                if (!llResponse.IsSuccessStatusCode)
                    throw new Exception($"Could not get a long-lived token ({(int)llResponse.StatusCode}): {llContent}");

                using var llDoc = JsonDocument.Parse(llContent);
                token = llDoc.RootElement.GetProperty("access_token").GetString()!;

                var fbExpiresIn = llDoc.RootElement.TryGetProperty("expires_in", out var fbExp) ? fbExp.GetInt64() : 5183944L;

                return (token, null, DateTime.UtcNow.AddSeconds(fbExpiresIn));
            }

            if (platform == "linkedin")
            {
                var form = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["redirect_uri"] = redirectUri,
                    ["client_id"] = clientId,
                    ["client_secret"] = clientSecret
                });

                var response = await client.PostAsync("https://www.linkedin.com/oauth/v2/accessToken", form);
                var content = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    throw new Exception($"LinkedIn token exchange failed ({(int)response.StatusCode}): {content}");

                using var doc = JsonDocument.Parse(content);
                var token = doc.RootElement.GetProperty("access_token").GetString()!;
                var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var exp) ? exp.GetInt64() : 5183944L;
                var refreshToken = doc.RootElement.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;

                return (token, refreshToken, DateTime.UtcNow.AddSeconds(expiresIn));
            }

            if (platform == "pinterest")
            {
                var form = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["redirect_uri"] = redirectUri
                });

                using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.pinterest.com/v5/oauth/token")
                {
                    Content = form
                };
                var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

                var response = await client.SendAsync(req);
                var content = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    throw new Exception($"Pinterest token exchange failed ({(int)response.StatusCode}): {content}");

                using var doc = JsonDocument.Parse(content);
                var token = doc.RootElement.GetProperty("access_token").GetString()!;
                var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var exp) ? exp.GetInt64() : 2592000L; // default 30 days
                var refreshToken = doc.RootElement.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;

                return (token, refreshToken, DateTime.UtcNow.AddSeconds(expiresIn));
            }

            throw new Exception($"Unsupported platform for OAuth: {platform}");
        }

        /// <summary>Reads the Pinterest username of the authenticated user.</summary>
        private async Task<string?> ResolvePinterestUserAsync(string accessToken)
        {
            try
            {
                using var response = await GetWithBearerAsync("https://api.pinterest.com/v5/user_account", accessToken);
                var content = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Pinterest user_account failed with status {Status}", (int)response.StatusCode);
                    return null;
                }

                using var doc = JsonDocument.Parse(content);
                return doc.RootElement.TryGetProperty("username", out var u) ? u.GetString() : null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read Pinterest user account");
                return null;
            }
        }

        /// <summary>Sends a GET with the token in the Authorization header (not in the URL).</summary>
        private async Task<HttpResponseMessage> GetWithBearerAsync(string url, string accessToken)
        {
            var client = _httpClientFactory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            return await client.SendAsync(req);
        }

        /// <summary>Finds the first Facebook Page this token can manage and returns its ID.</summary>
        private async Task<string?> ResolveFacebookPageIdAsync(string userToken)
        {
            try
            {
                using var response = await GetWithBearerAsync(
                    $"https://graph.facebook.com/{GraphVersion}/me/accounts", userToken);
                var content = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode) return null;

                using var doc = JsonDocument.Parse(content);
                var data = doc.RootElement.GetProperty("data");
                if (data.GetArrayLength() > 0)
                    return data[0].GetProperty("id").GetString();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not resolve Facebook Page ID");
            }
            return null;
        }

        /// <summary>Finds the Instagram Business Account ID linked to the first Facebook Page.</summary>
        private async Task<string?> ResolveInstagramAccountIdAsync(string userToken)
        {
            try
            {
                using var response = await GetWithBearerAsync(
                    $"https://graph.facebook.com/{GraphVersion}/me/accounts?fields=instagram_business_account", userToken);
                var content = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode) return null;

                using var doc = JsonDocument.Parse(content);
                var data = doc.RootElement.GetProperty("data");
                foreach (var page in data.EnumerateArray())
                {
                    if (page.TryGetProperty("instagram_business_account", out var igAccount))
                        return igAccount.GetProperty("id").GetString();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not resolve Instagram Account ID");
            }
            return null;
        }

        /// <summary>Reads the LinkedIn member ID (the "sub" value) of the person who just logged in.</summary>
        private async Task<string?> ResolveLinkedInMemberIdAsync(string accessToken)
        {
            try
            {
                using var response = await GetWithBearerAsync("https://api.linkedin.com/v2/userinfo", accessToken);
                var content = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("LinkedIn userinfo failed with status {Status}", (int)response.StatusCode);
                    return null;
                }

                using var doc = JsonDocument.Parse(content);
                return doc.RootElement.TryGetProperty("sub", out var sub) ? sub.GetString() : null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read the LinkedIn member ID");
                return null;
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // Result page (shown in the popup)
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Simple HTML page shown after OAuth completes. All dynamic text is HTML-encoded.
        /// It also tells the opener window the result via postMessage (the old
        /// "refresh when popup closes" behaviour keeps working as well).
        /// <summary>
        /// Simple HTML page shown after OAuth completes. All dynamic text is HTML-encoded.
        /// It also tells the opener window the result via postMessage.
        /// </summary>
        private static string BuildResultHtml(bool success, string platform, string? errorMessage, int? connectionId = null, int? clientId = null)
        {
            var safePlatform = WebUtility.HtmlEncode(platform);
            var emoji = success ? "✅" : "❌";
            var title = success ? $"{safePlatform} Connected!" : $"{safePlatform} Connection Failed";
            var msg = success
                ? $"Your {safePlatform} account is authenticated. Select the accounts/pages to manage."
                : $"Error: {WebUtility.HtmlEncode(errorMessage ?? "Unknown error")}";
            var color = success ? "#198754" : "#dc3545";

            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n");
            sb.Append("    <meta charset=\"UTF-8\">\n");
            sb.Append("    <title>").Append(title).Append("</title>\n");
            sb.Append("    <link href=\"https://cdn.jsdelivr.net/npm/bootstrap@5.3.2/dist/css/bootstrap.min.css\" rel=\"stylesheet\">\n");
            sb.Append("</head>\n");
            sb.Append("<body class=\"d-flex align-items-center justify-content-center\" style=\"min-height:100vh;background:#f8f9fa\">\n");
            sb.Append("    <div class=\"card shadow text-center p-5\" style=\"max-width:440px;width:100%\">\n");
            sb.Append("        <div class=\"display-1 mb-3\">").Append(emoji).Append("</div>\n");
            sb.Append("        <h4 class=\"fw-bold\" style=\"color:").Append(color).Append("\">").Append(title).Append("</h4>\n");
            sb.Append("        <p class=\"text-muted mt-2\">").Append(msg).Append("</p>\n");
            if (success)
                sb.Append("        <div class=\"progress mt-3\"><div class=\"progress-bar bg-success\" id=\"prog\" style=\"width:0%\"></div></div>\n");
            sb.Append("        <button class=\"btn btn-secondary mt-4\" onclick=\"window.close()\">Close Window</button>\n");
            sb.Append("    </div>\n");

            // Platform comes from a fixed allow-list (or the word "social"), so it is safe inside JS.
            var jsPlatform = platform is "facebook" or "instagram" or "linkedin" ? platform : "social";
            var connIdStr = connectionId.HasValue ? connectionId.Value.ToString() : "null";
            var cliIdStr = clientId.HasValue ? clientId.Value.ToString() : "null";

            sb.Append("    <script>\n");
            sb.Append("        try { if (window.opener) { window.opener.postMessage({ type: 'wiseliy-oauth', platform: '")
              .Append(jsPlatform).Append("', success: ").Append(success ? "true" : "false")
              .Append(", connectionId: ").Append(connIdStr)
              .Append(", clientId: ").Append(cliIdStr)
              .Append(" }, window.location.origin); } } catch (e) { }\n");
            if (success)
            {
                sb.Append("        var w = 0;\n");
                sb.Append("        var t = setInterval(function() {\n");
                sb.Append("            w += 4;\n");
                sb.Append("            document.getElementById('prog').style.width = w + '%';\n");
                sb.Append("            if (w >= 100) { clearInterval(t); window.close(); }\n");
                sb.Append("        }, 40);\n");
            }
            sb.Append("    </script>\n");
            sb.Append("</body>\n</html>");
            return sb.ToString();
        }

        /// <summary>An error whose message is safe and useful to show to the user.</summary>
        private sealed class UserFacingException : Exception
        {
            public UserFacingException(string message) : base(message) { }
        }
    }
}