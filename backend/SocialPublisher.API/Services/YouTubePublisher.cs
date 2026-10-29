using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SocialPublisher.API.Models;

namespace SocialPublisher.API.Services
{
    public class YouTubePublisher : ISocialPublisher
    {
        public string PlatformName => "youtube";

        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<YouTubePublisher> _logger;

        public YouTubePublisher(
            HttpClient httpClient,
            IConfiguration configuration,
            IWebHostEnvironment environment,
            ILogger<YouTubePublisher> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _environment = environment;
            _logger = logger;
        }

        public async Task<PublishResultDto> PublishAsync(SocialPost post, SocialAccount account, CancellationToken cancellationToken)
        {
            var result = new PublishResultDto { Platform = PlatformName };

            try
            {
                var token = account.AccessToken;
                if (string.IsNullOrWhiteSpace(token))
                {
                    result.Status = "Failed";
                    result.Error = "YouTube access token is missing. Please connect or reconnect your YouTube channel.";
                    return result;
                }

                // YouTube requires a video file
                var videoMedia = post.Media?.FirstOrDefault(m => m.MediaType == "Video")
                              ?? post.Media?.FirstOrDefault(m => m.FileUrl.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                                                                 m.FileUrl.EndsWith(".mov", StringComparison.OrdinalIgnoreCase) ||
                                                                 m.FileUrl.EndsWith(".avi", StringComparison.OrdinalIgnoreCase) ||
                                                                 m.FileUrl.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase));

                if (videoMedia == null)
                {
                    result.Status = "Failed";
                    result.Error = "YouTube publishing requires a video file (MP4, MOV, etc.). Please attach a video to your post.";
                    return result;
                }

                var physicalPath = UploadPaths.GetFilePath(_environment.ContentRootPath, videoMedia.FileUrl);

                if (!File.Exists(physicalPath))
                {
                    result.Status = "Failed";
                    result.Error = $"Video file not found at path: {physicalPath}";
                    return result;
                }

                var title = !string.IsNullOrWhiteSpace(post.Title)
                    ? post.Title
                    : (post.Content.Length > 60 ? post.Content.Substring(0, 57) + "..." : post.Content);

                if (string.IsNullOrWhiteSpace(title))
                    title = "New Video via Wiseliy Social";

                var description = post.Content ?? string.Empty;

                _logger.LogInformation("Starting YouTube resumable video upload for Channel {ChannelId}. Title: {Title}", account.ExternalAccountId, title);

                // ── 1. Initiate Resumable Upload Session ────────────────────────
                // POST https://www.googleapis.com/upload/youtube/v3/videos?uploadType=resumable&part=snippet,status
                var metadata = new
                {
                    snippet = new
                    {
                        title = title,
                        description = description,
                        categoryId = "22" // People & Blogs default
                    },
                    status = new
                    {
                        privacyStatus = "public", // Public by default
                        selfDeclaredMadeForKids = false
                    }
                };

                var metaJson = JsonSerializer.Serialize(metadata);
                var initRequest = new HttpRequestMessage(HttpMethod.Post, "https://www.googleapis.com/upload/youtube/v3/videos?uploadType=resumable&part=snippet,status")
                {
                    Content = new StringContent(metaJson, Encoding.UTF8, "application/json")
                };

                initRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var fileInfo = new FileInfo(physicalPath);
                initRequest.Headers.Add("X-Upload-Content-Length", fileInfo.Length.ToString());
                initRequest.Headers.Add("X-Upload-Content-Type", "video/*");

                var initResponse = await _httpClient.SendAsync(initRequest, cancellationToken);
                var initContent = await initResponse.Content.ReadAsStringAsync(cancellationToken);

                if (!initResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to initiate YouTube upload: {Code} {Body}", (int)initResponse.StatusCode, initContent);
                    result.Status = "Failed";
                    result.Error = $"YouTube API rejected upload initiation ({(int)initResponse.StatusCode}): {ExtractErrorMessage(initContent)}";
                    return result;
                }

                if (!initResponse.Headers.TryGetValues("Location", out var locationValues))
                {
                    result.Status = "Failed";
                    result.Error = "YouTube did not return a resumable upload Location URL.";
                    return result;
                }

                var uploadUrl = locationValues.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(uploadUrl))
                {
                    result.Status = "Failed";
                    result.Error = "YouTube returned an empty upload URL.";
                    return result;
                }

                // ── 2. Stream Upload Video Content ─────────────────────────────
                using var fileStream = File.OpenRead(physicalPath);
                using var uploadRequest = new HttpRequestMessage(HttpMethod.Put, uploadUrl)
                {
                    Content = new StreamContent(fileStream)
                };
                uploadRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("video/*");
                uploadRequest.Content.Headers.ContentLength = fileInfo.Length;

                var uploadResponse = await _httpClient.SendAsync(uploadRequest, cancellationToken);
                var uploadContent = await uploadResponse.Content.ReadAsStringAsync(cancellationToken);

                if (!uploadResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to upload video content to YouTube: {Code} {Body}", (int)uploadResponse.StatusCode, uploadContent);
                    result.Status = "Failed";
                    result.Error = $"YouTube upload failed ({(int)uploadResponse.StatusCode}): {ExtractErrorMessage(uploadContent)}";
                    return result;
                }

                // Parse YouTube video ID
                using var respDoc = JsonDocument.Parse(uploadContent);
                var videoId = respDoc.RootElement.GetProperty("id").GetString();

                result.Status = "Published";
                result.ExternalPostId = videoId;
                result.PostUrl = $"https://www.youtube.com/watch?v={videoId}";
                _logger.LogInformation("YouTube video uploaded successfully! Video ID: {VideoId}", videoId);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error publishing to YouTube");
                result.Status = "Failed";
                result.Error = $"YouTube publishing error: {ex.Message}";
                return result;
            }
        }

        private static string ExtractErrorMessage(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("error", out var err))
                {
                    if (err.TryGetProperty("message", out var msg))
                        return msg.GetString() ?? json;
                }
            }
            catch { }
            return json;
        }
    }
}
