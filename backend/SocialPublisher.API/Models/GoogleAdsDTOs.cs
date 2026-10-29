using System;
using System.Collections.Generic;

namespace SocialPublisher.API.Models
{
    /// <summary>
    /// DTO returned when listing accessible Google Ads customer accounts.
    /// </summary>
    public class GoogleAdsCustomerDto
    {
        public string CustomerId { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string CurrencyCode { get; set; } = "USD";
        public string TimeZone { get; set; } = string.Empty;
        public bool IsManager { get; set; }
    }

    /// <summary>
    /// KPI overview for the Google Ads dashboard cards.
    /// </summary>
    public class GoogleAdsOverviewDto
    {
        public int ClientId { get; set; }
        public string CustomerId { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string CurrencyCode { get; set; } = "USD";
        public bool IsConnected { get; set; }
        public decimal TotalSpend { get; set; }
        public long TotalImpressions { get; set; }
        public long TotalClicks { get; set; }
        public double TotalConversions { get; set; }
        public decimal CTR { get; set; }
        public decimal AvgCPC { get; set; }
        public string DateRangeLabel { get; set; } = "Last 30 Days";
        public List<GoogleAdsCampaignDto> Campaigns { get; set; } = new();
        public List<GoogleAdsDailyMetricDto> DailyMetrics { get; set; } = new();
    }

    /// <summary>
    /// A single Google Ads campaign row.
    /// </summary>
    public class GoogleAdsCampaignDto
    {
        public string CampaignId { get; set; } = string.Empty;
        public string CampaignName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string CampaignType { get; set; } = string.Empty;
        public decimal Budget { get; set; }
        public decimal Spend { get; set; }
        public long Impressions { get; set; }
        public long Clicks { get; set; }
        public decimal CTR { get; set; }
        public decimal AvgCPC { get; set; }
        public long Conversions { get; set; }
        public decimal CostPerConversion { get; set; }
    }

    /// <summary>
    /// Daily performance data point for charts.
    /// </summary>
    public class GoogleAdsDailyMetricDto
    {
        public string Date { get; set; } = string.Empty;
        public long Impressions { get; set; }
        public long Clicks { get; set; }
        public decimal Spend { get; set; }
        public long Conversions { get; set; }
    }

    /// <summary>
    /// Request body for selecting/connecting a Google Ads customer account.
    /// </summary>
    public class GoogleAdsSelectAccountRequest
    {
        public int ClientId { get; set; }
        public string CustomerId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Status of the Google Ads connection for a client workspace.
    /// </summary>
    public class GoogleAdsConnectionStatusDto
    {
        public bool IsConnected { get; set; }
        public string CustomerId { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public DateTime? TokenExpiresAt { get; set; }
        public string Status { get; set; } = "NotConnected";
    }
}
