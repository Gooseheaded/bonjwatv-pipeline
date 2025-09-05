using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Net.Http;
using System.Text.Json;

namespace bwkt_webapp.Pages.Admin
{
    public class IndexModel : PageModel
    {
        public bool IsAdmin { get; private set; }
        public List<RatingEvent> Events { get; private set; } = new();

        public void OnGet()
        {
            IsAdmin = CheckIsAdmin();
            if (!IsAdmin) return;
            TryLoadRecent();
        }

        private bool CheckIsAdmin()
        {
            var ids = Environment.GetEnvironmentVariable("ADMIN_USER_IDS");
            if (string.IsNullOrWhiteSpace(ids)) return false;
            var current = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            return !string.IsNullOrWhiteSpace(current) && ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(current);
        }

        private void TryLoadRecent()
        {
            try
            {
                var apiBase = DeriveApiBase();
                if (string.IsNullOrWhiteSpace(apiBase)) return;
                var url = $"{apiBase}/admin/ratings/recent?limit=100";
                using var http = new HttpClient();
                var json = http.GetStringAsync(url).GetAwaiter().GetResult();
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var items = JsonSerializer.Deserialize<List<RatingEvent>>(json, opts) ?? new();
                Events = items;
            }
            catch { }
        }

        private static string? DeriveApiBase()
        {
            var apiVideosUrl = Environment.GetEnvironmentVariable("DATA_CATALOG_URL");
            var explicitApiBase = Environment.GetEnvironmentVariable("CATALOG_API_BASE_URL");
            if (!string.IsNullOrWhiteSpace(explicitApiBase)) return explicitApiBase!.TrimEnd('/');
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

        public record RatingEvent(string VideoId, int Version, string UserId, string? UserName, string Value, DateTimeOffset CreatedAt);
    }
}

