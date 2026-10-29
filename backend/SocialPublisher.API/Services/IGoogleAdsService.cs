using System.Collections.Generic;
using System.Threading.Tasks;
using SocialPublisher.API.Models;

namespace SocialPublisher.API.Services
{
    public interface IGoogleAdsService
    {
        Task<long?> GetFirstAccessibleCustomerIdAsync(
            string plainRefreshToken);

        Task<IReadOnlyList<(long CampaignId, string CampaignName)>>
            ListCampaignsAsync(
                string encryptedRefreshToken,
                long loginCustomerId,
                long customerId);

        Task<GoogleAdsOverviewDto> GetOverviewAsync(
            string encryptedRefreshToken,
            long loginCustomerId,
            long customerId);
    }
}
