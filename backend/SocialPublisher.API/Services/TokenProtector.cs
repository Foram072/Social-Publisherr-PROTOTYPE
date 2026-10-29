using Microsoft.AspNetCore.DataProtection;

namespace SocialPublisher.API.Services
{
    public interface ITokenProtector
    {
        string Protect(string plain);
        string Unprotect(string protectedText);
    }

    public class TokenProtector : ITokenProtector
    {
        private readonly IDataProtector _protector;

        public TokenProtector(IDataProtectionProvider provider)
        {
            // "GoogleAdsRefreshToken" is the purpose string – change if you reuse for other tokens.
            _protector = provider.CreateProtector("GoogleAdsRefreshToken");
        }

        public string Protect(string plain) => _protector.Protect(plain);
        public string Unprotect(string protectedText) => _protector.Unprotect(protectedText);
    }
}
