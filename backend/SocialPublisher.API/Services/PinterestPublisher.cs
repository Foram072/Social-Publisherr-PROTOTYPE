
using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SocialPublisher.API.Models;

namespace SocialPublisher.API.Services
{
    public class PinterestPublisher : ISocialPublisher
    {
        public string PlatformName => "pinterest";

        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<PinterestPublisher> _logger;

        public PinterestPublisher(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<PinterestPublisher> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
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
                // 1. Validate access token.
                var token = account.AccessToken;

                if (string.IsNullOrWhiteSpace(token))
                {
                    return Fail(
                        result,
                        "Pinterest access token is missing. Please reconnect Pinterest.");
                }

                // 2. Resolve a real board ID from Pinterest.
                var boardId = await ResolveBoardIdAsync(
                    token,
                    cancellationToken);

                if (string.IsNullOrWhiteSpace(boardId))
                {
                    return Fail(
                        result,
                        "No accessible Pinterest board was found. " +
                        "Create a board in the connected Pinterest account, " +
                        "then try again. If a board already exists, verify " +
                        "the OAuth token and boards:read permission.");
                }

                // 3. Resolve the image URL.
                var publicBaseUrl =
                    _configuration["SocialMedia:PublicBaseUrl"]?.TrimEnd('/');

                var imageMedia = post.Media?.FirstOrDefault(
                    media => string.Equals(
                        media.MediaType,
                        "Image",
                        StringComparison.OrdinalIgnoreCase));

                string? imageUrl = null;

                if (!string.IsNullOrWhiteSpace(imageMedia?.FileUrl))
                {
                    var fileUrl = imageMedia.FileUrl.Trim();

                    if (Uri.TryCreate(
                            fileUrl,
                            UriKind.Absolute,
                            out var absoluteUri)
                        && (absoluteUri.Scheme == Uri.UriSchemeHttp
                            || absoluteUri.Scheme == Uri.UriSchemeHttps))
                    {
                        imageUrl = absoluteUri.AbsoluteUri;
                    }
                    else if (!string.IsNullOrWhiteSpace(publicBaseUrl))
                    {
                        imageUrl = publicBaseUrl + "/" +
                            fileUrl.TrimStart('/');
                    }
                }

                if (string.IsNullOrWhiteSpace(imageUrl))
                {
                    return Fail(
                        result,
                        "Pinterest requires an image with a publicly accessible " +
                        "HTTP or HTTPS URL. Upload an image and configure " +
                        "SocialMedia:PublicBaseUrl if your file URL is relative.");
                }

                // Pinterest's image_url source requires Pinterest to be able
                // to access the image URL. Localhost URLs will not work.
                if (!Uri.TryCreate(
                        imageUrl,
                        UriKind.Absolute,
                        out var validatedImageUri)
                    || (validatedImageUri.Scheme != Uri.UriSchemeHttp
                        && validatedImageUri.Scheme != Uri.UriSchemeHttps))
                {
                    return Fail(
                        result,
                        "The image URL is invalid. Provide a valid public HTTP/HTTPS URL.");
                }

                // 4. Prepare the Pin payload.
                var content = post.Content ?? string.Empty;

                var title = !string.IsNullOrWhiteSpace(post.Title)
                    ? post.Title
                    : content.Length > 100
                        ? content.Substring(0, 97) + "..."
                        : content;

                var pinPayload = new
                {
                    board_id = boardId,
                    title = title,
                    description = content,
                    media_source = new
                    {
                        source_type = "image_url",
                        url = imageUrl
                    }
                };

                // 5. Publish the Pin.
                using var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    "https://api.pinterest.com/v5/pins");

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
                        out var idProperty))
                {
                    pinId = idProperty.GetString();
                }

                if (string.IsNullOrWhiteSpace(pinId))
                {
                    _logger.LogWarning(
                        "Pinterest returned success without a Pin ID. Response: {Body}",
                        responseBody);

                    return Fail(
                        result,
                        "Pinterest returned a successful response, but no Pin ID was found.");
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
        /// Retrieves a valid board ID from the connected Pinterest account.
        /// Does not assume ExternalAccountId is a board ID.
        /// Supports Pinterest board-list pagination.
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
                var url = "https://api.pinterest.com/v5/boards?page_size=100";

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
                        $"{(int)response.StatusCode}). Check the access token " +
                        "and boards:read permission. Response: " +
                        responseBody);
                }

                using var document = JsonDocument.Parse(responseBody);
                var root = document.RootElement;

                if (!root.TryGetProperty(
                        "items",
                        out var items)
                    || items.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidOperationException(
                        "Pinterest returned an unexpected board-list response.");
                }

                foreach (var board in items.EnumerateArray())
                {
                    if (!board.TryGetProperty(
                            "id",
                            out var idProperty))
                    {
                        continue;
                    }

                    var boardId = idProperty.GetString();

                    if (string.IsNullOrWhiteSpace(boardId))
                    {
                        continue;
                    }

                    // If a specific board was configured, only use that board.
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

                    // Otherwise, use the first board returned by Pinterest.
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
                    "the connected account's accessible boards. Verify the ID.");
            }

            _logger.LogWarning(
                "Pinterest returned no accessible boards for the connected account.");

            return null;
        }

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