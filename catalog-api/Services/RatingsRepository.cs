using System.Text.Json;
using System.Text.Json.Serialization;

namespace catalog_api.Services;

public enum RatingValue { red, yellow, green }

public record RatingSummaryDto(
    [property: JsonPropertyName("Red")] int Red,
    [property: JsonPropertyName("Yellow")] int Yellow,
    [property: JsonPropertyName("Green")] int Green,
    [property: JsonPropertyName("Version")] int Version,
    [property: JsonPropertyName("UserRating")] RatingValue? UserRating
);

public record RatingEvent(
    [property: JsonPropertyName("VideoId")] string VideoId,
    [property: JsonPropertyName("Version")] int Version,
    [property: JsonPropertyName("UserId")] string UserId,
    [property: JsonPropertyName("UserName")] string? UserName,
    [property: JsonPropertyName("Value")] RatingValue Value,
    [property: JsonPropertyName("CreatedAt")] DateTimeOffset CreatedAt
);

internal class RatingsRepository
{
    private readonly string _path;
    private readonly JsonSerializerOptions _opts = new() { WriteIndented = false, PropertyNameCaseInsensitive = true };
    private RatingsRoot _store = new();
    private readonly object _lock = new();

    public RatingsRepository(IConfiguration config, IWebHostEnvironment env)
    {
        var configured = config["Data:RatingsPath"] ?? config["DATA_RATINGS_PATH"];
        _path = Path.GetFullPath(Path.Combine(env.ContentRootPath, configured ?? "data/ratings.json"));
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
                _store = JsonSerializer.Deserialize<RatingsRoot>(json, _opts) ?? new();
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
        catch
        {
            // swallow IO errors in dev; could log in future
        }
    }

    public RatingSummaryDto GetSummary(string videoId, int version, string? user)
    {
        lock (_lock)
        {
            if (_store.Videos.TryGetValue(videoId, out var vAgg) && vAgg.Versions.TryGetValue(version, out var agg))
            {
                return new RatingSummaryDto(agg.Red, agg.Yellow, agg.Green, version, user != null && agg.UserRatings.TryGetValue(user, out var ur) ? ur : null);
            }
            return new RatingSummaryDto(0, 0, 0, version, null);
        }
    }

    public void Submit(string user, string videoId, int version, RatingValue value, string? userName = null)
    {
        lock (_lock)
        {
            if (!_store.Videos.TryGetValue(videoId, out var vAgg))
            {
                vAgg = new VersionAggregates();
                _store.Videos[videoId] = vAgg;
            }
            if (!vAgg.Versions.TryGetValue(version, out var agg))
            {
                agg = new RatingAggregate();
                vAgg.Versions[version] = agg;
            }
            if (agg.UserRatings.TryGetValue(user, out var prev))
            {
                Decrement(agg, prev);
            }
            agg.UserRatings[user] = value;
            Increment(agg, value);
            _store.Events.Add(new RatingEvent(videoId, version, user, userName, value, DateTimeOffset.UtcNow));
            // keep only the last 1000 events to bound file size
            if (_store.Events.Count > 1000)
            {
                _store.Events.RemoveRange(0, _store.Events.Count - 1000);
            }
            Save();
        }
    }

    public IReadOnlyList<RatingEvent> GetRecent(int limit)
    {
        lock (_lock)
        {
            var count = Math.Clamp(limit, 1, 200);
            return _store.Events.OrderByDescending(e => e.CreatedAt).Take(count).ToList();
        }
    }

    private static void Increment(RatingAggregate agg, RatingValue v)
    {
        switch (v)
        {
            case RatingValue.red: agg.Red++; break;
            case RatingValue.yellow: agg.Yellow++; break;
            case RatingValue.green: agg.Green++; break;
        }
    }

    private static void Decrement(RatingAggregate agg, RatingValue v)
    {
        switch (v)
        {
            case RatingValue.red: agg.Red = Math.Max(0, agg.Red - 1); break;
            case RatingValue.yellow: agg.Yellow = Math.Max(0, agg.Yellow - 1); break;
            case RatingValue.green: agg.Green = Math.Max(0, agg.Green - 1); break;
        }
    }

    private class RatingsRoot
    {
        public Dictionary<string, VersionAggregates> Videos { get; set; } = new();
        public List<RatingEvent> Events { get; set; } = new();
    }

    private class VersionAggregates
    {
        public Dictionary<int, RatingAggregate> Versions { get; set; } = new();
    }

    private class RatingAggregate
    {
        public int Red { get; set; }
        public int Yellow { get; set; }
        public int Green { get; set; }
        public Dictionary<string, RatingValue> UserRatings { get; set; } = new();
    }
}
