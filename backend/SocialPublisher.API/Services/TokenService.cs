using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace SocialPublisher.API.Services
{
    public class TokenService
    {
        private readonly IConfiguration _cfg;

        public TokenService(IConfiguration cfg)
        {
            _cfg = cfg;
        }

        public string Create(int userId, string email, string fullName)
        {
            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, email),
                new Claim("name", fullName)
            };

            var jwtKey = _cfg["Jwt:Key"] ?? "a-very-secret-encryption-key-for-wiseliy-social-media-jwt-tokens-at-least-32-chars";
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _cfg["Jwt:Issuer"] ?? "wiseliy",
                audience: _cfg["Jwt:Audience"] ?? "wiseliy-social",
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(int.Parse(_cfg["Jwt:ExpiryMinutes"] ?? "1440")), // default 24h
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
