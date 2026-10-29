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
        private static readonly HashSet<string> SupportedPlatforms =
            new(StringComparer.Ordinal)
            {
                "facebook", "instagram", "linkedin",
                "pinterest", "youtube", "x"
            };

        private static readonly TimeSpan StateLifetime =
            TimeSpan.FromMinutes(10);

        // Remembers a finished OAuth login for a short time so the main page can pick it up.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<
            string, (int ConnectionId, int? ClientId, DateTime At)>
            PendingOAuthResults = new();

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

        private string GraphVersion =>
            _configuration["SocialMedia:GraphApiVersion"] ?? "v19.0";

        // GET: api/social/accounts
        [HttpGet]
        public async Task<IActionResult> GetAccounts(
            [FromQuery] int? clientId = null)
        {
            var query = _context.SocialAccounts.AsQueryable();

            if (clientId.HasValue && clientId.Value > 0)
                query = query.Where(a => a.ClientId == clientId.Value);

            var rows = await query
                .Select(a => new
                {
                    a.Id,
                    a.Platform,
                    a.AccountName,
                    a.IsConnected,
                    a.TokenExpiresAt,
                    a.ClientId
                })
                .ToListAsync();

            var now = DateTime.UtcNow;

            var accounts = rows.Select(a =>
            {
                string status;

                if (!a.IsConnected)
                    status = "NotConnected";
                else if (a.TokenExpiresAt.HasValue &&
                         a.TokenExpiresAt.Value <= now)
                    status = "NeedsReconnection";
                else if (a.TokenExpiresAt.HasValue &&
                         a.TokenExpiresAt.Value <= now.AddDays(7))
                    status = "ExpiringSoon";
                else
                    status = "Connected";

                int? daysLeft = a.TokenExpiresAt.HasValue
                    ? (int)Math.Floor(
                        (a.TokenExpiresAt.Value - now).TotalDays)
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

        // POST: api/social/accounts/{id}/disconnect
        [HttpPost("{id:int}/disconnect")]
        public async Task<IActionResult> Disconnect(int id)
        {
            var account = await _context.SocialAccounts
                .FirstOrDefaultAsync(a => a.Id == id);

            if (account == null)
            {
                return NotFound(new { error = "Account not found." });
            }

            account.IsConnected = false;
            account.AccessToken = string.Empty;
            account.RefreshToken = null;
            account.TokenExpiresAt = null;
            account.OAuthState = null;

            if (account.ConnectionId.HasValue)
            {
                var connection = await _context.SocialConnections
                    .FirstOrDefaultAsync(c => c.Id == account.ConnectionId.Value);

                if (connection != null)
                {
                    connection.IsActive = false;
                    connection.AccessToken = string.Empty;
                    connection.RefreshToken = null;
                    connection.TokenExpiresAt = null;
                    connection.UpdatedAt = DateTime.UtcNow;
                }
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Account {Id} ({Platform}) disconnected",
                account.Id,
                account.Platform);

            return Ok(new { success = true });
        }

        // GET: api/social/accounts/discover/{connectionId}
        [HttpGet("discover/{connectionId:int}")]
        public async Task<IActionResult> DiscoverAssets(int connectionId)
        {
            try
            {
                var result =
                    await _discoveryService.DiscoverAssetsAsync(connectionId);

                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return NotFound(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error discovering assets for connection {ConnId}",
                    connectionId);

                return StatusCode(500, new
                {
                    error = "Failed to discover accounts. Please try reconnecting."
                });
            }
        }

        // POST: api/social/accounts/bind
        [HttpPost("bind")]
        public async Task<IActionResult> BindAccount(
            [FromBody] BindSocialAccountRequest request)
        {
            if (request == null ||
                string.IsNullOrWhiteSpace(request.Platform) ||
                string.IsNullOrWhiteSpace(request.ExternalAccountId))
            {
                return BadRequest(new
                {
                    error = "Platform and ExternalAccountId are required."
                });
            }

            try
            {
                var account =
                    await _discoveryService.BindAccountToClientAsync(request);

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
                _logger.LogError(
                    ex,
                    "Failed to bind account {Platform} {Id} to client {ClientId}",
                    request.Platform,
                    request.ExternalAccountId,
                    request.ClientId);

                return StatusCode(500, new
                {
                    error = "An error occurred while saving the account."
                });
            }
        }

        // GET: api/social/accounts/oauth-result/{platform}
        // The main page asks this after the login popup opens.
        [HttpGet("oauth-result/{platform}")]
        public IActionResult GetOAuthResult(string platform)
        {
            var p = (platform ?? string.Empty).Trim().ToLowerInvariant();

            if (PendingOAuthResults.TryRemove(p, out var r) &&
                DateTime.UtcNow - r.At < TimeSpan.FromMinutes(3))
            {
                return Ok(new
                {
                    ready = true,
                    connectionId = r.ConnectionId,
                    clientId = r.ClientId
                });
            }

            return Ok(new { ready = false });
        }

        // GET: api/social/accounts/connect/{platform}
        [HttpGet("connect/{platform}")]
        public async Task<IActionResult> Connect(
            string platform,
            [FromQuery] int? clientId = null)
        {
            var platformLower =
                (platform ?? string.Empty).Trim().ToLowerInvariant();

            if (!SupportedPlatforms.Contains(platformLower))
            {
                return BadRequest(new
                {
                    error = $"Platform '{WebUtility.HtmlEncode(platform ?? "")}' is not supported for OAuth."
                });
            }

            var key = char.ToUpperInvariant(platformLower[0])
                      + platformLower.Substring(1);

            var oauthClientId =
                _configuration[$"SocialMedia:{key}:ClientId"];

            if (string.IsNullOrWhiteSpace(oauthClientId) ||
                oauthClientId == "YOUR_CLIENT_ID_HERE")
            {
                return BadRequest(new
                {
                    error = $"ClientId for {platformLower} is not configured on the server."
                });
            }

            PendingOAuthResults.TryRemove(platformLower, out _);

            var redirectUri = BuildRedirectUri(platformLower);

            string? codeVerifier = null;
            string? codeChallenge = null;

            if (platformLower == "x")
            {
                codeVerifier = Base64UrlEncode(
                    RandomNumberGenerator.GetBytes(32));

                using var sha256 = SHA256.Create();

                var challengeBytes = sha256.ComputeHash(
                    Encoding.ASCII.GetBytes(codeVerifier));

                codeChallenge = Base64UrlEncode(challengeBytes);
            }

            // Send the normal state to the provider. Keep the verifier in DB.
            var state = GenerateState(clientId);

            var storedState = platformLower == "x"
                ? $"{state}~{codeVerifier}"
                : state;

            var account = await _context.SocialAccounts
                .FirstOrDefaultAsync(a => a.Platform == platformLower);

            if (account == null)
            {
                var defaultClient =
                    await _context.SocialClients.FirstOrDefaultAsync();

                account = new SocialAccount
                {
                    Platform = platformLower,
                    AccountName = $"{key} Account",
                    ExternalAccountId = "",
                    AccessToken = "",
                    IsConnected = false,
                    ClientId = clientId ?? defaultClient?.Id
                };

                _context.SocialAccounts.Add(account);
            }

            account.OAuthState = storedState;
            await _context.SaveChangesAsync();

            var encodedState = Uri.EscapeDataString(state);
            var encodedRedirect = Uri.EscapeDataString(redirectUri);

            string oauthUrl;

            switch (platformLower)
            {

                case "x":
                    {
                        var scopes =
                            _configuration["SocialMedia:X:Scopes"]
                            ?? "tweet.read tweet.write users.read offline.access";

                        oauthUrl = "https://x.com/i/oauth2/authorize"
                            + "?response_type=code"
                            + $"&client_id={Uri.EscapeDataString(oauthClientId)}"
                            + $"&redirect_uri={encodedRedirect}"
                            + $"&scope={Uri.EscapeDataString(scopes)}"
                            + $"&state={encodedState}"
                            + $"&code_challenge={Uri.EscapeDataString(codeChallenge!)}"
                            + "&code_challenge_method=S256";

                        break;
                    }


                case "facebook":
                    oauthUrl =
                        $"https://www.facebook.com/{GraphVersion}/dialog/oauth"
                        + $"?client_id={Uri.EscapeDataString(oauthClientId)}"
                        + $"&redirect_uri={encodedRedirect}"
                        + "&scope=pages_show_list,pages_manage_posts,pages_read_engagement"
                        + $"&state={encodedState}"
                        + "&response_type=code";
                    break;

                case "instagram":
                    oauthUrl =
                        $"https://www.facebook.com/{GraphVersion}/dialog/oauth"
                        + $"?client_id={Uri.EscapeDataString(oauthClientId)}"
                        + $"&redirect_uri={encodedRedirect}"
                        + "&scope=instagram_basic,instagram_content_publish,pages_show_list,pages_read_engagement,business_management"
                        + $"&state={encodedState}"
                        + "&response_type=code";
                    break;

                case "pinterest":
                    {
                        var scopes =
                            _configuration["SocialMedia:Pinterest:Scopes"]
                            ?? "boards:read,boards:write,pins:read,pins:write,user_accounts:read";

                        oauthUrl = "https://www.pinterest.com/oauth/"
                            + "?response_type=code"
                            + $"&client_id={Uri.EscapeDataString(oauthClientId)}"
                            + $"&redirect_uri={encodedRedirect}"
                            + $"&state={encodedState}"
                            + $"&scope={Uri.EscapeDataString(scopes)}";
                        break;
                    }

                case "youtube":
                    {
                        var scopes =
                            _configuration["SocialMedia:Youtube:Scopes"]
                            ?? "https://www.googleapis.com/auth/youtube.upload https://www.googleapis.com/auth/youtube.readonly";

                        oauthUrl = "https://accounts.google.com/o/oauth2/v2/auth"
                            + "?response_type=code"
                            + $"&client_id={Uri.EscapeDataString(oauthClientId)}"
                            + $"&redirect_uri={encodedRedirect}"
                            + $"&state={encodedState}"
                            + $"&scope={Uri.EscapeDataString(scopes)}"
                            + "&access_type=offline"
                            + "&prompt=consent";
                        break;
                    }

                default: // LinkedIn
                    {
                        var scopes =
                            _configuration["SocialMedia:LinkedIn:Scopes"]
                            ?? "openid profile w_member_social";

                        oauthUrl =
                            "https://www.linkedin.com/oauth/v2/authorization"
                            + "?response_type=code"
                            + $"&client_id={Uri.EscapeDataString(oauthClientId)}"
                            + $"&redirect_uri={encodedRedirect}"
                            + $"&state={encodedState}"
                            + $"&scope={Uri.EscapeDataString(scopes)}";
                        break;
                    }
            }

            return Redirect(oauthUrl);
        }

        // GET: api/social/accounts/callback/{platform}
        [HttpGet("callback/{platform}")]
        public async Task<IActionResult> Callback(
            string platform,
            string? code,
            string? state,
            string? error)
        {
            var platformLower =
                (platform ?? string.Empty).Trim().ToLowerInvariant();

            if (!SupportedPlatforms.Contains(platformLower))
            {
                return Content(
                    BuildResultHtml(
                        false, "social", "This platform is not supported."),
                    "text/html");
            }

            var key = char.ToUpperInvariant(platformLower[0])
                      + platformLower.Substring(1);

            var stateValue = state ?? string.Empty;

            SocialAccount? account = null;

            if (!string.IsNullOrEmpty(stateValue))
            {
                account = await _context.SocialAccounts
                    .FirstOrDefaultAsync(a =>
                        a.Platform == platformLower &&
                        a.OAuthState != null &&
                        (a.OAuthState == stateValue ||
                         a.OAuthState.StartsWith(stateValue + "~")));
            }

            if (account == null ||
                !IsStateValid(account.OAuthState, state))
            {
                _logger.LogWarning(
                    "Invalid or expired OAuth state for {Platform}",
                    platformLower);

                return Content(
                    BuildResultHtml(
                        false,
                        platformLower,
                        "Security check failed or the request expired. Please try connecting again."),
                    "text/html");
            }

            // Read the verifier before clearing the one-time state.
            var codeVerifier = platformLower == "x"
                ? TryExtractCodeVerifierFromState(account.OAuthState)
                : null;

            if (platformLower == "x" &&
                string.IsNullOrWhiteSpace(codeVerifier))
            {
                account.OAuthState = null;
                await _context.SaveChangesAsync();

                return Content(
                    BuildResultHtml(
                        false,
                        platformLower,
                        "The X security verification could not be completed. Please try connecting again."),
                    "text/html");
            }

            // The state is single-use, even if the user cancels.
            account.OAuthState = null;
            await _context.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(error))
            {
                _logger.LogWarning(
                    "OAuth denied for {Platform}: {Error}",
                    platformLower, error);

                var friendly = error == "unauthorized_scope_error"
                    ? "This app requested a permission that is not approved. Check the app's permissions."
                    : "Authorization was cancelled or denied.";

                return Content(
                    BuildResultHtml(false, platformLower, friendly),
                    "text/html");
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                return Content(
                    BuildResultHtml(
                        false, platformLower,
                        "No authorization code was received."),
                    "text/html");
            }

            try
            {
                var clientId =
                    _configuration[$"SocialMedia:{key}:ClientId"];

                var clientSecret =
                    _configuration[$"SocialMedia:{key}:ClientSecret"];

                if (string.IsNullOrWhiteSpace(clientId))
                    throw new UserFacingException(
                        "The OAuth client is not configured.");

                var redirectUri = BuildRedirectUri(platformLower);

                var tokenData = await ExchangeCodeForTokenAsync(
                    platformLower,
                    code,
                    clientId,
                    clientSecret ?? string.Empty,
                    redirectUri,
                    codeVerifier);

                // Do not mark the account connected until profile validation succeeds.
                account.AccessToken = tokenData.AccessToken;
                account.RefreshToken = tokenData.RefreshToken;
                account.TokenExpiresAt = tokenData.ExpiresAt;

                if (platformLower == "facebook")
                {
                    account.ExternalAccountId =
                        await ResolveFacebookPageIdAsync(tokenData.AccessToken)
                        ?? account.ExternalAccountId;
                }

                if (platformLower == "instagram")
                {
                    account.ExternalAccountId =
                        await ResolveInstagramAccountIdAsync(tokenData.AccessToken)
                        ?? account.ExternalAccountId;
                }

                if (platformLower == "linkedin")
                {
                    var memberId =
                        await ResolveLinkedInMemberIdAsync(tokenData.AccessToken);

                    if (string.IsNullOrWhiteSpace(memberId))
                    {
                        throw new UserFacingException(
                            "Could not read your LinkedIn member ID. Check your LinkedIn app's OpenID Connect permissions.");
                    }

                    account.ExternalAccountId = memberId;
                }

                if (platformLower == "pinterest")
                {
                    var username =
                        await ResolvePinterestUserAsync(tokenData.AccessToken);

                    if (!string.IsNullOrWhiteSpace(username))
                        account.ExternalAccountId = username;
                }

                if (platformLower == "youtube")
                {
                    var channel =
                        await ResolveYouTubeChannelAsync(tokenData.AccessToken);

                    if (channel == null)
                    {
                        throw new UserFacingException(
                            "Could not find a YouTube channel for this Google account. Make sure the account has a YouTube channel.");
                    }

                    account.ExternalAccountId = channel.Value.Id;
                    account.AccountName = channel.Value.Title;
                }

                if (platformLower == "x")
                {
                    var xUser =
                        await ResolveXUserAsync(tokenData.AccessToken);

                    if (xUser == null ||
                        string.IsNullOrWhiteSpace(xUser.Value.Id) ||
                        string.IsNullOrWhiteSpace(xUser.Value.Username))
                    {
                        throw new UserFacingException(
                            "X authorization succeeded, but the account profile could not be retrieved. Check your X app permissions and try again.");
                    }

                    account.ExternalAccountId = xUser.Value.Id;
                    account.AccountName = "@" + xUser.Value.Username;
                }

                account.IsConnected = true;

                var targetClientId = TryExtractClientIdFromState(state);

                var connection = await _context.SocialConnections
                    .FirstOrDefaultAsync(c =>
                        c.Provider == platformLower &&
                        (targetClientId == null ||
                         c.ClientId == targetClientId));

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

                _logger.LogInformation(
                    "OAuth completed for {Platform}, ConnectionId {ConnId}",
                    platformLower, connection.Id);

                if (platformLower == "x")
                {
                    // The main page picks this up by asking /oauth-result/x
                    PendingOAuthResults[platformLower] =
                        (connection.Id, targetClientId, DateTime.UtcNow);
                }

                return Content(
                    BuildResultHtml(
                        true,
                        platformLower,
                        null,
                        connection.Id,
                        targetClientId),
                    "text/html");
            }
            catch (UserFacingException ex)
            {
                _logger.LogWarning(
                    "OAuth failed for {Platform}: {Message}",
                    platformLower, ex.Message);

                return Content(
                    BuildResultHtml(false, platformLower, ex.Message),
                    "text/html");
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex, "Failed to complete OAuth for {Platform}",
                    platformLower);

                return Content(
                    BuildResultHtml(
                        false,
                        platformLower,
                        "The connection could not be completed. Please try again or contact support."),
                    "text/html");
            }
        }

        private string BuildRedirectUri(string platform)
        {
            var callbackBaseUrl =
                _configuration["SocialMedia:CallbackBaseUrl"];

            if (string.IsNullOrWhiteSpace(callbackBaseUrl))
            {
                throw new InvalidOperationException(
                    "SocialMedia:CallbackBaseUrl is missing. Set it to the public HTTPS backend URL.");
            }

            if (!Uri.TryCreate(
                    callbackBaseUrl.TrimEnd('/'),
                    UriKind.Absolute,
                    out var callbackBaseUri) ||
                callbackBaseUri.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException(
                    "SocialMedia:CallbackBaseUrl must be a valid HTTPS URL.");
            }

            return $"{callbackBaseUri.GetLeftPart(UriPartial.Authority)}/api/social/accounts/callback/{platform}";
        }

        // State helpers

        private static string GenerateState(int? clientId = null)
        {
            var random = Base64UrlEncode(
                RandomNumberGenerator.GetBytes(32));

            var expiry = DateTimeOffset.UtcNow
                .Add(StateLifetime)
                .ToUnixTimeSeconds();

            return clientId.HasValue
                ? $"{random}.{expiry}.{clientId.Value}"
                : $"{random}.{expiry}";
        }

        private static bool IsStateValid(
            string? stored,
            string? received)
        {
            if (string.IsNullOrWhiteSpace(stored) ||
                string.IsNullOrWhiteSpace(received))
                return false;

            var cleanStored = stored.Split('~')[0];
            var cleanReceived = received.Split('~')[0];

            var storedBytes = Encoding.UTF8.GetBytes(cleanStored);
            var receivedBytes = Encoding.UTF8.GetBytes(cleanReceived);

            if (storedBytes.Length != receivedBytes.Length ||
                !CryptographicOperations.FixedTimeEquals(
                    storedBytes, receivedBytes))
                return false;

            var parts = cleanStored.Split('.');

            if (parts.Length < 2 ||
                !long.TryParse(parts[1], out var expiry))
                return false;

            return DateTimeOffset.UtcNow.ToUnixTimeSeconds() <= expiry;
        }

        private static int? TryExtractClientIdFromState(string? state)
        {
            if (string.IsNullOrWhiteSpace(state))
                return null;

            var cleanState = state.Split('~')[0];
            var parts = cleanState.Split('.');

            return parts.Length >= 3 &&
                   int.TryParse(parts[2], out var clientId)
                ? clientId
                : null;
        }

        private static string? TryExtractCodeVerifierFromState(
            string? storedState)
        {
            if (string.IsNullOrWhiteSpace(storedState))
                return null;

            var index = storedState.IndexOf('~');

            return index >= 0 && index < storedState.Length - 1
                ? storedState[(index + 1)..]
                : null;
        }

        private static string Base64UrlEncode(byte[] bytes) =>
            Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');

        // Token exchange

        private async Task<(
            string AccessToken,
            string? RefreshToken,
            DateTime? ExpiresAt)>
            ExchangeCodeForTokenAsync(
                string platform,
                string code,
                string clientId,
                string clientSecret,
                string redirectUri,
                string? codeVerifier = null)
        {
            var client = _httpClientFactory.CreateClient();

            if (platform is "facebook" or "instagram")
            {
                var tokenUrl =
                    $"https://graph.facebook.com/{GraphVersion}/oauth/access_token";

                var response = await client.PostAsync(
                    tokenUrl,
                    new FormUrlEncodedContent(
                        new Dictionary<string, string>
                        {
                            ["client_id"] = clientId,
                            ["redirect_uri"] = redirectUri,
                            ["client_secret"] = clientSecret,
                            ["code"] = code
                        }));

                var content = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    throw new Exception(
                        $"Facebook token exchange failed ({(int)response.StatusCode}): {content}");

                using var doc = JsonDocument.Parse(content);
                var token = doc.RootElement
                    .GetProperty("access_token").GetString()!;

                var longLivedResponse = await client.PostAsync(
                    tokenUrl,
                    new FormUrlEncodedContent(
                        new Dictionary<string, string>
                        {
                            ["grant_type"] = "fb_exchange_token",
                            ["client_id"] = clientId,
                            ["client_secret"] = clientSecret,
                            ["fb_exchange_token"] = token
                        }));

                var longLivedContent =
                    await longLivedResponse.Content.ReadAsStringAsync();

                if (!longLivedResponse.IsSuccessStatusCode)
                    throw new Exception(
                        $"Could not get a long-lived Facebook token ({(int)longLivedResponse.StatusCode}).");

                using var longLivedDoc =
                    JsonDocument.Parse(longLivedContent);

                token = longLivedDoc.RootElement
                    .GetProperty("access_token").GetString()!;

                var expiresIn =
                    longLivedDoc.RootElement.TryGetProperty(
                        "expires_in", out var fbExp)
                        ? fbExp.GetInt64()
                        : 5183944L;

                return (
                    token,
                    null,
                    DateTime.UtcNow.AddSeconds(expiresIn));
            }

            if (platform == "linkedin")
            {
                var form = new FormUrlEncodedContent(
                    new Dictionary<string, string>
                    {
                        ["grant_type"] = "authorization_code",
                        ["code"] = code,
                        ["redirect_uri"] = redirectUri,
                        ["client_id"] = clientId,
                        ["client_secret"] = clientSecret
                    });

                var response = await client.PostAsync(
                    "https://www.linkedin.com/oauth/v2/accessToken",
                    form);

                var content = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    throw new Exception(
                        $"LinkedIn token exchange failed ({(int)response.StatusCode}): {content}");

                using var doc = JsonDocument.Parse(content);

                var token = doc.RootElement
                    .GetProperty("access_token").GetString()!;

                var expiresIn =
                    doc.RootElement.TryGetProperty(
                        "expires_in", out var exp)
                        ? exp.GetInt64()
                        : 5183944L;

                var refreshToken =
                    doc.RootElement.TryGetProperty(
                        "refresh_token", out var rt)
                        ? rt.GetString()
                        : null;

                return (
                    token,
                    refreshToken,
                    DateTime.UtcNow.AddSeconds(expiresIn));
            }

            if (platform == "pinterest")
            {
                var form = new FormUrlEncodedContent(
                    new Dictionary<string, string>
                    {
                        ["grant_type"] = "authorization_code",
                        ["code"] = code,
                        ["redirect_uri"] = redirectUri
                    });

                using var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    "https://api.pinterest.com/v5/oauth/token")
                {
                    Content = form
                };

                var credentials = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));

                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Basic", credentials);

                var response = await client.SendAsync(request);
                var content = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Pinterest token exchange failed with status {Status}: {Body}",
                        (int)response.StatusCode,
                        content);

                    throw new UserFacingException(
                        "Pinterest authorization failed. Check the Pinterest app credentials, redirect URL, and permissions.");
                }

                using var doc = JsonDocument.Parse(content);

                var token = doc.RootElement
                    .GetProperty("access_token").GetString()!;

                var expiresIn = doc.RootElement.TryGetProperty(
                    "expires_in", out var exp)
                    ? exp.GetInt64()
                    : 2592000L;

                var refreshToken = doc.RootElement.TryGetProperty(
                    "refresh_token", out var rt)
                    ? rt.GetString()
                    : null;

                return (
                    token,
                    refreshToken,
                    DateTime.UtcNow.AddSeconds(expiresIn));
            }

            if (platform == "youtube")
            {
                var form = new FormUrlEncodedContent(
                    new Dictionary<string, string>
                    {
                        ["grant_type"] = "authorization_code",
                        ["code"] = code,
                        ["redirect_uri"] = redirectUri,
                        ["client_id"] = clientId,
                        ["client_secret"] = clientSecret
                    });

                var response = await client.PostAsync(
                    "https://oauth2.googleapis.com/token",
                    form);

                var content = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    throw new Exception(
                        $"YouTube token exchange failed ({(int)response.StatusCode}): {content}");

                using var doc = JsonDocument.Parse(content);

                var token = doc.RootElement
                    .GetProperty("access_token").GetString()!;

                var expiresIn =
                    doc.RootElement.TryGetProperty(
                        "expires_in", out var exp)
                        ? exp.GetInt64()
                        : 3600L;

                var refreshToken =
                    doc.RootElement.TryGetProperty(
                        "refresh_token", out var rt)
                        ? rt.GetString()
                        : null;

                return (
                    token,
                    refreshToken,
                    DateTime.UtcNow.AddSeconds(expiresIn));
            }

            if (platform == "x")
            {
                if (string.IsNullOrWhiteSpace(codeVerifier))
                {
                    throw new UserFacingException(
                        "The X PKCE verifier is missing. Please connect X again.");
                }

                var formValues = new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["redirect_uri"] = redirectUri,
                    ["code_verifier"] = codeVerifier
                };

                // Confidential clients authenticate with Basic.
                // Public clients send client_id in the form body.
                using var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    "https://api.x.com/2/oauth2/token");

                if (!string.IsNullOrWhiteSpace(clientSecret))
                {
                    var credentials = Convert.ToBase64String(
                        Encoding.UTF8.GetBytes(
                            $"{clientId}:{clientSecret}"));

                    request.Headers.Authorization =
                        new AuthenticationHeaderValue(
                            "Basic", credentials);
                }
                else
                {
                    formValues["client_id"] = clientId;
                }

                request.Content = new FormUrlEncodedContent(formValues);

                var response = await client.SendAsync(request);
                var content = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "X token exchange failed with status {Status}: {Body}",
                        (int)response.StatusCode, content);

                    throw new UserFacingException(
                        "X could not complete authorization. Check the X client type, client credentials, callback URL, and app permissions.");
                }

                using var doc = JsonDocument.Parse(content);

                var token = doc.RootElement
                    .GetProperty("access_token").GetString()!;

                var expiresIn =
                    doc.RootElement.TryGetProperty(
                        "expires_in", out var exp)
                        ? exp.GetInt64()
                        : 7200L;

                var refreshToken =
                    doc.RootElement.TryGetProperty(
                        "refresh_token", out var rt)
                        ? rt.GetString()
                        : null;

                return (
                    token,
                    refreshToken,
                    DateTime.UtcNow.AddSeconds(expiresIn));
            }

            throw new Exception(
                $"Unsupported platform for OAuth: {platform}");
        }

        // Pinterest profile

        private async Task<string?> ResolvePinterestUserAsync(
            string accessToken)
        {
            try
            {
                using var response = await GetWithBearerAsync(
                    "https://api.pinterest.com/v5/user_account",
                     accessToken);

                var content = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Pinterest user_account failed with status {Status}",
                        (int)response.StatusCode);

                    return null;
                }

                using var doc = JsonDocument.Parse(content);

                return doc.RootElement.TryGetProperty(
                    "username", out var username)
                    ? username.GetString()
                    : null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex, "Could not read Pinterest user account");

                return null;
            }
        }

        // X profile

        private async Task<(string Id, string Username)?>
            ResolveXUserAsync(string accessToken)
        {
            try
            {
                using var response = await GetWithBearerAsync(
                 "https://api.x.com/2/users/me",
                  accessToken);

                var content = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "X users/me failed with status {Status}: {Content}",
                        (int)response.StatusCode, content);

                    return null;
                }

                using var doc = JsonDocument.Parse(content);

                if (doc.RootElement.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("id", out var idElement) &&
                    data.TryGetProperty("username", out var usernameElement))
                {
                    var id = idElement.GetString();
                    var username = usernameElement.GetString();

                    if (!string.IsNullOrWhiteSpace(id) &&
                        !string.IsNullOrWhiteSpace(username))
                    {
                        return (id, username);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex, "Could not resolve X user account");
            }

            return null;
        }

        // YouTube channel

        private async Task<(string Id, string Title)?>
            ResolveYouTubeChannelAsync(string accessToken)
        {
            try
            {
                using var response = await GetWithBearerAsync(
                    "https://www.googleapis.com/youtube/v3/channels?part=snippet&mine=true",
                    accessToken);

                var content = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "YouTube channels call failed with status {Status}: {Body}",
                        (int)response.StatusCode, content);

                    return null;
                }

                using var doc = JsonDocument.Parse(content);

                if (!doc.RootElement.TryGetProperty(
                        "items", out var items) ||
                    items.GetArrayLength() == 0)
                {
                    return null;
                }

                var channel = items[0];

                var id = channel.GetProperty("id").GetString();
                var title = channel.GetProperty("snippet")
                    .GetProperty("title").GetString();

                if (string.IsNullOrWhiteSpace(id))
                    return null;

                return (id, title ?? "YouTube Channel");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex, "Could not read YouTube channel");

                return null;
            }
        }

        // Shared authenticated GET helper

        private async Task<HttpResponseMessage> GetWithBearerAsync(
            string url,
            string accessToken)
        {
            var client = _httpClientFactory.CreateClient();

            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);

            return await client.SendAsync(request);
        }

        // Facebook Page

        private async Task<string?> ResolveFacebookPageIdAsync(
            string userToken)
        {
            try
            {
                using var response = await GetWithBearerAsync(
                    $"https://graph.facebook.com/{GraphVersion}/me/accounts",
                    userToken);

                var content = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    return null;

                using var doc = JsonDocument.Parse(content);
                var data = doc.RootElement.GetProperty("data");

                if (data.GetArrayLength() > 0)
                    return data[0].GetProperty("id").GetString();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex, "Could not resolve Facebook Page ID");
            }

            return null;
        }

        // Instagram Business account

        private async Task<string?> ResolveInstagramAccountIdAsync(
            string userToken)
        {
            try
            {
                using var response = await GetWithBearerAsync(
                    $"https://graph.facebook.com/{GraphVersion}/me/accounts?fields=instagram_business_account",
                    userToken);

                var content = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    return null;

                using var doc = JsonDocument.Parse(content);
                var data = doc.RootElement.GetProperty("data");

                foreach (var page in data.EnumerateArray())
                {
                    if (page.TryGetProperty(
                            "instagram_business_account",
                            out var instagramAccount))
                    {
                        return instagramAccount.GetProperty("id")
                            .GetString();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex, "Could not resolve Instagram Account ID");
            }

            return null;
        }

        // LinkedIn member ID

        private async Task<string?> ResolveLinkedInMemberIdAsync(
            string accessToken)
        {
            try
            {
                using var response = await GetWithBearerAsync(
                    "https://api.linkedin.com/v2/userinfo",
                    accessToken);

                var content = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "LinkedIn userinfo failed with status {Status}",
                        (int)response.StatusCode);

                    return null;
                }

                using var doc = JsonDocument.Parse(content);

                return doc.RootElement.TryGetProperty(
                    "sub", out var sub)
                    ? sub.GetString()
                    : null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex, "Could not read the LinkedIn member ID");

                return null;
            }
        }

        // OAuth popup result page

        private string BuildResultHtml(
            bool success,
            string platform,
            string? errorMessage,
            int? connectionId = null,
            int? clientId = null)
        {
            var safePlatform = WebUtility.HtmlEncode(platform);
            var emoji = success ? "✅" : "❌";

            var title = success
                ? $"{safePlatform} Connected!"
                : $"{safePlatform} Connection Failed";

            var message = success
                ? $"Your {safePlatform} account is authenticated. Select the accounts/pages to manage."
                : $"Error: {WebUtility.HtmlEncode(errorMessage ?? "Unknown error")}";

            var color = success ? "#198754" : "#dc3545";
            var sb = new StringBuilder();

            sb.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n");
            sb.Append("<meta charset=\"UTF-8\">\n");
            sb.Append("<title>").Append(title).Append("</title>\n");
            sb.Append("<link href=\"https://cdn.jsdelivr.net/npm/bootstrap@5.3.2/dist/css/bootstrap.min.css\" rel=\"stylesheet\">\n");
            sb.Append("</head>\n");
            sb.Append("<body class=\"d-flex align-items-center justify-content-center\" style=\"min-height:100vh;background:#f8f9fa\">\n");
            sb.Append("<div class=\"card shadow text-center p-5\" style=\"max-width:440px;width:100%\">\n");
            sb.Append("<div class=\"display-1 mb-3\">").Append(emoji).Append("</div>\n");
            sb.Append("<h4 class=\"fw-bold\" style=\"color:")
                .Append(color).Append("\">").Append(title).Append("</h4>\n");
            sb.Append("<p class=\"text-muted mt-2\">")
                .Append(message).Append("</p>\n");

            if (success)
            {
                sb.Append("<div class=\"progress mt-3\"><div class=\"progress-bar bg-success\" id=\"prog\" style=\"width:0%\"></div></div>\n");
            }

            sb.Append("<button class=\"btn btn-secondary mt-4\" onclick=\"window.close()\">Close Window</button>\n");
            sb.Append("</div>\n");

            var jsPlatform = platform is
                "facebook" or "instagram" or "linkedin" or
                "pinterest" or "youtube" or "x"
                    ? platform
                    : "social";

            var connId = connectionId?.ToString() ?? "null";
            var cliId = clientId?.ToString() ?? "null";

            var frontendBaseUrl = _configuration["Frontend:BaseUrl"];
            var frontendOrigin = string.Empty;

            if (Uri.TryCreate(
                    frontendBaseUrl,
                    UriKind.Absolute,
                    out var frontendUri) &&
                (frontendUri.Scheme == Uri.UriSchemeHttps ||
                 frontendUri.Scheme == Uri.UriSchemeHttp))
            {
                frontendOrigin =
                    frontendUri.GetLeftPart(UriPartial.Authority);
            }

            var frontendOriginJs =
                JsonSerializer.Serialize(frontendOrigin);

            sb.Append("<script>\n");
            sb.Append("const frontendOrigin = ")
                .Append(frontendOriginJs).Append(";\n");

            // Tell the main page. It may be open on the Frontend:BaseUrl address OR on
            // the same address as this popup, so try both (duplicates are skipped).
            sb.Append("var oauthMsg = { type: 'wiseliy-oauth', platform: '")
                .Append(jsPlatform)
                .Append("', success: ")
                .Append(success ? "true" : "false")
                .Append(", connectionId: ")
                .Append(connId)
                .Append(", clientId: ")
                .Append(cliId)
                .Append(" };\n");
            sb.Append("[frontendOrigin, window.location.origin].filter(function (o, i, a) { return o && a.indexOf(o) === i; }).forEach(function (o) {\n");
            sb.Append("try { if (window.opener) window.opener.postMessage(oauthMsg, o); } catch (e) {}\n");
            sb.Append("});\n");

            if (success)
            {
                sb.Append("var w = 0;\n");
                sb.Append("var t = setInterval(function() {\n");
                sb.Append("w += 4;\n");
                sb.Append("document.getElementById('prog').style.width = w + '%';\n");
                sb.Append("if (w >= 100) { clearInterval(t); window.close(); }\n");
                sb.Append("}, 40);\n");
            }

            sb.Append("</script>\n");
            sb.Append("</body>\n</html>");

            return sb.ToString();
        }

        private sealed class UserFacingException : Exception
        {
            public UserFacingException(string message)
                : base(message)
            {
            }
        }
    }
}