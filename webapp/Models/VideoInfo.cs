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

        [JsonPropertyName("creator")]
        public string? Creator { get; set; }

        [JsonPropertyName("tags")]
        public string[]? Tags { get; set; }

        [JsonPropertyName("submitter")]
        public string? Submitter { get; set; }

        [JsonPropertyName("submissionDate")]
        public string? SubmissionDate { get; set; }

        // Optional ratings summary provided by the Catalog API list endpoint
        [JsonPropertyName("Red")]
        public int Red { get; set; }
        [JsonPropertyName("Yellow")]
        public int Yellow { get; set; }
        [JsonPropertyName("Green")]
        public int Green { get; set; }
    }
}
