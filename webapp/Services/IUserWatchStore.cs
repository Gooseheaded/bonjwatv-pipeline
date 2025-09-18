using System.Text.Json;

namespace bwkt_webapp.Services;

public interface IUserWatchStore
{
    Task<HashSet<string>> GetWatchedAsync(string userKey);
    Task AddWatchedAsync(string userKey, string videoId);
}

public class FileUserWatchStore : IUserWatchStore
{
    private readonly string _root;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileUserWatchStore(string root)
    {
        _root = root;
        Directory.CreateDirectory(_root);
    }

    private string PathFor(string userKey)
        => System.IO.Path.Combine(_root, Sanitize(userKey) + ".json");

    private static string Sanitize(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s;
    }

    public async Task<HashSet<string>> GetWatchedAsync(string userKey)
    {
        var path = PathFor(userKey);
        if (!File.Exists(path)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var fs = File.OpenRead(path);
            var doc = await JsonDocument.ParseAsync(fs);
            if (doc.RootElement.TryGetProperty("ids", out var ids) && ids.ValueKind == JsonValueKind.Array)
            {
                return ids.EnumerateArray()
                          .Where(e => e.ValueKind == JsonValueKind.String)
                          .Select(e => e.GetString()!)
                          .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
        }
        catch { /* ignore corrupt file */ }
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public async Task AddWatchedAsync(string userKey, string videoId)
    {
        var path = PathFor(userKey);
        await _gate.WaitAsync();
        try
        {
            var set = await GetWatchedAsync(userKey);
            if (!set.Add(videoId)) return;
            using var fs = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(fs, new { ids = set, updatedAt = DateTimeOffset.UtcNow });
        }
        finally
        {
            _gate.Release();
        }
    }
}

