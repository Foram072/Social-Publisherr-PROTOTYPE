using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SocialPublisher.API.Data;
using SocialPublisher.API.Models;
using SocialPublisher.API.Services;
using Microsoft.Extensions.Configuration;
using System.Linq;
using System.Threading.Tasks;

namespace SocialPublisher.API.Controllers
{
    [ApiController]
    [Route("api/social/googleads")]
    public class GoogleAdsCampaignController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IGoogleAdsService _ads;
        private readonly IConfiguration _cfg;

        public GoogleAdsCampaignController(ApplicationDbContext db, IGoogleAdsService ads, IConfiguration cfg)
        {
            _db = db;
            _ads = ads;
            _cfg = cfg;
        }

        // GET: /api/social/googleads/{clientId}/campaigns
        [HttpGet("{clientId}/campaigns")]
        public async Task<IActionResult> GetCampaigns(int clientId)
        {
            var account = await _db.SocialAccounts
                .Where(a => a.ClientId == clientId && a.Platform == SocialAccount.PlatformGoogleAds && a.IsConnected)
                .FirstOrDefaultAsync();

            if (account == null || string.IsNullOrWhiteSpace(account.GoogleAdsRefreshToken) || account.GoogleAdsCustomerId == null)
                return BadRequest(new { error = "Google Ads not connected for this client." });

            var loginCustomerId = long.Parse(
                _cfg["GoogleAds:LoginCustomerId"] ?? Environment.GetEnvironmentVariable("GOOGLE_ADS_LOGIN_CUSTOMER_ID") ?? "0");

            var campaigns = await _ads.ListCampaignsAsync(
                account.GoogleAdsRefreshToken!,
                loginCustomerId,
                account.GoogleAdsCustomerId!.Value);

            return Ok(campaigns.Select(c => new { c.CampaignId, c.CampaignName }));
        }
    }
}
