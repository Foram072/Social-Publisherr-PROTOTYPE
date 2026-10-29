using System.Text.Json.Serialization;

namespace SocialPublisher.API.Models
{
    /// <summary>
    /// DTO representing a Facebook Ad Account returned by the Graph API.
    /// </summary>
    public class MetaAdAccountDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }
}
