
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.Ads.GoogleAds.Config;
using Google.Ads.GoogleAds.Lib;
using Google.Ads.GoogleAds.V26.Errors;
using Google.Ads.GoogleAds.V26.Services;
using Microsoft.Extensions.Configuration;
using SocialPublisher.API.Models;
using GadsServices = Google.Ads.GoogleAds.Services;

namespace SocialPublisher.API.Services
{
    public class GoogleAdsService : IGoogleAdsService
    {
        private readonly ITokenProtector _protector;
        private readonly IConfiguration _config;

        public GoogleAdsService(
            ITokenProtector protector,
            IConfiguration config)
        {
            _protector = protector
                ?? throw new ArgumentNullException(nameof(protector));

            _config = config
                ?? throw new ArgumentNullException(nameof(config));
        }

        private GoogleAdsClient CreateClient(
            string refreshToken,
            long? loginCustomerId)
        {
            var adsConfig = new GoogleAdsConfig
            {
                DeveloperToken = _config["GoogleAds:DeveloperToken"],
                OAuth2ClientId = _config["GoogleAds:ClientId"],
                OAuth2ClientSecret = _config["GoogleAds:ClientSecret"],
                OAuth2RefreshToken = refreshToken
            };

            if (loginCustomerId.HasValue)
            {
                adsConfig.LoginCustomerId =
                    loginCustomerId.Value.ToString();
            }

            return new GoogleAdsClient(adsConfig);
        }

        private GoogleAdsClient BuildClient(
            string encryptedRefreshToken,
            long loginCustomerId)
        {
            if (string.IsNullOrWhiteSpace(encryptedRefreshToken))
            {
                throw new ArgumentException(
                    "Refresh token must be provided.",
                    nameof(encryptedRefreshToken));
            }

            string refreshToken =
                _protector.Unprotect(encryptedRefreshToken);

            return CreateClient(refreshToken, loginCustomerId);
        }

        public async Task<long?> GetFirstAccessibleCustomerIdAsync(
            string plainRefreshToken)
        {
            if (string.IsNullOrWhiteSpace(plainRefreshToken))
            {
                throw new ArgumentException(
                    "Refresh token must be provided.",
                    nameof(plainRefreshToken));
            }

            var client = CreateClient(plainRefreshToken, null);

            CustomerServiceClient customerService =
                client.GetService(GadsServices.V26.CustomerService);

            var response =
                await customerService.ListAccessibleCustomersAsync(
                    new ListAccessibleCustomersRequest());

            string resourceName =
                response.ResourceNames.FirstOrDefault();

            if (string.IsNullOrWhiteSpace(resourceName))
                return null;

            string idText = resourceName.Split('/').Last();

            return long.TryParse(idText, out long customerId)
                ? customerId
                : null;
        }

        public async Task<IReadOnlyList<(long CampaignId, string CampaignName)>>
            ListCampaignsAsync(
                string encryptedRefreshToken,
                long loginCustomerId,
                long customerId)
        {
            var client = BuildClient(
                encryptedRefreshToken,
                loginCustomerId);

            GoogleAdsServiceClient service =
                client.GetService(GadsServices.V26.GoogleAdsService);

            var request = new SearchGoogleAdsRequest
            {
                CustomerId = customerId.ToString(),
                Query = @"
                    SELECT campaign.id, campaign.name
                    FROM campaign
                    WHERE campaign.status = ENABLED"
            };

            var campaigns =
                new List<(long CampaignId, string CampaignName)>();

            try
            {
                await foreach (GoogleAdsRow row
                    in service.SearchAsync(request))
                {
                    campaigns.Add((
                        row.Campaign.Id,
                        row.Campaign.Name));
                }
            }
            catch (GoogleAdsException ex)
            {
                throw new InvalidOperationException(
                    $"Google Ads API error: {ex.Message}", ex);
            }

            return campaigns;
        }

