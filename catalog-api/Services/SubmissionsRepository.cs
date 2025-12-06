using System.Text.Json;
using System.Text.Json.Serialization;

namespace catalog_api.Services;

public class SubmissionsRepository
{
    private readonly string _path;
    private readonly JsonSerializerOptions _opts = new() { WriteIndented = false, PropertyNameCaseInsensitive = true };
    private Root _store = new();
    private readonly object _lock = new();

    public SubmissionsRepository(IConfiguration config, IWebHostEnvironment env)
    {
        var configured = config["Data:SubmissionsPath"] ?? config["DATA_SUBMISSIONS_PATH"];
        _path = Path.GetFullPath(Path.Combine(env.ContentRootPath, configured ?? "data/submissions.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        Load();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                _store = JsonSerializer.Deserialize<Root>(json, _opts) ?? new();
            }
        }
        catch
        {
            _store = new();
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_store, _opts);
            File.WriteAllText(_path, json);
        }
        catch { }
    }

    public Submission CreateVideo(string submittedBy, VideoSubmissionPayload payload)
    {
        lock (_lock)
        {
            var s = new Submission
            {
                Id = Guid.NewGuid().ToString("N"),
                Type = "video",
                Status = "pending",
                SubmittedAt = DateTimeOffset.UtcNow,
                SubmittedBy = submittedBy,
                Payload = payload
            };
            _store.Items.Add(s);
            Save();
            return s;
        }
    }

    public Submission CreateSubtitleCorrection(string submittedBy, SubtitleCorrectionPayload payload)
    {
        lock (_lock)
        {
            var s = new Submission
            {
                Id = Guid.NewGuid().ToString("N"),
                Type = "subtitle_correction",
                Status = "pending",
                SubmittedAt = DateTimeOffset.UtcNow,
                SubmittedBy = submittedBy,
                SubtitleCorrection = payload
            };
            _store.Items.Add(s);
            Save();
            return s;
        }
    }

    public IReadOnlyList<Submission> List(string? type, string? status, int page, int pageSize)
    {
        lock (_lock)
        {
            IEnumerable<Submission> q = _store.Items;
            if (!string.IsNullOrWhiteSpace(type)) q = q.Where(x => string.Equals(x.Type, type, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => string.Equals(x.Status, status, StringComparison.OrdinalIgnoreCase));
            return q
                .OrderByDescending(x => x.SubmittedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();
        }
    }

    public Submission? Get(string id)
    {
        lock (_lock)
        {
            return _store.Items.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        }
    }

    public bool Review(string id, string reviewerId, string action, string? reason)
    {
        lock (_lock)
        {
            var s = _store.Items.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            if (s == null) return false;
            if (!string.Equals(s.Status, "pending", StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(action, "approve", StringComparison.OrdinalIgnoreCase))
            {
                s.Status = "approved";
            }
            else if (string.Equals(action, "reject", StringComparison.OrdinalIgnoreCase))
            {
                s.Status = "rejected";
            }
            else return false;
            s.ReviewedAt = DateTimeOffset.UtcNow;
            s.ReviewerId = reviewerId;
            s.Reason = reason;
            Save();
            return true;
        }
    }

    private class Root
    {
        public List<Submission> Items { get; set; } = new();
    }
}

public class Submission
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
    [JsonPropertyName("status")] public string Status { get; set; } = "pending";
    [JsonPropertyName("submitted_at")] public DateTimeOffset SubmittedAt { get; set; }
    [JsonPropertyName("submitted_by")] public string SubmittedBy { get; set; } = string.Empty;
    [JsonPropertyName("reviewed_at")] public DateTimeOffset? ReviewedAt { get; set; }
    [JsonPropertyName("reviewer_id")] public string? ReviewerId { get; set; }
    [JsonPropertyName("reason")] public string? Reason { get; set; }
    [JsonPropertyName("payload")] public VideoSubmissionPayload? Payload { get; set; }
    [JsonPropertyName("correction")] public SubtitleCorrectionPayload? SubtitleCorrection { get; set; }
}

public class VideoSubmissionPayload
{
    [JsonPropertyName("youtube_id")] public string YoutubeId { get; set; } = string.Empty;
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("creator")] public string? Creator { get; set; }
    // New fields for canonicalization
    [JsonPropertyName("creator_original")] public string? CreatorOriginal { get; set; }
    [JsonPropertyName("creator_canonical")] public string? CreatorCanonical { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("tags")] public string[]? Tags { get; set; }
    [JsonPropertyName("release_date")] public string? ReleaseDate { get; set; }
    [JsonPropertyName("subtitle_storage_key")] public string? SubtitleStorageKey { get; set; }
    [JsonPropertyName("subtitle_url")] public string? SubtitleUrl { get; set; }
}

public class SubtitleCorrectionPayload
{
    [JsonPropertyName("video_id")] public string VideoId { get; set; } = string.Empty;
    [JsonPropertyName("subtitle_version")] public int SubtitleVersion { get; set; }
    [JsonPropertyName("timestamp_seconds")] public double TimestampSeconds { get; set; }
    [JsonPropertyName("window_start_seconds")] public double WindowStartSeconds { get; set; }
    [JsonPropertyName("window_end_seconds")] public double WindowEndSeconds { get; set; }
    [JsonPropertyName("notes")] public string? Notes { get; set; }
    [JsonPropertyName("submitted_by_user_id")] public string SubmittedByUserId { get; set; } = string.Empty;
    [JsonPropertyName("submitted_by_display_name")] public string? SubmittedByDisplayName { get; set; }
    [JsonPropertyName("cues")] public List<SubtitleCorrectionCue> Cues { get; set; } = new();
}

public class SubtitleCorrectionCue
{
    [JsonPropertyName("sequence")] public int Sequence { get; set; }
    [JsonPropertyName("start_seconds")] public double StartSeconds { get; set; }
    [JsonPropertyName("end_seconds")] public double EndSeconds { get; set; }
    [JsonPropertyName("original_text")] public string OriginalText { get; set; } = string.Empty;
    [JsonPropertyName("updated_text")] public string UpdatedText { get; set; } = string.Empty;
}
