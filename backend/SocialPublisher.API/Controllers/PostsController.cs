using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SocialPublisher.API.Data;
using SocialPublisher.API.Models;
using SocialPublisher.API.Services;

namespace SocialPublisher.API.Controllers
{
    [ApiController]
    [Route("api/social/posts")]
    public class PostsController : ControllerBase
    {
        private readonly SocialPublishingService _publishingService;
        private readonly ApplicationDbContext _context;

        public PostsController(SocialPublishingService publishingService, ApplicationDbContext context)
        {
            _publishingService = publishingService;
            _context = context;
        }

        /// <summary>
        /// Creates and publishes a new post to the selected social media platforms.
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<PublishResponse>> CreatePost(
            [FromBody] PublishRequest request,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.Content))
                return BadRequest(new { error = "Post content is required." });

            if (request.Platforms == null || request.Platforms.Count == 0)
                return BadRequest(new { error = "Please select at least one platform." });

            // If scheduled for a future time, record it as Scheduled without immediate publishing
            if (request.ScheduledAt.HasValue && request.ScheduledAt.Value > System.DateTime.UtcNow)
            {
                var scheduledPost = new SocialPost
                {
                    Title = request.Title ?? string.Empty,
                    Content = request.Content,
                    Status = "Scheduled",
                    CreatedAt = System.DateTime.UtcNow,
                    ScheduledAt = request.ScheduledAt.Value,
                    ScheduledPlatforms = string.Join(",", request.Platforms)
                };

                var mediaIds = (request.MediaIds ?? new List<int>()).Distinct().ToList();
                if (mediaIds.Count > 0)
                {
                    var mediaItems = await _context.SocialPostMedia
                        .Where(m => mediaIds.Contains(m.Id) && m.PostId == null)
                        .ToListAsync(cancellationToken);

                    foreach (var m in mediaItems)
                        scheduledPost.Media.Add(m);
                }

                _context.SocialPosts.Add(scheduledPost);
                await _context.SaveChangesAsync(cancellationToken);

                return Ok(new PublishResponse
                {
                    PostId = scheduledPost.Id.ToString(),
                    Status = "Scheduled",
                    Results = request.Platforms.Select(p => new PublishResultDto
                    {
                        Platform = p.ToLowerInvariant(),
                        Status = "Scheduled"
                    }).ToList()
                });
            }

            var response = await _publishingService.PublishPostAsync(request, cancellationToken);
            return Ok(response);
        }

        /// <summary>
        /// Returns publishing history for all posts.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetPosts(CancellationToken cancellationToken)
        {
            var posts = await _context.SocialPosts
                .Include(p => p.PublishResults)
                    .ThenInclude(r => r.SocialAccount)
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => new
                {
                    p.Id,
                    p.Title,
                    p.Content,
                    p.Status,
                    p.CreatedAt,
                    p.ScheduledAt,
                    Results = p.PublishResults.Select(r => new
                    {
                        r.Id,
                        Platform = r.SocialAccount.Platform,
                        r.Status,
                        r.ExternalPostId,
                        r.PostUrl,
                        r.ErrorMessage,
                        r.PublishedAt
                    })
                })
                .ToListAsync(cancellationToken);

            return Ok(posts);
        }

        /// <summary>
        /// Returns the publishing result for a specific post.
        /// </summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetPost(int id, CancellationToken cancellationToken)
        {
            var post = await _context.SocialPosts
                .Include(p => p.PublishResults)
                    .ThenInclude(r => r.SocialAccount)
                .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

            if (post == null)
                return NotFound(new { error = $"Post {id} not found." });

            return Ok(new
            {
                post.Id,
                post.Title,
                post.Content,
                post.Status,
                post.CreatedAt,
                Results = post.PublishResults.Select(r => new
                {
                    r.Id,
                    Platform = r.SocialAccount.Platform,
                    r.Status,
                    r.ExternalPostId,
                    r.PostUrl,
                    r.ErrorMessage,
                    r.PublishedAt
                })
            });
        }

        /// <summary>
        /// Retries publishing only on failed platforms.
        /// Already published platforms are NOT republished.
        /// </summary>
        [HttpPost("{id}/retry")]
        public async Task<ActionResult<PublishResponse>> RetryPost(int id, CancellationToken cancellationToken)
        {
            var response = await _publishingService.RetryFailedAsync(id, cancellationToken);
            return Ok(response);
        }
    }
}
