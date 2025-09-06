using System.Text.Json;
using System.Net.Http;

namespace bwkt_webapp.Services;

public interface IRatingsClient
{
    (int Red, int Yellow, int Green) GetSummary(string videoId, int version = 1);
}

public class HttpRatingsClient : IRatingsClient
{
    private readonly HttpClient _http = new HttpClient();

    private static string? ResolveApiBase()
    {
        var explicitApiBase = Environment.GetEnvironmentVariable("CATALOG_API_BASE_URL");
        if (!string.IsNullOrWhiteSpace(explicitApiBase)) return explicitApiBase!.TrimEnd('/');
        var apiVideosUrl = Environment.GetEnvironmentVariable("DATA_CATALOG_URL");
        if (string.IsNullOrWhiteSpace(apiVideosUrl)) return null;
        try
        {
            var uri = new Uri(apiVideosUrl!);
            var path = uri.AbsolutePath.TrimEnd('/');
            if (path.EndsWith("/videos", StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring(0, path.Length - "/videos".Length);
            }
            var builderUri = new UriBuilder(uri.Scheme, uri.Host, uri.Port, path);
            return builderUri.Uri.ToString().TrimEnd('/');
        }
        catch { return null; }
    }

    public (int Red, int Yellow, int Green) GetSummary(string videoId, int version = 1)
    {
        var apiBase = ResolveApiBase();
        if (string.IsNullOrWhiteSpace(apiBase)) return (0, 0, 0);
        try
        {
            var url = $"{apiBase}/videos/{videoId}/ratings?version={version}";
            var json = _http.GetStringAsync(url).GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            int r = root.TryGetProperty("Red", out var rr) ? rr.GetInt32() : 0;
            int y = root.TryGetProperty("Yellow", out var yy) ? yy.GetInt32() : 0;
            int g = root.TryGetProperty("Green", out var gg) ? gg.GetInt32() : 0;
            return (r, y, g);
        }
        catch
        {
            return (0, 0, 0);
        }
    }
}

