using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SocialPublisher.API.Data;

namespace SocialPublisher.API.Services
{
    /// <summary>
    /// Background service that periodically checks for posts scheduled to be published
    /// and triggers their publishing when due.
    /// </summary>
    public class PostSchedulerBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<PostSchedulerBackgroundService> _logger;
        private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(30);

        public PostSchedulerBackgroundService(
            IServiceProvider serviceProvider,
            ILogger<PostSchedulerBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Post Scheduler Background Service started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckAndPublishDuePostsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while executing scheduled post checks.");
                }

                await Task.Delay(_checkInterval, stoppingToken);
            }

            _logger.LogInformation("Post Scheduler Background Service is stopping.");
        }

        private async Task CheckAndPublishDuePostsAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var publishingService = scope.ServiceProvider.GetRequiredService<SocialPublishingService>();

            var now = DateTime.UtcNow;

            var duePosts = await dbContext.SocialPosts
                .Where(p => p.Status == "Scheduled" && p.ScheduledAt.HasValue && p.ScheduledAt.Value <= now)
                .OrderBy(p => p.ScheduledAt)
                .Take(10)
                .ToListAsync(cancellationToken);

            if (duePosts.Count == 0)
                return;

            _logger.LogInformation("Found {Count} due scheduled post(s) to publish.", duePosts.Count);

            foreach (var post in duePosts)
            {
                var platforms = (post.ScheduledPlatforms ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();

                if (platforms.Count == 0)
                {
                    post.Status = "Failed";
                    await dbContext.SaveChangesAsync(cancellationToken);
                    continue;
                }

                // Immediate lock: prevent duplicate execution if interval triggers while publishing
                post.Status = "Processing";
                await dbContext.SaveChangesAsync(cancellationToken);

                try
                {
                    _logger.LogInformation("Publishing scheduled post ID: {PostId} for platforms [{Platforms}]...", post.Id, string.Join(",", platforms));
                    await publishingService.PublishExistingPostAsync(post.Id, platforms, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to publish scheduled post ID {PostId}", post.Id);
                    post.Status = "Failed";
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
            }
        }
    }
}
