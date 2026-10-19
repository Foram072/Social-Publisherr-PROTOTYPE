using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SocialPublisher.API.Data;
using SocialPublisher.API.Models;

namespace SocialPublisher.API.Controllers
{
    [ApiController]
    [Route("api/social/invitations")]
    public class ClientInvitationController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;

        public ClientInvitationController(ApplicationDbContext context, Microsoft.Extensions.Configuration.IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        public class CreateInvitationRequest
        {
            public int ClientId { get; set; }
            public string? RecipientEmail { get; set; }
            public string? RecipientName { get; set; }
            public string? Message { get; set; }
        }

        /// <summary>
        /// Agency generates a secure link to send to a client to connect their social accounts.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateInvitation([FromBody] CreateInvitationRequest request)
        {
            var client = await _context.SocialClients.FindAsync(request.ClientId);
            if (client == null)
            {
                return NotFound(new { error = "Brand Workspace / Client not found." });
            }

            var invitation = new ClientConnectionInvitation
            {
                ClientId = request.ClientId,
                RecipientEmail = request.RecipientEmail?.Trim(),
                RecipientName = request.RecipientName?.Trim(),
                Message = request.Message?.Trim(),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                Token = Guid.NewGuid().ToString("N")
            };

            _context.ClientConnectionInvitations.Add(invitation);
            await _context.SaveChangesAsync();

            // Check if there is a public tunnel URL configured, otherwise fallback to request host / LAN
            var publicBase = _configuration["SocialMedia:PublicBaseUrl"];
            string host;
            if (!string.IsNullOrWhiteSpace(publicBase))
            {
                host = publicBase.TrimEnd('/');
            }
            else
            {
                host = Request.Scheme + "://" + Request.Host.Value;
            }

            var connectionUrl = $"{host}/connect-client.html?token={invitation.Token}";

            return Ok(new
            {
                success = true,
                invitationId = invitation.Id,
                token = invitation.Token,
                connectionUrl,
                expiresAt = invitation.ExpiresAt,
                clientName = client.Name
            });
        }

        /// <summary>
        /// Client opens the delegated onboarding page with their invitation token.
        /// Validates the token and returns the Brand info and available networks.
        /// </summary>
        [HttpGet("resolve/{token}")]
        public async Task<IActionResult> ResolveInvitation(string token)
        {
            var invitation = await _context.ClientConnectionInvitations
                .Include(i => i.Client)
                .FirstOrDefaultAsync(i => i.Token == token);

            if (invitation == null)
            {
                return NotFound(new { error = "Invalid or expired invitation link." });
            }

            if (invitation.ExpiresAt < DateTime.UtcNow)
            {
                return BadRequest(new { error = "This connection link has expired. Please ask your agency to send a new invitation link." });
            }

            return Ok(new
            {
                valid = true,
                clientId = invitation.ClientId,
                clientName = invitation.Client?.Name,
                recipientName = invitation.RecipientName,
                recipientEmail = invitation.RecipientEmail,
                message = invitation.Message,
                expiresAt = invitation.ExpiresAt
            });
        }
    }
}
