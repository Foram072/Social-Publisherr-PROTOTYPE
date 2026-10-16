using System.Threading;
using System.Threading.Tasks;
using SocialPublisher.API.Models;

namespace SocialPublisher.API.Services
{
    public interface ISocialPublisher
    {
        string PlatformName { get; }
        Task<PublishResultDto> PublishAsync(SocialPost post, SocialAccount account, CancellationToken cancellationToken);
    }
}
