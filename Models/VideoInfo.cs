using System.Text.Json.Serialization;

namespace bwkt_webapp.Models
{
    public class VideoInfo
    {
        [JsonPropertyName("v")]
        public string VideoId { get; set; } = null!;

        [JsonPropertyName("title")]
        public string Title { get; set; } = null!;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("subtitleUrl")]
        public string SubtitleUrl { get; set; } = null!;

        [JsonPropertyName("tags")]
        public string[]? Tags { get; set; }
    }
}