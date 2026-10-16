using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using SocialPublisher.API.Models;
using SocialPublisher.API.Services;

namespace SocialPublisher.API.Controllers
{
    [ApiController]
    [Route("api/social/engagement")]
    public class EngagementController : ControllerBase
    {
        private readonly ISocialEngagementService _engagementService;

        public EngagementController(ISocialEngagementService engagementService)
        {
            _engagementService = engagementService;
        }

        /// <summary>
        /// Fetches comments across platforms for a published post.
        /// </summary>
        [HttpGet("posts/{postId}/comments")]
        public async Task<ActionResult<List<PostCommentDto>>> GetPostComments(int postId, CancellationToken cancellationToken)
        {
            try
            {
                var comments = await _engagementService.GetPostCommentsAsync(postId, cancellationToken);
                return Ok(comments);
            }
            catch (System.ArgumentException ex)
            {
                return NotFound(new { error = ex.Message });
            }
        }

        /// <summary>
        /// Posts a reply to a comment on a given platform.
        /// </summary>
        [HttpPost("comments/{commentId}/reply")]
        public async Task<ActionResult<ReplyCommentResponse>> ReplyComment(
            string commentId,
            [FromBody] ReplyCommentRequest request,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.Message))
                return BadRequest(new { error = "Reply message cannot be empty." });

            var result = await _engagementService.ReplyToCommentAsync(commentId, request, cancellationToken);
            if (!result.Success)
                return StatusCode(500, result);

            return Ok(result);
        }
    }
}
