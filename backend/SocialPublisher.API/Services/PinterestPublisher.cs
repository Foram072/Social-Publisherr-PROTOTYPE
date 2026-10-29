using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SocialPublisher.API.Models;

namespace SocialPublisher.API.Services
{
    /// <summary>
    /// Publishes to Pinterest.
    ///  - Video attached  -> video pin (an attached image is used as the cover, otherwise a video frame)
    ///  - 2+ images       -> carousel pin (max 5 images)
    ///  - 1 image         -> normal image pin
    /// </summary>
    public class PinterestPublisher : ISocialPublisher
    {
        public string PlatformName => "pinterest";

        private static readonly string[] VideoExtensions = { ".mp4", ".mov", ".m4v" };
        private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };

        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment; // NEW: needed to read the video file from disk
        private readonly ILogger<PinterestPublisher> _logger;

        public PinterestPublisher(
            HttpClient httpClient,
            IConfiguration configuration,
            IWebHostEnvironment environment,
            ILogger<PinterestPublisher> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _environment = environment;
            _logger = logger;
        }

        // Select the environment consistently for all API requests.
        private bool UseSandbox =>
            bool.TryParse(
                _configuration["SocialMedia:Pinterest:UseSandbox"],
                out var enabled) && enabled;

        private string ApiBaseUrl =>
            UseSandbox
                ? "https://api-sandbox.pinterest.com/v5/"
                : "https://api.pinterest.com/v5/";

        // Never use a Production account token for Sandbox requests.
        private string? GetAccessToken(SocialAccount account)
        {
            if (UseSandbox)
            {
                return _configuration[
                    "SocialMedia:Pinterest:SandboxAccessToken"]?.Trim();
            }

            var token = !string.IsNullOrWhiteSpace(account.AccessToken)
                ? account.AccessToken
                : account.Connection?.AccessToken;

            return !string.IsNullOrWhiteSpace(token)
                ? token.Trim()
                : _configuration[
                    "SocialMedia:Pinterest:AccessToken"]?.Trim();
        }

