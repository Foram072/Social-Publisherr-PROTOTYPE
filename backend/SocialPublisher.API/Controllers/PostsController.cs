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

            // If submitted for approval, save with NeedsApproval status
            if (request.SubmitForApproval)
            {
                var approvalPost = new SocialPost
                {
                    Title = request.Title ?? string.Empty,
                    Content = request.Content,
                    Status = "NeedsApproval",
                    CreatedAt = System.DateTime.UtcNow,
                    ScheduledAt = request.ScheduledAt,
                    ScheduledPlatforms = string.Join(",", request.Platforms),
                    ClientId = request.ClientId
                };

                var mediaIds = (request.MediaIds ?? new List<int>()).Distinct().ToList();
                if (mediaIds.Count > 0)
                {
                    var mediaItems = await _context.SocialPostMedia
                        .Where(m => mediaIds.Contains(m.Id) && m.PostId == null)
                        .ToListAsync(cancellationToken);

                    foreach (var m in mediaItems)
                        approvalPost.Media.Add(m);
                }

                _context.SocialPosts.Add(approvalPost);
                _context.SocialPostApprovals.Add(new SocialPostApproval
                {
                    Post = approvalPost,
                    Action = "Submitted",
                    ReviewerName = "Content Creator",
                    Comments = "Submitted for client/manager approval."
                });

                await _context.SaveChangesAsync(cancellationToken);

                return Ok(new PublishResponse
                {
                    PostId = approvalPost.Id.ToString(),
                    Status = "NeedsApproval",
                    Results = request.Platforms.Select(p => new PublishResultDto
                    {
                        Platform = p.ToLowerInvariant(),
                        Status = "Pending Approval"
                    }).ToList()
                });
            }

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
                    ScheduledPlatforms = string.Join(",", request.Platforms),
                    ClientId = request.ClientId
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
        /// Returns publishing history for posts (optionally filtered by Brand Workspace clientId).
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetPosts([FromQuery] int? clientId, CancellationToken cancellationToken)
        {
            var query = _context.SocialPosts.AsQueryable();

            if (clientId.HasValue && clientId.Value > 0)
            {
                query = query.Where(p => p.ClientId == clientId.Value);
            }

            var posts = await query
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
                    p.ClientId,
                    Approvals = _context.SocialPostApprovals
                        .Where(a => a.PostId == p.Id)
                        .OrderByDescending(a => a.CreatedAt)
                        .Select(a => new
                        {
                            a.Id,
                            a.Action,
                            a.ReviewerName,
                            a.Comments,
                            a.CreatedAt
                        })
                        .ToList(),
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

        /// <summary>
        /// Cancels a scheduled post so it won't be sent by the scheduler.
        /// </summary>
        [HttpPost("{id}/cancel")]
        public async Task<IActionResult> CancelScheduledPost(int id, CancellationToken cancellationToken)
        {
            var post = await _context.SocialPosts.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
            if (post == null)
                return NotFound(new { error = $"Post {id} not found." });

            if (post.Status != "Scheduled")
                return BadRequest(new { error = $"Only scheduled posts can be cancelled. Current status is {post.Status}." });

            post.Status = "Cancelled";
            await _context.SaveChangesAsync(cancellationToken);
            return Ok(new { success = true, postId = id, status = "Cancelled" });
        }

        /// <summary>
        /// Manually triggers immediate publishing of a scheduled post.
        /// </summary>
        [HttpPost("{id}/publish-now")]
        public async Task<ActionResult<PublishResponse>> PublishScheduledNow(int id, CancellationToken cancellationToken)
        {
            var post = await _context.SocialPosts.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
            if (post == null)
                return NotFound(new { error = $"Post {id} not found." });

            if (post.Status != "Scheduled")
                return BadRequest(new { error = $"Only scheduled posts can be published immediately via this endpoint." });

            var platforms = (post.ScheduledPlatforms ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            if (platforms.Count == 0)
                return BadRequest(new { error = "No platforms specified for this post." });

            var response = await _publishingService.PublishExistingPostAsync(post.Id, platforms, cancellationToken);
            return Ok(response);
        }

        public class ApprovalActionRequest
        {
            public string? ReviewerName { get; set; }
            public string? Comments { get; set; }
        }

        /// <summary>
        /// Approves a post in NeedsApproval status.
        /// If the post has a future ScheduledAt date, it changes to Scheduled.
        /// If it has no schedule or is scheduled for now/past, it publishes immediately.
        /// </summary>
        [HttpPost("{id}/approve")]
        public async Task<IActionResult> ApprovePost(
            int id, 
            [FromBody] ApprovalActionRequest? req, 
            CancellationToken cancellationToken)
        {
            var post = await _context.SocialPosts.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
            if (post == null)
                return NotFound(new { error = $"Post {id} not found." });

            if (post.Status != "NeedsApproval")
                return BadRequest(new { error = $"Only posts in 'NeedsApproval' status can be approved. Current status is {post.Status}." });

            _context.SocialPostApprovals.Add(new SocialPostApproval
            {
                PostId = post.Id,
                Action = "Approved",
                ReviewerName = req?.ReviewerName ?? "Client Reviewer",
                Comments = req?.Comments ?? "Approved for release."
            });

            // If scheduled in the future, set to Scheduled so scheduler picks it up
            if (post.ScheduledAt.HasValue && post.ScheduledAt.Value > System.DateTime.UtcNow)
            {
                post.Status = "Scheduled";
                await _context.SaveChangesAsync(cancellationToken);
                return Ok(new { success = true, postId = id, status = "Scheduled", message = "Post approved and queued for scheduled publishing." });
            }

            // Otherwise, publish immediately to platforms
            post.Status = "Approved";
            await _context.SaveChangesAsync(cancellationToken);

            var platforms = (post.ScheduledPlatforms ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            if (platforms.Count > 0)
            {
                var pubResult = await _publishingService.PublishExistingPostAsync(post.Id, platforms, cancellationToken);
                return Ok(new { success = true, postId = id, status = post.Status, result = pubResult });
            }

            return Ok(new { success = true, postId = id, status = "Approved" });
        }

        /// <summary>
        /// Rejects a post in NeedsApproval status.
        /// </summary>
        [HttpPost("{id}/reject")]
        public async Task<IActionResult> RejectPost(
            int id, 
            [FromBody] ApprovalActionRequest? req, 
            CancellationToken cancellationToken)
        {
            var post = await _context.SocialPosts.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
            if (post == null)
                return NotFound(new { error = $"Post {id} not found." });

            if (post.Status != "NeedsApproval")
                return BadRequest(new { error = $"Only posts in 'NeedsApproval' status can be rejected. Current status is {post.Status}." });

            post.Status = "Rejected";

            _context.SocialPostApprovals.Add(new SocialPostApproval
            {
                PostId = post.Id,
                Action = "Rejected",
                ReviewerName = req?.ReviewerName ?? "Client Reviewer",
                Comments = req?.Comments ?? "Rejected by reviewer."
            });

            await _context.SaveChangesAsync(cancellationToken);
            return Ok(new { success = true, postId = id, status = "Rejected", comments = req?.Comments });
        }
    }
}
