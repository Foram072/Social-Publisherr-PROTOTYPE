using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace SocialPublisher.API.Controllers
{
    /// <summary>
    /// Helper controller for debugging social account configurations.
    /// Use this to verify tokens and page IDs are correct before publishing.
    /// </summary>
    [ApiController]
    [Route("api/social/debug")]
    public class DebugController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;

        public DebugController(IConfiguration configuration, IHttpClientFactory httpClientFactory)
        {
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// Tests the Facebook token and lists all pages the token has access to.
        /// Use this to find your correct Page ID.
        /// </summary>
        [HttpGet("facebook")]
        public async Task<IActionResult> TestFacebook()
        {
            var token = _configuration["SocialMedia:Facebook:AccessToken"];
            var configuredPageId = _configuration["SocialMedia:Facebook:PageId"];

            if (string.IsNullOrEmpty(token) || token == "YOUR_ACCESS_TOKEN_HERE")
            {
                return BadRequest(new
                {
                    error = "No Facebook access token configured. Set it in appsettings.json under SocialMedia:Facebook:AccessToken"
                });
            }

            var client = _httpClientFactory.CreateClient();

            // Test: Get user info
            var meResponse = await client.GetAsync($"https://graph.facebook.com/v19.0/me?access_token={token}");
            var meContent = await meResponse.Content.ReadAsStringAsync();

            // Test: Get all pages this token has access to
            var pagesResponse = await client.GetAsync($"https://graph.facebook.com/v19.0/me/accounts?access_token={token}");
            var pagesContent = await pagesResponse.Content.ReadAsStringAsync();

            return Ok(new
            {
                configured_page_id = configuredPageId,
                token_preview = token.Length > 20 ? token.Substring(0, 20) + "..." : token,
                me_response = System.Text.Json.JsonDocument.Parse(meContent).RootElement,
                pages_you_can_post_to = System.Text.Json.JsonDocument.Parse(pagesContent).RootElement,
                hint = "Look in 'pages_you_can_post_to.data' — find your page and confirm the 'id' matches your configured_page_id"
            });
        }

        /// <summary>
        /// Tests the Instagram configuration by checking linked Facebook Pages for Instagram Business Accounts.
        /// Use this to find your correct Instagram Account ID.
        /// </summary>
        [HttpGet("instagram")]
        public async Task<IActionResult> TestInstagram()
        {
            var token = _configuration["SocialMedia:Instagram:AccessToken"];
            var configuredAccountId = _configuration["SocialMedia:Instagram:AccountId"];

            if (string.IsNullOrEmpty(token) || token == "YOUR_ACCESS_TOKEN_HERE")
            {
                return BadRequest(new
                {
                    error = "No Instagram access token configured. Set it in appsettings.json under SocialMedia:Instagram:AccessToken"
                });
            }

            var client = _httpClientFactory.CreateClient();

            // To find an Instagram Business Account, we must query the user's Facebook Pages
            // and request the 'instagram_business_account' field.
            var url = $"https://graph.facebook.com/v19.0/me/accounts?fields=id,name,instagram_business_account&access_token={token}";
            var pagesResponse = await client.GetAsync(url);
            var pagesContent = await pagesResponse.Content.ReadAsStringAsync();

            return Ok(new
            {
                configured_account_id = configuredAccountId,
                token_preview = token.Length > 20 ? token.Substring(0, 20) + "..." : token,
                pages_with_instagram_info = System.Text.Json.JsonDocument.Parse(pagesContent).RootElement,
                hint = "Look in 'data'. If a page has an Instagram account linked, you will see 'instagram_business_account' with an 'id'. Copy that ID to your appsettings.json."
            });
        }
    }
}
