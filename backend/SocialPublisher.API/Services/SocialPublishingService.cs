using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SocialPublisher.API.Data;
using SocialPublisher.API.Models;

namespace SocialPublisher.API.Services
{
    public class SocialPublishingService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly IEnumerable<ISocialPublisher> _publishers;

        public SocialPublishingService(
            ApplicationDbContext dbContext,
            IEnumerable<ISocialPublisher> publishers)
        {
            _dbContext = dbContext;
            _publishers = publishers;
        }

        /// <summary>
        /// Publishes a post to all selected platforms independently.
        /// Failure on one platform does not stop others.
        /// </summary>
        public async Task<PublishResponse> PublishPostAsync(PublishRequest request, CancellationToken cancellationToken)
        {
            // --- Step 1: Validate ---
            if (string.IsNullOrWhiteSpace(request.Content))
                throw new ArgumentException("Post content is required.");

            if (request.Platforms == null || request.Platforms.Count == 0)
                throw new ArgumentException("At least one platform must be selected.");

            var response = new PublishResponse();

            // --- Step 2: Create Post record in DB ---
            var post = new SocialPost
            {
                Title = request.Title ?? string.Empty,
                Content = request.Content,
                Status = "Processing",
                CreatedAt = DateTime.UtcNow,
                ClientId = request.ClientId
            };

            // CHANGED (1/4): link images uploaded earlier via POST /api/social/media (needed for Instagram)
            var mediaIds = (request.MediaIds ?? new List<int>()).Distinct().ToList();
            if (mediaIds.Count > 0)
            {
                var mediaItems = await _dbContext.SocialPostMedia
                    .Where(m => mediaIds.Contains(m.Id) && m.PostId == null)
                    .ToListAsync(cancellationToken);

                foreach (var m in mediaItems)
                    post.Media.Add(m);
            }

            _dbContext.SocialPosts.Add(post);
            await _dbContext.SaveChangesAsync(cancellationToken);
            response.PostId = post.Id.ToString();

            // --- Step 3: Process each platform independently ---
            foreach (var platform in request.Platforms)
            {
                var platformLower = platform.ToLowerInvariant();

                // Look for brand-specific connected account first; fall back to global/base account if not found
                SocialAccount? account = null;
                if (request.ClientId.HasValue && request.ClientId.Value > 0)
                {
                    account = await _dbContext.SocialAccounts
                        .FirstOrDefaultAsync(a => a.ClientId == request.ClientId.Value && a.Platform == platformLower && a.IsConnected, cancellationToken);
                }

                if (account == null)
                {
                    account = await _dbContext.SocialAccounts
                        .FirstOrDefaultAsync(a => a.Platform == platformLower, cancellationToken);
                }

                if (account == null || !account.IsConnected)
                {
                    var failResult = new PublishResultDto
                    {
                        Platform = platformLower,
                        Status = "Failed",
                        Error = account == null
                            ? $"No account configured for {platformLower}"
                            : $"{platformLower} account is not connected. Please set the access token in appsettings.json."
                    };
                    response.Results.Add(failResult);

                    _dbContext.SocialPostPublishes.Add(new SocialPostPublish
                    {
                        PostId = post.Id,
                        SocialAccountId = account?.Id ?? 0,
                        Status = "Failed",
                        ErrorMessage = failResult.Error
                    });
                    continue;
                }

                // Get the right publisher for this platform
                var publisher = _publishers.FirstOrDefault(p => p.PlatformName == platformLower);
                if (publisher == null)
                {
                    response.Results.Add(new PublishResultDto
                    {
                        Platform = platformLower,
                        Status = "Failed",
                        Error = $"Publisher for {platformLower} is not implemented yet."
                    });
                    continue;
                }

                // --- Step 4: Call the platform API ---
                var result = await publisher.PublishAsync(post, account, cancellationToken);
                MarkIfAuthFailed(account, result);
                response.Results.Add(result);

                // --- Step 5: Save individual result to DB ---
                _dbContext.SocialPostPublishes.Add(new SocialPostPublish
                {
                    PostId = post.Id,
                    SocialAccountId = account.Id,
                    Status = result.Status,
                    ExternalPostId = result.ExternalPostId,
                    PostUrl = result.PostUrl,
                    ErrorMessage = result.Error,
                    PublishedAt = result.Status == "Published" ? DateTime.UtcNow : null
                });
            }

            // --- Step 6: Update overall post status ---
            // CHANGED (2/4): an empty result list must never count as "Published"
            if (response.Results.Count > 0 && response.Results.All(r => r.Status == "Published"))
                post.Status = "Published";
            else if (response.Results.Any(r => r.Status == "Published"))
                post.Status = "PartiallyPublished";
            else
                post.Status = "Failed";

            await _dbContext.SaveChangesAsync(cancellationToken);
            response.Status = post.Status;

            return response;
        }

