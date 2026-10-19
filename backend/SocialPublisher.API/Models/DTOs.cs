using System;
using System.Collections.Generic;

namespace SocialPublisher.API.Models
{
    public class PublishRequest
    {
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public List<int> MediaIds { get; set; } = new List<int>();
        public List<string> Platforms { get; set; } = new List<string>();
        public DateTime? ScheduledAt { get; set; }
        public int? ClientId { get; set; }
        public bool SubmitForApproval { get; set; } = false;
    }

    public class PublishResponse
    {
        public string PostId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public List<PublishResultDto> Results { get; set; } = new List<PublishResultDto>();
    }

    public class PublishResultDto
    {
        public string Platform { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? ExternalPostId { get; set; }
        public string? PostUrl { get; set; }
        public string? Error { get; set; }
    }

    public class SocialAccountDto
    {
        public int Id { get; set; }
        public string Platform { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public bool IsConnected { get; set; }
    }
}