        public async Task<GoogleAdsOverviewDto> GetOverviewAsync(
            string encryptedRefreshToken,
            long loginCustomerId,
            long customerId)
        {
            var client = BuildClient(
                encryptedRefreshToken,
                loginCustomerId);

            GoogleAdsServiceClient service =
                client.GetService(GadsServices.V26.GoogleAdsService);

            var overview = new GoogleAdsOverviewDto
            {
                CustomerId = customerId.ToString(),
                DateRangeLabel = "Last 30 Days"
            };

            try
            {
                var customerRequest = new SearchGoogleAdsRequest
                {
                    CustomerId = customerId.ToString(),
                    Query = @"
                        SELECT customer.currency_code,
                               customer.descriptive_name
                        FROM customer
                        LIMIT 1"
                };

                await foreach (GoogleAdsRow row
                    in service.SearchAsync(customerRequest))
                {
                    overview.CurrencyCode =
                        row.Customer.CurrencyCode;

                    overview.AccountName =
                        row.Customer.DescriptiveName;

                    break;
                }

                var campaignRequest = new SearchGoogleAdsRequest
                {
                    CustomerId = customerId.ToString(),
                    Query = @"
                        SELECT campaign.id,
                               campaign.name,
                               campaign.status,
                               campaign.advertising_channel_type,
                               campaign_budget.amount_micros,
                               metrics.cost_micros,
                               metrics.impressions,
                               metrics.clicks,
                               metrics.ctr,
                               metrics.average_cpc,
                               metrics.conversions
                        FROM campaign
                        WHERE segments.date DURING LAST_30_DAYS
                          AND campaign.status != 'REMOVED'
                        ORDER BY metrics.cost_micros DESC"
                };

                await foreach (GoogleAdsRow row
                    in service.SearchAsync(campaignRequest))
                {
                    overview.Campaigns.Add(
                        new GoogleAdsCampaignDto
                        {
                            CampaignId =
                                row.Campaign.Id.ToString(),

                            CampaignName =
                                row.Campaign.Name,

                            Status = row.Campaign.Status
                                .ToString()
                                .ToUpperInvariant(),

                            CampaignType =
                                row.Campaign.AdvertisingChannelType
                                    .ToString(),

                            Budget =
                                (decimal)row.CampaignBudget.AmountMicros
                                / 1_000_000m,

                            Spend =
                                (decimal)row.Metrics.CostMicros
                                / 1_000_000m,

                            Impressions =
                                row.Metrics.Impressions,

                            Clicks =
                                row.Metrics.Clicks,

                            CTR =
                                (decimal)row.Metrics.Ctr * 100m,

                            AvgCPC =
                                (decimal)row.Metrics.AverageCpc
                                / 1_000_000m,

                            // DTO declares Conversions as long.
                            // Round the API's fractional metric.
                            Conversions =
                                (long)Math.Round(
                                    row.Metrics.Conversions),

                            CostPerConversion =
                                row.Metrics.Conversions > 0
                                    ? ((decimal)row.Metrics.CostMicros
                                        / 1_000_000m)
                                        / (decimal)row.Metrics.Conversions
                                    : 0m
                        });
                }
            }
            catch (GoogleAdsException ex)
            {
                throw new InvalidOperationException(
                    $"Google Ads API error: {ex.Message}", ex);
            }

            overview.TotalSpend =
                overview.Campaigns.Sum(c => c.Spend);

            overview.TotalImpressions =
                overview.Campaigns.Sum(c => c.Impressions);

            overview.TotalClicks =
                overview.Campaigns.Sum(c => c.Clicks);

            overview.TotalConversions =
                overview.Campaigns.Sum(c =>
                    (double)c.Conversions);

            // DTO declares CTR as decimal, not double.
            overview.CTR = overview.TotalImpressions > 0
                ? (decimal)overview.TotalClicks
                    / overview.TotalImpressions * 100m
                : 0m;

            overview.AvgCPC = overview.TotalClicks > 0
                ? overview.TotalSpend / overview.TotalClicks
                : 0m;

            return overview;
        }
    }
}