        /// <summary>
        /// Publishes a pre-saved post (such as a Scheduled post that has become due)
        /// using its stored content, media, and platform list.
        /// </summary>
        public async Task<PublishResponse> PublishExistingPostAsync(int postId, List<string> platforms, CancellationToken cancellationToken)
        {
            var post = await _dbContext.SocialPosts
                .Include(p => p.Media)
                .Include(p => p.PublishResults)
                .FirstOrDefaultAsync(p => p.Id == postId, cancellationToken);

            if (post == null)
                throw new ArgumentException($"Post with ID {postId} not found.");

            post.Status = "Processing";
            await _dbContext.SaveChangesAsync(cancellationToken);

            var response = new PublishResponse { PostId = post.Id.ToString() };

            foreach (var platform in platforms)
            {
                var platformLower = platform.ToLowerInvariant().Trim();

                // Look for brand-specific connected account first; fall back to global/base account
                SocialAccount? account = null;
                if (post.ClientId.HasValue && post.ClientId.Value > 0)
                {
                    account = await _dbContext.SocialAccounts
                        .FirstOrDefaultAsync(a => a.ClientId == post.ClientId.Value && a.Platform == platformLower && a.IsConnected, cancellationToken);
                }

                if (account == null)
                {
                    account = await _dbContext.SocialAccounts
                        .FirstOrDefaultAsync(a => a.Platform == platformLower, cancellationToken);
                }

                if (account == null || !account.IsConnected)
                {
                    var failResult = new PublishResultDto
                    {
                        Platform = platformLower,
                        Status = "Failed",
                        Error = account == null
                            ? $"No account configured for {platformLower}"
                            : $"{platformLower} account is not connected."
                    };
                    response.Results.Add(failResult);

                    _dbContext.SocialPostPublishes.Add(new SocialPostPublish
                    {
                        PostId = post.Id,
                        SocialAccountId = account?.Id ?? 0,
                        Status = "Failed",
                        ErrorMessage = failResult.Error
                    });
                    continue;
                }

                var publisher = _publishers.FirstOrDefault(p => p.PlatformName == platformLower);
                if (publisher == null)
                {
                    response.Results.Add(new PublishResultDto
                    {
                        Platform = platformLower,
                        Status = "Failed",
                        Error = $"Publisher for {platformLower} is not implemented yet."
                    });
                    continue;
                }

                var result = await publisher.PublishAsync(post, account, cancellationToken);
                MarkIfAuthFailed(account, result);
                response.Results.Add(result);

                _dbContext.SocialPostPublishes.Add(new SocialPostPublish
                {
                    PostId = post.Id,
                    SocialAccountId = account.Id,
                    Status = result.Status,
                    ExternalPostId = result.ExternalPostId,
                    PostUrl = result.PostUrl,
                    ErrorMessage = result.Error,
                    PublishedAt = result.Status == "Published" ? DateTime.UtcNow : null
                });
            }

            if (response.Results.Count > 0 && response.Results.All(r => r.Status == "Published"))
                post.Status = "Published";
            else if (response.Results.Any(r => r.Status == "Published"))
                post.Status = "PartiallyPublished";
            else
                post.Status = "Failed";

            await _dbContext.SaveChangesAsync(cancellationToken);
            response.Status = post.Status;

            return response;
        }

