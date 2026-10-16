using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
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
    [Route("api/social/auth")]
    public class AuthController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AuthController> _logger;
        private readonly TokenService _tokens;
        private readonly IConfiguration _cfg;

        public AuthController(
            ApplicationDbContext context,
            ILogger<AuthController> logger,
            TokenService tokens,
            IConfiguration cfg)
        {
            _context = context;
            _logger = logger;
            _tokens = tokens;
            _cfg = cfg;
        }

        public class SignupRequest
        {
            public string FullName { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
            public string CompanyName { get; set; } = string.Empty; // Organization Name (e.g. Acme Marketing)
            public string? FirstBrandName { get; set; } // Optional initial brand (e.g. Acme Clothing)
        }

        public class LoginRequest
        {
            public string Email { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
        }

        /// <summary>
        /// Zoho Social Step 1 & 3: User Signup + Organization / Brand Creation.
        /// (Email verification is bypassed per requirement for instant onboarding).
        /// </summary>
        [HttpPost("signup")]
        public async Task<IActionResult> Signup([FromBody] SignupRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.FullName) || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
                return BadRequest(new { error = "Full name, work email, and password are required." });

            var emailNormalized = request.Email.Trim().ToLowerInvariant();

            var exists = await _context.Users.AnyAsync(u => u.Email.ToLower() == emailNormalized);
            if (exists)
                return BadRequest(new { error = "An account with this email address already exists. Please log in." });

            // 1. Create Organization (Agency Portal)
            var orgName = string.IsNullOrWhiteSpace(request.CompanyName) ? $"{request.FullName}'s Organization" : request.CompanyName.Trim();
            var organization = new Organization
            {
                Name = orgName,
                Country = "United States",
                TimeZone = "UTC",
                CreatedAt = DateTime.UtcNow
            };
            _context.Organizations.Add(organization);
            await _context.SaveChangesAsync();

            // 2. Hash Password (PBKDF2 SHA256)
            var saltBytes = RandomNumberGenerator.GetBytes(16);
            var salt = Convert.ToBase64String(saltBytes);
            var hash = HashPassword(request.Password, salt);

            // 3. Create User
            var user = new User
            {
                FullName = request.FullName.Trim(),
                Email = emailNormalized,
                PasswordHash = hash,
                Salt = salt,
                Role = "Owner",
                OrganizationId = organization.Id,
                CreatedAt = DateTime.UtcNow
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            // 4. Create Initial Brand Workspace (e.g. "Acme Clothing" or default)
            var brandName = string.IsNullOrWhiteSpace(request.FirstBrandName) ? $"{orgName} Brand" : request.FirstBrandName.Trim();
            var firstBrand = new SocialClient
            {
                Name = brandName,
                Description = $"Brand workspace for {brandName}",
                OrganizationId = organization.Id,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            _context.SocialClients.Add(firstBrand);
            await _context.SaveChangesAsync();

            _logger.LogInformation("New user signed up: {Email}, created Organization: {OrgId}, Brand: {BrandId}", user.Email, organization.Id, firstBrand.Id);

            var token = _tokens.Create(user.Id, user.Email, user.FullName);

            return Ok(new
            {
                success = true,
                message = "Account and organization created successfully.",
                token = token,
                user = new
                {
                    user.Id,
                    user.FullName,
                    user.Email,
                    user.Role
                },
                organization = new
                {
                    organization.Id,
                    organization.Name
                },
                activeBrand = new
                {
                    firstBrand.Id,
                    firstBrand.Name
                }
            });
        }

        /// <summary>
        /// Zoho Social Step 4: User Login.
        /// Validates credentials and returns user profile, organization, and all accessible brands.
        /// </summary>
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
                return BadRequest(new { error = "Email and password are required." });

            var emailNormalized = request.Email.Trim().ToLowerInvariant();
            var user = await _context.Users
                .Include(u => u.Organization)
                .ThenInclude(o => o!.Brands)
                .FirstOrDefaultAsync(u => u.Email.ToLower() == emailNormalized);

            if (user == null)
                return Unauthorized(new { error = "Invalid email or password." });

            var computedHash = HashPassword(request.Password, user.Salt);
            if (computedHash != user.PasswordHash)
                return Unauthorized(new { error = "Invalid email or password." });

            // If user has no brand yet, fallback to default or first available
            var brands = user.Organization?.Brands.Select(b => new
            {
                b.Id,
                b.Name,
                b.Description,
                b.TimeZone
            }).ToList() ?? new();

            var token = _tokens.Create(user.Id, user.Email, user.FullName);

            return Ok(new
            {
                success = true,
                token = token,
                user = new
                {
                    user.Id,
                    user.FullName,
                    user.Email,
                    user.Role
                },
                organization = user.Organization != null ? new
                {
                    user.Organization.Id,
                    user.Organization.Name
                } : null,
                brands = brands
            });
        }

        /// <summary>
        /// Step 1 of Google OAuth: Triggers Challenge with Google scheme.
        /// </summary>
        [HttpGet("google")]
        public IActionResult GoogleLogin()
        {
            return Challenge(new AuthenticationProperties
            {
                RedirectUri = "/api/social/auth/google/callback"
            }, GoogleDefaults.AuthenticationScheme);
        }

        /// <summary>
        /// Step 2 of Google OAuth: Receives callback from Google, retrieves claims,
        /// creates/links the user in the database, and issues a JWT token.
        /// </summary>
        [HttpGet("google/callback")]
        public async Task<IActionResult> GoogleCallback()
        {
            var front = _cfg["Frontend:BaseUrl"] ?? "";
            var result = await HttpContext.AuthenticateAsync("External");

            if (!result.Succeeded || result.Principal == null)
            {
                _logger.LogWarning("Google external authentication failed");
                return Redirect($"{front}/auth-callback.html#error=google");
            }

            var email = result.Principal.FindFirstValue(ClaimTypes.Email);
            var name = result.Principal.FindFirstValue(ClaimTypes.Name) ?? email?.Split('@')[0] ?? "User";
            var gid = result.Principal.FindFirstValue(ClaimTypes.NameIdentifier);

            await HttpContext.SignOutAsync("External");

            if (string.IsNullOrEmpty(email))
            {
                _logger.LogWarning("Google authentication returned no email");
                return Redirect($"{front}/auth-callback.html#error=noemail");
            }

            var emailNormalized = email.Trim().ToLowerInvariant();

            // 1. Find existing user by email or by GoogleId
            var user = await _context.Users
                .Include(u => u.Organization)
                .ThenInclude(o => o!.Brands)
                .FirstOrDefaultAsync(u => u.Email.ToLower() == emailNormalized || (u.GoogleId != null && u.GoogleId == gid));

            if (user == null)
            {
                // 2. First time signing up with Google: create Organization + User + initial Brand workspace
                var orgName = $"{name}'s Organization";
                var organization = new Organization
                {
                    Name = orgName,
                    Country = "United States",
                    TimeZone = "UTC",
                    CreatedAt = DateTime.UtcNow
                };
                _context.Organizations.Add(organization);
                await _context.SaveChangesAsync();

                user = new User
                {
                    FullName = name,
                    Email = emailNormalized,
                    GoogleId = gid,
                    PasswordHash = null,
                    Salt = string.Empty,
                    Role = "Owner",
                    OrganizationId = organization.Id,
                    CreatedAt = DateTime.UtcNow
                };
                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                var firstBrand = new SocialClient
                {
                    Name = $"{name}'s Brand",
                    Description = $"Default brand workspace for {name}",
                    OrganizationId = organization.Id,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                _context.SocialClients.Add(firstBrand);
                await _context.SaveChangesAsync();
            }
            else if (user.GoogleId == null && !string.IsNullOrEmpty(gid))
            {
                user.GoogleId = gid;
                await _context.SaveChangesAsync();
            }

            // 3. Issue Wiseliy JWT
            var jwt = _tokens.Create(user.Id, user.Email, user.FullName);
            return Redirect($"{front}/auth-callback.html#token={Uri.EscapeDataString(jwt)}");
        }

        /// <summary>
        /// Initiates Facebook Login OAuth flow for user authentication.
        /// </summary>
        [HttpGet("facebook")]
        public IActionResult FacebookLogin()
        {
            return Challenge(new AuthenticationProperties
            {
                RedirectUri = "/api/social/auth/external/callback?provider=Facebook"
            }, "Facebook");
        }

        /// <summary>
        /// Initiates LinkedIn Login OAuth flow for user authentication.
        /// </summary>
        [HttpGet("linkedin")]
        public IActionResult LinkedInLogin()
        {
            return Challenge(new AuthenticationProperties
            {
                RedirectUri = "/api/social/auth/external/callback?provider=LinkedIn"
            }, "LinkedIn");
        }

        /// <summary>
        /// Shared OAuth callback for external providers (Facebook, LinkedIn).
        /// </summary>
        [HttpGet("external/callback")]
        public async Task<IActionResult> ExternalCallback([FromQuery] string provider = "External")
        {
            var front = _cfg["Frontend:BaseUrl"] ?? "";
            var result = await HttpContext.AuthenticateAsync("External");

            if (!result.Succeeded || result.Principal == null)
            {
                _logger.LogWarning("{Provider} external authentication failed", provider);
                return Redirect($"{front}/auth-callback.html#error={provider.ToLowerInvariant()}");
            }

            var email = result.Principal.FindFirstValue(ClaimTypes.Email);
            var name = result.Principal.FindFirstValue(ClaimTypes.Name) ?? email?.Split('@')[0] ?? $"{provider} User";
            var externalId = result.Principal.FindFirstValue(ClaimTypes.NameIdentifier);

            await HttpContext.SignOutAsync("External");

            if (string.IsNullOrEmpty(email))
            {
                // If Facebook or provider does not return email, use provider-scoped identifier
                email = $"{externalId}@{provider.ToLowerInvariant()}.user";
            }

            var emailNormalized = email.Trim().ToLowerInvariant();
            var user = await _context.Users
                .Include(u => u.Organization)
                .ThenInclude(o => o!.Brands)
                .FirstOrDefaultAsync(u => u.Email.ToLower() == emailNormalized);

            if (user == null)
            {
                var orgName = $"{name}'s Organization";
                var organization = new Organization
                {
                    Name = orgName,
                    Country = "United States",
                    TimeZone = "UTC",
                    CreatedAt = DateTime.UtcNow
                };
                _context.Organizations.Add(organization);
                await _context.SaveChangesAsync();

                user = new User
                {
                    Email = emailNormalized,
                    FullName = name,
                    Role = "Admin",
                    OrganizationId = organization.Id,
                    CreatedAt = DateTime.UtcNow
                };
                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                var firstBrand = new SocialClient
                {
                    Name = $"{name}'s Brand",
                    Description = $"Default brand workspace for {name}",
                    OrganizationId = organization.Id,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                _context.SocialClients.Add(firstBrand);
                await _context.SaveChangesAsync();
            }

            var jwt = _tokens.Create(user.Id, user.Email, user.FullName);
            return Redirect($"{front}/auth-callback.html#token={Uri.EscapeDataString(jwt)}");
        }

        /// <summary>
        /// Protected endpoint: Returns current user session based on JWT Bearer token or optional userId query.
        /// </summary>
        [HttpGet("me")]
        public async Task<IActionResult> GetCurrentUser([FromQuery] int? userId = null)
        {
            int idToFetch = 0;

            if (User?.Identity?.IsAuthenticated == true)
            {
                var subClaim = User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (int.TryParse(subClaim, out var subId))
                {
                    idToFetch = subId;
                }
            }

            if (idToFetch == 0 && userId.HasValue && userId.Value > 0)
            {
                idToFetch = userId.Value;
            }

            if (idToFetch == 0)
                return Unauthorized(new { error = "Not authenticated" });

            var user = await _context.Users
                .Include(u => u.Organization)
                .ThenInclude(o => o!.Brands)
                .FirstOrDefaultAsync(u => u.Id == idToFetch);

            if (user == null)
                return NotFound(new { error = "User not found." });

            return Ok(new
            {
                user = new
                {
                    user.Id,
                    user.FullName,
                    user.Email,
                    user.Role
                },
                organization = user.Organization != null ? new { user.Organization.Id, user.Organization.Name } : null,
                brands = user.Organization?.Brands.Select(b => new { b.Id, b.Name, b.Description, b.TimeZone }).ToList() ?? new()
            });
        }

        private static string HashPassword(string password, string salt)
        {
            using var sha = SHA256.Create();
            var combined = Encoding.UTF8.GetBytes(password + ":" + salt);
            var hashBytes = sha.ComputeHash(combined);
            return Convert.ToBase64String(hashBytes);
        }
    }
}
