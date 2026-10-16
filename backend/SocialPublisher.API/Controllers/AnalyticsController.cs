using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using SocialPublisher.API.Models;
using SocialPublisher.API.Services;

namespace SocialPublisher.API.Controllers
{
    [ApiController]
    [Route("api/social/analytics")]
    public class AnalyticsController : ControllerBase
    {
        private readonly ISocialAnalyticsService _analyticsService;

        public AnalyticsController(ISocialAnalyticsService analyticsService)
        {
            _analyticsService = analyticsService;
        }

        /// <summary>
        /// Fetches real-time social metrics (likes, comments, reach, impressions) for a published post.
        /// </summary>
        [HttpGet("posts/{postId}")]
        public async Task<ActionResult<PostAnalyticsDto>> GetPostAnalytics(int postId, CancellationToken cancellationToken)
        {
            try
            {
                var result = await _analyticsService.GetPostAnalyticsAsync(postId, cancellationToken);
                return Ok(result);
            }
            catch (System.ArgumentException ex)
            {
                return NotFound(new { error = ex.Message });
            }
        }
    }
}