        /// <summary>
        /// Retries only the failed platforms for an existing post.
        /// Skips platforms that are already published successfully.
        /// </summary>
        public async Task<PublishResponse> RetryFailedAsync(int postId, CancellationToken cancellationToken)
        {
            var post = await _dbContext.SocialPosts
                .Include(p => p.Media)   // CHANGED (3/4): Instagram retry needs the image
                .Include(p => p.PublishResults)
                    .ThenInclude(r => r.SocialAccount)
                .FirstOrDefaultAsync(p => p.Id == postId, cancellationToken);

            if (post == null)
                throw new ArgumentException($"Post with ID {postId} not found.");

            var response = new PublishResponse { PostId = postId.ToString() };

            foreach (var publishRecord in post.PublishResults)
            {
                // Skip already published platforms
                if (publishRecord.Status == "Published")
                {
                    response.Results.Add(new PublishResultDto
                    {
                        Platform = publishRecord.SocialAccount.Platform,
                        Status = "Published",
                        ExternalPostId = publishRecord.ExternalPostId,
                        PostUrl = publishRecord.PostUrl
                    });
                    continue;
                }

                var platform = publishRecord.SocialAccount.Platform;
                var publisher = _publishers.FirstOrDefault(p => p.PlatformName == platform);

                if (publisher == null || !publishRecord.SocialAccount.IsConnected)
                {
                    response.Results.Add(new PublishResultDto
                    {
                        Platform = platform,
                        Status = "Failed",
                        Error = "Publisher not available or account disconnected."
                    });
                    continue;
                }

                var result = await publisher.PublishAsync(post, publishRecord.SocialAccount, cancellationToken);
                MarkIfAuthFailed(publishRecord.SocialAccount, result);
                response.Results.Add(result);

                // Update existing record
                publishRecord.Status = result.Status;
                publishRecord.ExternalPostId = result.ExternalPostId;
                publishRecord.PostUrl = result.PostUrl;
                publishRecord.ErrorMessage = result.Error;
                publishRecord.PublishedAt = result.Status == "Published" ? DateTime.UtcNow : null;
            }

            // Update overall status
            // CHANGED (4/4): same empty-list guard as above
            if (response.Results.Count > 0 && response.Results.All(r => r.Status == "Published"))
                post.Status = "Published";
            else if (response.Results.Any(r => r.Status == "Published"))
                post.Status = "PartiallyPublished";
            else
                post.Status = "Failed";

            await _dbContext.SaveChangesAsync(cancellationToken);
            response.Status = post.Status;

            return response;
        }

        /// <summary>
        /// If the platform says the login is no longer valid (expired / revoked token),
        /// mark the account as expired so the page shows "Needs Reconnection".
        /// Ordinary posting errors (bad image, too long, etc.) are NOT treated as login problems.
        /// The change is saved by the SaveChanges call that already follows.
        /// </summary>
        private static void MarkIfAuthFailed(SocialAccount account, PublishResultDto result)
        {
            if (result.Status == "Published" || string.IsNullOrWhiteSpace(result.Error))
                return;

            var error = result.Error;

            var isAuthFailure =
                error.Contains("token has expired or is not valid", StringComparison.OrdinalIgnoreCase) // LinkedIn 401
                || error.Contains("API error 190:", StringComparison.OrdinalIgnoreCase)                // Instagram invalid token
                || Regex.IsMatch(error, "\"code\"\\s*:\\s*190\\b");                                  // Facebook invalid token

            if (isAuthFailure)
                account.TokenExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        }
    }
}