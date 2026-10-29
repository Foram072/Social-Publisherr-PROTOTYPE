using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
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
    public class GoogleAdsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IConfiguration _cfg;
        private readonly ITokenProtector _protector;
        private readonly IGoogleAdsService _googleAds;
        private readonly ILogger<GoogleAdsController> _logger;

        public GoogleAdsController(
            ApplicationDbContext db, IConfiguration cfg, ITokenProtector protector,
            IGoogleAdsService googleAds, ILogger<GoogleAdsController> logger)
        {
            _db = db;
            _cfg = cfg;
            _protector = protector;
            _googleAds = googleAds;
            _logger = logger;
        }

        private string GetRedirectUri() =>
            _cfg["GoogleAds:CallbackUrl"] ??
            $"{_cfg["SocialMedia:CallbackBaseUrl"]}/api/social/accounts/callback/googleads";

        private Task<SocialAccount?> FindAccountAsync(int clientId) =>
            _db.SocialAccounts.FirstOrDefaultAsync(a =>
                a.ClientId == clientId && a.Platform == SocialAccount.PlatformGoogleAds);

        // ---------- GET /api/googleads/connect?clientId=3 ----------
        [HttpGet("~/api/googleads/connect")]
        public IActionResult Connect([FromQuery] int clientId)
        {
            var query = new Dictionary<string, string>
            {
                ["client_id"] = _cfg["GoogleAds:ClientId"] ?? "",
                ["redirect_uri"] = GetRedirectUri(),
                ["response_type"] = "code",
                ["scope"] = "https://www.googleapis.com/auth/adwords",
                ["access_type"] = "offline",   // needed to receive a refresh token
                ["prompt"] = "consent",        // forces Google to return the refresh token every time
                ["state"] = $"clientId={clientId}"
            };

            var qs = string.Join("&", query.Select(kv =>
                $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));

            return Redirect($"https://accounts.google.com/o/oauth2/v2/auth?{qs}");
        }

        // ---------- GET /api/social/accounts/callback/googleads ----------
        [HttpGet("callback/googleads")]
        public async Task<IActionResult> GoogleAdsCallback(
            [FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error)
        {
            if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
                return PopupResult(false, error ?? "Authorization was cancelled.");

            try
            {
                // state = "clientId=12" or "clientId=12;customerId=1234567890"
                var parts = state.Split(';', StringSplitOptions.RemoveEmptyEntries);
                int clientId = int.Parse(parts[0].Split('=')[1]);
                long? customerId = parts.Length > 1 ? long.Parse(parts[1].Split('=')[1]) : null;

                var tokens = await ExchangeCodeForTokensAsync(code);
                if (string.IsNullOrEmpty(tokens.refresh_token))
                    return PopupResult(false, "Google did not return a refresh token. Please try again.");

                // If no customer ID was given, use the first account this login can access
                customerId ??= await _googleAds.GetFirstAccessibleCustomerIdAsync(tokens.refresh_token);

                var encryptedRefresh = _protector.Protect(tokens.refresh_token);
                var account = await FindAccountAsync(clientId);

                if (account == null)
                {
                    account = new SocialAccount
                    {
                        ClientId = clientId,
                        Platform = SocialAccount.PlatformGoogleAds
                    };
                    _db.SocialAccounts.Add(account);
                }

                account.GoogleAdsRefreshToken = encryptedRefresh;
                account.GoogleAdsCustomerId = customerId;
                account.IsConnected = true;

                await _db.SaveChangesAsync();
                return PopupResult(true, "Google Ads connected.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Google Ads callback failed");
                return PopupResult(false, ex.Message);
            }
        }

        // ---------- GET /api/googleads/status/3 ----------
        [HttpGet("~/api/googleads/status/{clientId:int}")]
        public async Task<IActionResult> Status(int clientId)
        {
            var account = await FindAccountAsync(clientId);

            if (account == null || !account.IsConnected ||
                string.IsNullOrEmpty(account.GoogleAdsRefreshToken) || account.GoogleAdsCustomerId == null)
            {
                // Return 200 with a status instead of 404, so the UI can show the Connect button
                return Ok(new { isConnected = false, status = "NotConnected" });
            }

            return Ok(new
            {
                isConnected = true,
                status = "Connected",
                customerId = account.GoogleAdsCustomerId.ToString(),
                accountName = (string?)null
            });
        }

        // ---------- GET /api/googleads/overview/3 ----------
        [HttpGet("~/api/googleads/overview/{clientId:int}")]
        public async Task<IActionResult> Overview(int clientId)
        {
            var account = await FindAccountAsync(clientId);
            if (account?.GoogleAdsRefreshToken == null || account.GoogleAdsCustomerId == null)
                return NotFound(new { error = "Google Ads is not connected for this brand." });

            try
            {
                long customerId = account.GoogleAdsCustomerId.Value;
                long loginId = long.TryParse(_cfg["GoogleAds:LoginCustomerId"], out var l) ? l : customerId;

                var overview = await _googleAds.GetOverviewAsync(
                    account.GoogleAdsRefreshToken, loginId, customerId);

                return Ok(overview);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Google Ads overview failed");
                return StatusCode(500, new { error = ex.InnerException?.Message ?? ex.Message });
            }
        }

        // ---------- DELETE /api/googleads/disconnect/3 ----------
        [HttpDelete("~/api/googleads/disconnect/{clientId:int}")]
        public async Task<IActionResult> Disconnect(int clientId)
        {
            var account = await FindAccountAsync(clientId);
            if (account == null) return Ok(new { success = true });

            account.IsConnected = false;
            account.GoogleAdsRefreshToken = null;
            account.GoogleAdsCustomerId = null;
            await _db.SaveChangesAsync();

            return Ok(new { success = true });
        }

        // ---------- helpers ----------
        private ContentResult PopupResult(bool success, string message)
        {
            var safe = WebUtility.HtmlEncode(message);
            var html = $@"<!DOCTYPE html><html><body style='font-family:sans-serif;text-align:center;padding:40px'>
<h3>{(success ? "Google Ads connected" : "Connection failed")}</h3><p>{safe}</p>
<script>
  if (window.opener) {{
    window.opener.postMessage({{ type: '{(success ? "googleads-connected" : "googleads-failed")}' }}, window.location.origin);
  }}
  setTimeout(function () {{ window.close(); }}, 1500);
</script></body></html>";
            return Content(html, "text/html");
        }

        private async Task<(string access_token, string refresh_token, int expires_in)> ExchangeCodeForTokensAsync(string code)
        {
            var clientId = _cfg["GoogleAds:ClientId"] ?? Environment.GetEnvironmentVariable("GOOGLE_ADS_CLIENT_ID");
            var clientSecret = _cfg["GoogleAds:ClientSecret"] ?? Environment.GetEnvironmentVariable("GOOGLE_ADS_CLIENT_SECRET");

            using var client = new HttpClient();
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("code", code),
                new KeyValuePair<string, string>("client_id", clientId!),
                new KeyValuePair<string, string>("client_secret", clientSecret!),
                new KeyValuePair<string, string>("redirect_uri", GetRedirectUri()),
                new KeyValuePair<string, string>("grant_type", "authorization_code")
            });

            var resp = await client.PostAsync("https://oauth2.googleapis.com/token", content);
            resp.EnsureSuccessStatusCode();
            var payload = await resp.Content.ReadFromJsonAsync<GoogleTokenResponse>();
            return (payload!.access_token, payload.refresh_token, payload.expires_in);
        }

        private class GoogleTokenResponse
        {
            public string access_token { get; set; } = default!;
            public string refresh_token { get; set; } = default!;
            public int expires_in { get; set; }
        }
    }
}