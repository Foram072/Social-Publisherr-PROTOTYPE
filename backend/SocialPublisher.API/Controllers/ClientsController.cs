using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SocialPublisher.API.Data;
using SocialPublisher.API.Models;

namespace SocialPublisher.API.Controllers
{
    [ApiController]
    [Route("api/social/clients")]
    public class ClientsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ClientsController> _logger;

        public ClientsController(ApplicationDbContext context, ILogger<ClientsController> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Lists all client/brand workspaces managed by the agency (e.g. ABC Interiors, Brand X)
        /// with their count of connected accounts.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetClients()
        {
            var clients = await _context.SocialClients
                .Include(c => c.Accounts)
                .OrderBy(c => c.Id)
                .Select(c => new
                {
                    c.Id,
                    c.Name,
                    c.Description,
                    c.TimeZone,
                    c.IsActive,
                    c.CreatedAt,
                    ConnectedAccountsCount = c.Accounts.Count(a => a.IsConnected),
                    Accounts = c.Accounts.Select(a => new
                    {
                        a.Id,
                        a.Platform,
                        a.AccountName,
                        a.ExternalAccountId,
                        a.IsConnected,
                        a.AvatarUrl
                    })
                })
                .ToListAsync();

            return Ok(clients);
        }

        /// <summary>
        /// Gets details and connected social accounts for a specific client workspace.
        /// </summary>
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetClient(int id)
        {
            var client = await _context.SocialClients
                .Include(c => c.Accounts)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (client == null)
                return NotFound(new { error = $"Client with ID {id} not found." });

            return Ok(new
            {
                client.Id,
                client.Name,
                client.Description,
                client.TimeZone,
                client.IsActive,
                client.CreatedAt,
                Accounts = client.Accounts
                    .OrderByDescending(a => a.IsConnected)
                    .ThenByDescending(a => a.Id)
                    .Select(a => new
                    {
                        a.Id,
                        a.Platform,
                        a.AccountName,
                        a.ExternalAccountId,
                        a.IsConnected,
                        a.AvatarUrl
                    })
            });
        }

        public class CreateClientRequest
        {
            public string Name { get; set; } = string.Empty;
            public string? Description { get; set; }
            public string? TimeZone { get; set; }
            public int? OrganizationId { get; set; }
        }

        /// <summary>
        /// Creates a new client/brand workspace (e.g., adding a new digital marketing client).
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateClient([FromBody] CreateClientRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest(new { error = "Client workspace name is required." });

            int? orgId = request.OrganizationId;
            if (!orgId.HasValue)
            {
                // Fall back to first organization if available
                var defaultOrg = await _context.Organizations.FirstOrDefaultAsync();
                orgId = defaultOrg?.Id;
            }

            var client = new SocialClient
            {
                Name = request.Name.Trim(),
                Description = request.Description?.Trim(),
                TimeZone = string.IsNullOrWhiteSpace(request.TimeZone) ? "UTC" : request.TimeZone.Trim(),
                OrganizationId = orgId,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _context.SocialClients.Add(client);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Created new social client workspace '{Name}' (ID {Id}, OrgId: {OrgId})", client.Name, client.Id, client.OrganizationId);

            return CreatedAtAction(nameof(GetClient), new { id = client.Id }, new
            {
                client.Id,
                client.Name,
                client.Description,
                client.TimeZone,
                client.OrganizationId,
                client.IsActive,
                client.CreatedAt
            });
        }
    }
}