        public async Task<PublishResultDto> PublishAsync(
            SocialPost post,
            SocialAccount account,
            CancellationToken cancellationToken)
        {
            var result = new PublishResultDto
            {
                Platform = PlatformName
            };

            try
            {
                // 1. Get the token for the selected environment.
                var token = GetAccessToken(account);

                if (string.IsNullOrWhiteSpace(token))
                {
                    return Fail(
                        result,
                        UseSandbox
                            ? "Pinterest Sandbox token is missing. Configure " +
                              "SocialMedia:Pinterest:SandboxAccessToken."
                            : "Pinterest access token is missing. " +
                              "Please reconnect Pinterest.");
                }

                _logger.LogInformation(
                    "Pinterest publishing environment: {Environment}",
                    UseSandbox ? "Sandbox" : "Production");

                // 2. Resolve an accessible board.
                var boardId = await ResolveBoardIdAsync(
                    token,
                    cancellationToken);

                if (string.IsNullOrWhiteSpace(boardId))
                {
                    return Fail(  
                        result,
                        "No accessible Pinterest board was found. " +
                        "Create a board in the selected Pinterest environment " +
                        "and verify that the token has boards:read permission.");
                }

                // 3. Sort the attached files into videos and images (upload order is kept).
                var videoFiles = post.Media?
                    .Where(m => IsVideo(m.MediaType, m.FileUrl))
                    .Select(m => m.FileUrl)
                    .ToList() ?? new List<string>();

                var imageFiles = post.Media?
                    .Where(m => !IsVideo(m.MediaType, m.FileUrl) && IsImage(m.MediaType, m.FileUrl))
                    .Select(m => m.FileUrl)
                    .ToList() ?? new List<string>();

                if (videoFiles.Count == 0 && imageFiles.Count == 0)
                {
                    return Fail(
                        result,
                        "Pinterest needs at least one image or video. " +
                        "Please attach one to your post.");
                }

                var publicBaseUrl =
                    _configuration["SocialMedia:PublicBaseUrl"]?.TrimEnd('/');

                const string imageUrlError =
                    "Pinterest requires an image with a valid public " +
                    "HTTP or HTTPS URL. Configure SocialMedia:PublicBaseUrl " +
                    "if your image URL is relative.";

                // 4. Prepare the Pin payload.
                var content = post.Content ?? string.Empty;

                var title = !string.IsNullOrWhiteSpace(post.Title)
                    ? post.Title
                    : content.Length > 100
                        ? content.Substring(0, 97) + "..."
                        : content;

                title = Truncate(title.Replace("\r", " ").Replace("\n", " ").Trim(), 100);
                if (string.IsNullOrWhiteSpace(title)) title = "New Pin";

                var description = Truncate(content, 500); // Pinterest description limit

                object mediaSource;

                if (videoFiles.Count > 0)
                {
                    // ── VIDEO PIN ───────────────────────────────────────────
                    var physicalPath = UploadPaths.GetFilePath(
                        _environment.ContentRootPath,
                        videoFiles[0]);

                    if (!File.Exists(physicalPath))
                    {
                        return Fail(result, $"Video file not found at path: {physicalPath}");
                    }

                    // Cover image: the first attached image (must be public), else a frame of the video.
                    string? coverUrl = null;

                    if (imageFiles.Count > 0)
                    {
                        coverUrl = BuildPublicUrl(imageFiles[0], publicBaseUrl);

                        if (coverUrl == null)
                        {
                            return Fail(result, imageUrlError);
                        }
                    }

                    var mediaId = await UploadVideoAsync(
                        token,
                        physicalPath,
                        cancellationToken);

                    var videoSource = new Dictionary<string, object>
                    {
                        ["source_type"] = "video_id",
                        ["media_id"] = mediaId
                    };

                    if (coverUrl != null)
                    {
                        videoSource["cover_image_url"] = coverUrl;
                    }
                    else
                    {
                        videoSource["cover_image_key_frame_time"] = 1000;
                    }

                    mediaSource = videoSource;
                }
                else if (imageFiles.Count >= 2)
                {
                    // ── CAROUSEL PIN (2-5 images) ───────────────────────────
                    var items = new List<object>();

                    foreach (var file in imageFiles.Take(5))
                    {
                        var url = BuildPublicUrl(file, publicBaseUrl);

                        if (url == null)
                        {
                            return Fail(result, imageUrlError);
                        }

                        items.Add(new { url, title });
                    }

                    mediaSource = new Dictionary<string, object>
                    {
                        ["source_type"] = "multiple_image_urls",
                        ["items"] = items
                    };
                }
                else
                {
                    // ── SINGLE IMAGE PIN ────────────────────────────────────
                    var imageUrl = BuildPublicUrl(imageFiles[0], publicBaseUrl);

                    if (imageUrl == null)
                    {
                        return Fail(result, imageUrlError);
                    }

                    mediaSource = new Dictionary<string, object>
                    {
                        ["source_type"] = "image_url",
                        ["url"] = imageUrl
                    };
                }

                var pinPayload = new Dictionary<string, object>
                {
                    ["board_id"] = boardId,
                    ["title"] = title,
                    ["description"] = description,
                    ["media_source"] = mediaSource
                };

                // 5. Create the Pin in the same environment as board lookup.
                using var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"{ApiBaseUrl}pins");

                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);

                request.Content = JsonContent.Create(pinPayload);

                using var response = await _httpClient.SendAsync(
                    request,
                    cancellationToken);

                var responseBody = await response.Content
                    .ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError(
                        "Pinterest Pin creation failed. HTTP {StatusCode}. " +
                        "Response: {ResponseBody}",
                        (int)response.StatusCode,
                        responseBody);

