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
    public class XPublisher : ISocialPublisher
    {
        public string PlatformName => "x";

        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<XPublisher> _logger;

        public XPublisher(
            HttpClient httpClient,
            IConfiguration configuration,
            IWebHostEnvironment environment,
            ILogger<XPublisher> logger)
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
                    result.Error = "X (Twitter) access token is missing. Please connect your X account.";
                    return result;
                }

                if (string.IsNullOrWhiteSpace(post.Content))
                {
                    result.Status = "Failed";
                    result.Error = "Tweet content cannot be empty.";
                    return result;
                }

                var text = post.Content;
                // Standard tweet length check (280 characters limit for basic/free accounts)
                // Note: URLs take 23 chars on X, but we validate basic length here
                if (text.Length > 280)
                {
                    _logger.LogWarning("Tweet content exceeds 280 characters ({Length}). Truncating or posting as supported.", text.Length);
                }

                _logger.LogInformation("Publishing Tweet for user {User}. Content preview: {Preview}", 
                    account.AccountName ?? account.ExternalAccountId, 
                    text.Length > 50 ? text.Substring(0, 47) + "..." : text);

                // Build request payload for Twitter v2 tweets endpoint: POST https://api.twitter.com/2/tweets
                var tweetPayload = new Dictionary<string, object>
                {
                    ["text"] = text
                };

                var jsonPayload = JsonSerializer.Serialize(tweetPayload);
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.twitter.com/2/tweets")
                {
                    Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var response = await _httpClient.SendAsync(request, cancellationToken);
                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("X Tweet API call failed ({Status}): {Body}", (int)response.StatusCode, responseContent);
                    result.Status = "Failed";
                    try
                    {
                        using var errDoc = JsonDocument.Parse(responseContent);
                        if (errDoc.RootElement.TryGetProperty("detail", out var detail))
                            result.Error = detail.GetString();
                        else if (errDoc.RootElement.TryGetProperty("title", out var title))
                            result.Error = title.GetString();
                        else
                            result.Error = $"X API error: {response.StatusCode} - {responseContent}";
                    }
                    catch
                    {
                        result.Error = $"X API returned {(int)response.StatusCode}: {responseContent}";
                    }
                    return result;
                }

                using var doc = JsonDocument.Parse(responseContent);
                var tweetId = doc.RootElement.GetProperty("data").GetProperty("id").GetString();
                var tweetText = doc.RootElement.GetProperty("data").TryGetProperty("text", out var tProp) ? tProp.GetString() : text;

                result.Status = "Published";
                result.ExternalPostId = tweetId;
                // Format post link if account username is known, or standard intent/status URL
                var username = !string.IsNullOrWhiteSpace(account.AccountName) ? account.AccountName.TrimStart('@') : "i";
                result.PostUrl = $"https://twitter.com/{username}/status/{tweetId}";

                _logger.LogInformation("Tweet published successfully! ID: {TweetId}, URL: {Url}", tweetId, result.PostUrl);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error publishing tweet to X");
                result.Status = "Failed";
                result.Error = ex.Message;
                return result;
            }
        }
    }
}
