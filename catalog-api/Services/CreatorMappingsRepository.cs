using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace catalog_api.Services;

public class CreatorMapping
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("source")] public string Source { get; set; } = string.Empty;
    [JsonPropertyName("source_normalized")] public string SourceNormalized { get; set; } = string.Empty;
    [JsonPropertyName("canonical")] public string Canonical { get; set; } = string.Empty;
    [JsonPropertyName("notes")] public string? Notes { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; set; }
    [JsonPropertyName("created_by")] public string? CreatedBy { get; set; }
    [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; set; }
}

internal class CreatorMappingsRepository
{
    private readonly string _path;
    private readonly JsonSerializerOptions _opts = new() { WriteIndented = false, PropertyNameCaseInsensitive = true };
    private readonly object _lock = new();
    private Root _store = new();

    public CreatorMappingsRepository(IConfiguration config, IWebHostEnvironment env)
    {
        var configured = config["Data:CreatorMappingsPath"] ?? config["DATA_CREATOR_MAPPINGS_PATH"];
        _path = Path.GetFullPath(Path.Combine(env.ContentRootPath, configured ?? "data/creator-mappings.json"));
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

    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var nfkc = input.Normalize(NormalizationForm.FormKC);
        // Collapse whitespace runs to single space
        var parts = nfkc.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);
        var collapsed = string.Join(" ", parts);
        return collapsed.Trim().ToLowerInvariant();
    }

    public string? Resolve(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        var key = Normalize(source);
        lock (_lock)
        {
            var m = _store.Items.FirstOrDefault(x => x.SourceNormalized == key);
            return m?.Canonical;
        }
    }

    public (IReadOnlyList<CreatorMapping> Items, int TotalCount) List(string? q, int page, int pageSize)
    {
        lock (_lock)
        {
            IEnumerable<CreatorMapping> items = _store.Items;
            if (!string.IsNullOrWhiteSpace(q))
            {
                var s = q.Trim();
                items = items.Where(x => (x.Source?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false)
                                       || (x.Canonical?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false));
            }
            items = items.OrderByDescending(x => x.UpdatedAt);
            var total = items.Count();
            var pageItems = items.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return (pageItems, total);
        }
    }

    public (CreatorMapping? Created, string? Error) Create(string source, string canonical, string? createdBy)
    {
        var srcNorm = Normalize(source);
        if (string.IsNullOrWhiteSpace(srcNorm)) return (null, "source_required");
        if (string.IsNullOrWhiteSpace(canonical?.Trim())) return (null, "canonical_required");
        lock (_lock)
        {
            if (_store.Items.Any(x => x.SourceNormalized == srcNorm)) return (null, "conflict");
            var now = DateTimeOffset.UtcNow;
            var m = new CreatorMapping
            {
                Id = Guid.NewGuid().ToString("N"),
                Source = source.Trim(),
                SourceNormalized = srcNorm,
                Canonical = canonical.Trim(),
                CreatedAt = now,
                CreatedBy = createdBy,
                UpdatedAt = now
            };
            _store.Items.Add(m);
            Save();
            return (m, null);
        }
    }

    public (CreatorMapping? Updated, string? Error) Update(string id, string source, string canonical, string? updatedBy)
    {
        var srcNorm = Normalize(source);
        if (string.IsNullOrWhiteSpace(srcNorm)) return (null, "source_required");
        if (string.IsNullOrWhiteSpace(canonical?.Trim())) return (null, "canonical_required");
        lock (_lock)
        {
            var existing = _store.Items.FirstOrDefault(x => x.Id == id);
            if (existing == null) return (null, "not_found");
            if (_store.Items.Any(x => x.Id != id && x.SourceNormalized == srcNorm)) return (null, "conflict");
            existing.Source = source.Trim();
            existing.SourceNormalized = srcNorm;
            existing.Canonical = canonical.Trim();
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            Save();
            return (existing, null);
        }
    }

    public bool Delete(string id)
    {
        lock (_lock)
        {
            var idx = _store.Items.FindIndex(x => x.Id == id);
            if (idx < 0) return false;
            _store.Items.RemoveAt(idx);
            Save();
            return true;
        }
    }

    private class Root
    {
        public List<CreatorMapping> Items { get; set; } = new();
    }
}