                    return Fail(
                        result,
                        $"Pinterest Pin creation failed (HTTP " +
                        $"{(int)response.StatusCode}): {responseBody}");
                }

                // 6. Read the created Pin ID.
                using var document = JsonDocument.Parse(responseBody);

                string? pinId = null;

                if (document.RootElement.TryGetProperty(
                        "id",
                        out var idProperty)
                    && idProperty.ValueKind == JsonValueKind.String)
                {
                    pinId = idProperty.GetString();
                }

                if (string.IsNullOrWhiteSpace(pinId))
                {
                    _logger.LogWarning(
                        "Pinterest returned success without a Pin ID.");

                    return Fail(
                        result,
                        "Pinterest returned a successful response, " +
                        "but no Pin ID was found.");
                }

                result.Status = "Published";
                result.ExternalPostId = pinId;
                result.PostUrl =
                    $"https://www.pinterest.com/pin/{pinId}/";

                _logger.LogInformation(
                    "Successfully published Pinterest Pin {PinId} " +
                    "to board {BoardId}",
                    pinId,
                    boardId);

                return result;
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Unexpected error while publishing to Pinterest.");

                result.Status = "Failed";
                result.Error = ex.Message;

                return result;
            }
        }

        /// <summary>
        /// Uploads a video to Pinterest and waits until it is processed.
        /// Steps: register -> upload file -> poll status. Returns the media_id for the Pin.
        /// </summary>
        private async Task<string> UploadVideoAsync(
            string token,
            string physicalPath,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation("Pinterest: registering video upload for {Path}", physicalPath);

            // 1. Register the upload.
            using var registerRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"{ApiBaseUrl}media");

            registerRequest.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            registerRequest.Content = JsonContent.Create(new { media_type = "video" });

            using var registerResponse = await _httpClient.SendAsync(
                registerRequest,
                cancellationToken);

            var registerBody = await registerResponse.Content
                .ReadAsStringAsync(cancellationToken);

            if (!registerResponse.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Pinterest video registration failed. HTTP {StatusCode}. Response: {ResponseBody}",
                    (int)registerResponse.StatusCode,
                    registerBody);

                throw new InvalidOperationException(
                    $"Pinterest could not register the video upload (HTTP " +
                    $"{(int)registerResponse.StatusCode}): {registerBody}");
            }

            string mediaId;
            string uploadUrl;
            var uploadParameters = new List<KeyValuePair<string, string>>();

            using (var registerDoc = JsonDocument.Parse(registerBody))
            {
                var root = registerDoc.RootElement;

                mediaId = root.GetProperty("media_id").ToString();

                uploadUrl = root.GetProperty("upload_url").GetString()
                    ?? throw new InvalidOperationException(
                        "Pinterest did not return an upload URL for the video.");

                foreach (var parameter in root.GetProperty("upload_parameters").EnumerateObject())
                {
                    uploadParameters.Add(new KeyValuePair<string, string>(
                        parameter.Name,
                        parameter.Value.ToString()));
                }
            }

            // 2. Upload the file to the address Pinterest returned (no Authorization header here).
            using (var form = new MultipartFormDataContent())
            {
                foreach (var parameter in uploadParameters)
                {
                    form.Add(new StringContent(parameter.Value), parameter.Key);
                }

                using var fileStream = File.OpenRead(physicalPath);
                var fileContent = new StreamContent(fileStream);
                fileContent.Headers.ContentType =
                    new MediaTypeHeaderValue(GetVideoMimeType(physicalPath));

                // The "file" field must be the last field of the form.
                form.Add(fileContent, "file", Path.GetFileName(physicalPath));

                using var uploadResponse = await _httpClient.PostAsync(
                    uploadUrl,
                    form,
                    cancellationToken);

                if (!uploadResponse.IsSuccessStatusCode)
                {
                    var uploadBody = await uploadResponse.Content
                        .ReadAsStringAsync(cancellationToken);

                    _logger.LogError(
                        "Pinterest video upload failed. HTTP {StatusCode}. Response: {ResponseBody}",
                        (int)uploadResponse.StatusCode,
                        uploadBody);

                    throw new InvalidOperationException(
                        $"Pinterest video upload failed (HTTP {(int)uploadResponse.StatusCode}).");
                }
            }

            // 3. Wait until Pinterest has finished processing (about 5 minutes at most).
            for (var attempt = 0; attempt < 60; attempt++)
            {
                using var statusRequest = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"{ApiBaseUrl}media/{mediaId}");

                statusRequest.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);

                using var statusResponse = await _httpClient.SendAsync(
                    statusRequest,
                    cancellationToken);

                if (statusResponse.IsSuccessStatusCode)
                {
                    var statusBody = await statusResponse.Content
                        .ReadAsStringAsync(cancellationToken);

                    using var statusDoc = JsonDocument.Parse(statusBody);

                    var status = statusDoc.RootElement.TryGetProperty("status", out var statusProperty)
                        ? statusProperty.GetString()
                        : null;

                    if (string.Equals(status, "succeeded", StringComparison.OrdinalIgnoreCase))
                    {
                        return mediaId;
                    }

                    if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            "Pinterest could not process the video. " +
                            "Check the format (MP4 or MOV) and the length.");
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }

            throw new InvalidOperationException(
                "Pinterest is still processing the video. Please try again in a few minutes.");
        }

        /// <summary>
        /// Finds a board accessible to the selected Pinterest token.
        /// Supports board-list pagination and optional BoardId filtering.
        /// </summary>
        private async Task<string?> ResolveBoardIdAsync(
            string token,
            CancellationToken cancellationToken)
        {
            var configuredBoardId =
                _configuration["SocialMedia:Pinterest:BoardId"]?.Trim();

            string? bookmark = null;

            do
            {
                var url = $"{ApiBaseUrl}boards?page_size=100";

                if (!string.IsNullOrWhiteSpace(bookmark))
                {
                    url += "&bookmark=" +
                        Uri.EscapeDataString(bookmark);
                }

                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    url);

                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);

                request.Headers.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/json"));

                using var response = await _httpClient.SendAsync(
                    request,
                    cancellationToken);

                var responseBody = await response.Content
                    .ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError(
                        "Pinterest board lookup failed. HTTP {StatusCode}. " +
                        "Response: {ResponseBody}",
                        (int)response.StatusCode,
                        responseBody);

                    throw new InvalidOperationException(
                        $"Pinterest board lookup failed (HTTP " +
                        $"{(int)response.StatusCode}). Check the selected " +
                        $"environment and token. Response: {responseBody}");
                }

                using var document = JsonDocument.Parse(responseBody);
                var root = document.RootElement;

                if (!root.TryGetProperty("items", out var items)
                    || items.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidOperationException(
                        "Pinterest returned an unexpected board-list response.");
                }

                foreach (var board in items.EnumerateArray())
                {
                    if (!board.TryGetProperty("id", out var idProperty)
                        || idProperty.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var boardId = idProperty.GetString();

                    if (string.IsNullOrWhiteSpace(boardId))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(configuredBoardId))
                    {
                        if (string.Equals(
                                boardId,
                                configuredBoardId,
                                StringComparison.Ordinal))
                        {
                            return boardId;
                        }

                        continue;
                    }

                    // Use the first accessible board if none was configured.
                    return boardId;
                }

                bookmark =
                    root.TryGetProperty(
                        "bookmark",
                        out var bookmarkProperty)
                    && bookmarkProperty.ValueKind == JsonValueKind.String
                        ? bookmarkProperty.GetString()
                        : null;

            } while (!string.IsNullOrWhiteSpace(bookmark));

            if (!string.IsNullOrWhiteSpace(configuredBoardId))
            {
                throw new InvalidOperationException(
                    "The configured Pinterest BoardId was not found in " +
                    "the selected account's accessible boards. Verify the ID.");
            }

            _logger.LogWarning(
                "Pinterest returned no accessible boards in {Environment}.",
                UseSandbox ? "Sandbox" : "Production");

            return null;
        }

        // ────────────────────────────────────────────────────────────────────
        // Helpers
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Pinterest downloads images itself, so the address must be public.
        /// Relative paths are combined with SocialMedia:PublicBaseUrl. Localhost is rejected.
        /// </summary>
        private static string? BuildPublicUrl(string fileUrl, string? publicBaseUrl)
        {
            if (string.IsNullOrWhiteSpace(fileUrl))
            {
                return null;
            }

            fileUrl = fileUrl.Trim();
            Uri? uri;

            if (Uri.TryCreate(fileUrl, UriKind.Absolute, out var absoluteUri)
                && (absoluteUri.Scheme == Uri.UriSchemeHttp
                    || absoluteUri.Scheme == Uri.UriSchemeHttps))
            {
                uri = absoluteUri;
            }
            else if (!string.IsNullOrWhiteSpace(publicBaseUrl))
            {
                var combined = publicBaseUrl + "/" + fileUrl.TrimStart('/');

                if (!Uri.TryCreate(combined, UriKind.Absolute, out uri)
                    || (uri.Scheme != Uri.UriSchemeHttp
                        && uri.Scheme != Uri.UriSchemeHttps))
                {
                    return null;
                }
            }
            else
            {
                return null;
            }

            var host = uri.Host;

            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || host == "127.0.0.1"
                || host == "::1")
            {
                return null;
            }

            return uri.AbsoluteUri;
        }

        private static bool IsVideo(string? mediaType, string? fileUrl) =>
            string.Equals(mediaType, "Video", StringComparison.OrdinalIgnoreCase)
            || HasExtension(fileUrl, VideoExtensions);

        private static bool IsImage(string? mediaType, string? fileUrl) =>
            string.Equals(mediaType, "Image", StringComparison.OrdinalIgnoreCase)
            || HasExtension(fileUrl, ImageExtensions);

        private static bool HasExtension(string? path, string[] extensions) =>
            !string.IsNullOrWhiteSpace(path)
            && extensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));

        private static string GetVideoMimeType(string path) =>
            Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".mov" => "video/quicktime",
                _ => "video/mp4"
            };

        private static string Truncate(string value, int max) =>
            value.Length <= max ? value : value.Substring(0, max - 3) + "...";

        private static PublishResultDto Fail(
            PublishResultDto result,
            string error)
        {
            result.Status = "Failed";
            result.Error = error;
            return result;
        }
    }
}