using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;

namespace bwkt_webapp.Pages.Admin
{
    public class RatingsModel : PageModel
    {
        public bool IsAdmin { get; private set; }
        public int PageNumber { get; private set; }
        public int PageSize { get; private set; }
        public int TotalCount { get; private set; }
        public List<RatingEvent> All { get; private set; } = new();
        public List<RatingEvent> Paged { get; private set; } = new();

        public void OnGet(int? page, int? pageSize)
        {
            IsAdmin = CheckIsAdmin();
            if (!IsAdmin) return;

            PageNumber = Math.Max(1, page ?? 1);
            PageSize = Math.Clamp(pageSize ?? 50, 10, 200);

            TryLoadAll();
            // Sort newest first
            All = All.OrderByDescending(e => e.CreatedAt).ToList();
            TotalCount = All.Count;

            Paged = All.Skip((PageNumber - 1) * PageSize).Take(PageSize).ToList();
        }

        private bool CheckIsAdmin()
        {
            var ids = Environment.GetEnvironmentVariable("ADMIN_USER_IDS");
            if (string.IsNullOrWhiteSpace(ids)) return false;
            var current = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            return !string.IsNullOrWhiteSpace(current) && ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(current);
        }

        private void TryLoadAll()
        {
            try
            {
                using var http = new HttpClient();
                string json;
                var apiBase = DeriveApiBase();
                if (!string.IsNullOrWhiteSpace(apiBase))
                {
                    var url = $"{apiBase}/admin/ratings/recent?limit=1000";
                    json = http.GetStringAsync(url).GetAwaiter().GetResult();
                }
                else
                {
                    var url = $"{Request.Scheme}://{Request.Host}/admin/ratings/recent?limit=1000";
                    using var msg = new HttpRequestMessage(HttpMethod.Get, url);
                    var cookie = Request.Headers["Cookie"].ToString();
                    if (!string.IsNullOrWhiteSpace(cookie)) msg.Headers.Add("Cookie", cookie);
                    var resp = http.Send(msg);
                    resp.EnsureSuccessStatusCode();
                    json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                }
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var items = JsonSerializer.Deserialize<List<RatingEvent>>(json, opts) ?? new();
                All = items;
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

        public enum RatingValue { red = 0, yellow = 1, green = 2 }
        public record RatingEvent(string VideoId, int Version, string UserId, string? UserName, RatingValue Value, DateTimeOffset CreatedAt);
    }
}
