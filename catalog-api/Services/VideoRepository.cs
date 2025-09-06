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

    [JsonPropertyName("hidden")]
    public bool? Hidden { get; set; }

    [JsonPropertyName("hiddenReason")]
    public string? HiddenReason { get; set; }

    [JsonPropertyName("hiddenAt")]
    public string? HiddenAt { get; set; }
}

public class VideoRepository : IDisposable
{
    private readonly string _jsonPath;
    private readonly JsonSerializerOptions _opts = new() { PropertyNameCaseInsensitive = true };
    private List<VideoItem> _cache = new();
    private FileSystemWatcher? _watcher;

    public VideoRepository(IConfiguration config, IWebHostEnvironment env, ILogger<VideoRepository> logger)
    {
        // Prefer explicit env var in prod; fallback to appsettings for dev.
        var configured = config["DATA_JSON_PATH"] ?? config["Data:JsonPath"];
        // Default relative to content root
        _jsonPath = Path.GetFullPath(Path.Combine(env.ContentRootPath, configured ?? "../webapp/data/videos.json"));
        Load();

        var dir = Path.GetDirectoryName(_jsonPath);
        var file = Path.GetFileName(_jsonPath);
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

    private void Load()
    {
        try
        {
            if (File.Exists(_jsonPath))
            {
                var json = File.ReadAllText(_jsonPath);
                var list = JsonSerializer.Deserialize<List<VideoItem>>(json, _opts) ?? new();
                _cache = list;
            }
            else
            {
                _cache = new();
            }
        }
        catch
        {
            // Keep previous cache on error
        }
    }

    public IReadOnlyList<VideoItem> All() => _cache;

    public void Dispose() => _watcher?.Dispose();
}
