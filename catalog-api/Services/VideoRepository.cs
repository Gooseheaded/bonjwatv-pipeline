using System.Text.Json;
using System.Text.Json.Serialization;

namespace catalog_api.Services;

public class VideoItem
{
    [JsonPropertyName("v")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("creator")]
    public string? Creator { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("tags")]
    public string[]? Tags { get; set; }

    [JsonPropertyName("releaseDate")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("subtitleUrl")]
    public string? SubtitleUrl { get; set; }

    [JsonPropertyName("subtitleContributors")]
    public List<SubtitleContributor>? SubtitleContributors { get; set; }

    [JsonPropertyName("hidden")]
    public bool? Hidden { get; set; }

    [JsonPropertyName("hiddenReason")]
    public string? HiddenReason { get; set; }

    [JsonPropertyName("hiddenAt")]
    public string? HiddenAt { get; set; }

    [JsonPropertyName("durationSeconds")]
    public int? DurationSeconds { get; set; }
}

public class SubtitleContributor
{
    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("userId")]
    public string? UserId { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("submittedAt")]
    public string? SubmittedAt { get; set; }
}

public class VideoRepository : IDisposable
{
    private readonly string _storePath;
    private readonly JsonSerializerOptions _opts = new() { PropertyNameCaseInsensitive = true };
    private List<VideoItem> _cache = new();
    private FileSystemWatcher? _watcher;
    private DateTime _lastWriteSeenUtc = DateTime.MinValue;
    private readonly object _sync = new();

    public VideoRepository(IConfiguration config, IWebHostEnvironment env, ILogger<VideoRepository> logger)
    {
        // Primary store path (new): DATA_VIDEOS_STORE_PATH or Data:VideosStorePath → default under app data
        var storeConfigured = config["DATA_VIDEOS_STORE_PATH"] ?? config["Data:VideosStorePath"];
        _storePath = Path.GetFullPath(Path.Combine(env.ContentRootPath, storeConfigured ?? "data/catalog-videos.json"));
        ImportFromLegacyIfEmpty(config, env, logger);
        Load();

        var dir = Path.GetDirectoryName(_storePath);
        var file = Path.GetFileName(_storePath);
        if (dir != null && Directory.Exists(dir))
        {
            _watcher = new FileSystemWatcher(dir, file)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
            };
            _watcher.Changed += (_, __) => Load();
            _watcher.Created += (_, __) => Load();
            _watcher.Renamed += (_, __) => Load();
            _watcher.EnableRaisingEvents = true;
        }
    }

    private void ImportFromLegacyIfEmpty(IConfiguration config, IWebHostEnvironment env, ILogger logger)
    {
        try
        {
            // If store already exists and has content, skip
            if (File.Exists(_storePath))
            {
                var existing = File.ReadAllText(_storePath);
                if (!string.IsNullOrWhiteSpace(existing) && existing.TrimStart().StartsWith("["))
                {
                    return;
                }
            }

            // Find legacy videos.json path
            var legacyConfigured = config["DATA_JSON_PATH"] ?? config["Data:JsonPath"];
            var legacyPath = Path.GetFullPath(Path.Combine(env.ContentRootPath, legacyConfigured ?? "data/videos.json"));
            if (!File.Exists(legacyPath)) return;

            var json = File.ReadAllText(legacyPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return;

            // Normalize minimal fields; copy through as-is
            var list = new List<Dictionary<string, object?>>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                var obj = new Dictionary<string, object?>();
                foreach (var prop in el.EnumerateObject())
                {
                    obj[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString(),
                        JsonValueKind.Number => (object?)prop.Value.GetRawText(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.Array => prop.Value.EnumerateArray().Select(x => x.ToString()).ToArray(),
                        _ => null
                    };
                }
                list.Add(obj);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);
            File.WriteAllText(_storePath, System.Text.Json.JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
            logger.LogInformation("Imported {Count} videos from legacy videos.json into store {StorePath}", list.Count, _storePath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed importing legacy videos.json into store {StorePath}", _storePath);
        }
    }

    private void Load()
    {
        try
        {
            lock (_sync)
            {
                if (File.Exists(_storePath))
                {
                    var json = File.ReadAllText(_storePath);
                    var list = JsonSerializer.Deserialize<List<VideoItem>>(json, _opts) ?? new();
                    _cache = list;
                    try { _lastWriteSeenUtc = File.GetLastWriteTimeUtc(_storePath); } catch { }
                }
                else
                {
                    _cache = new();
                    _lastWriteSeenUtc = DateTime.MinValue;
                }
            }
        }
        catch
        {
            // Keep previous cache on error
        }
    }

    private void EnsureFresh()
    {
        try
        {
            if (File.Exists(_storePath))
            {
                var ts = File.GetLastWriteTimeUtc(_storePath);
                if (ts > _lastWriteSeenUtc)
                {
                    Load();
                }
            }
        }
        catch { }
    }

    public IReadOnlyList<VideoItem> All()
    {
        EnsureFresh();
        return _cache;
    }

    public void Dispose() => _watcher?.Dispose();
}
