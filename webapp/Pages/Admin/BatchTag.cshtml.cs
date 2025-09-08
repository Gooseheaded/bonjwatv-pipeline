using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;

namespace bwkt_webapp.Pages.Admin
{
    public class BatchTagModel : PageModel
    {
        [BindProperty]
        public string? VideoIds { get; set; }
        [BindProperty]
        public string? Tag { get; set; }

        public bool IsAdmin { get; private set; }
        public string? Error { get; private set; }
        public string? Info { get; private set; }
        public string? Success { get; private set; }
        public List<ResultItem> Results { get; } = new();

        private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
        {
            "z","p","t","story","zvz","zvt","zvp","pvz","pvt","pvp","tvz","tvt","tvp"
        };

        public void OnGet()
        {
            IsAdmin = CheckIsAdmin();
        }

        public void OnPost()
        {
            IsAdmin = CheckIsAdmin();
            if (!IsAdmin)
            {
                Error = "Admin access required.";
                return;
            }

            var ids = ParseIds(VideoIds ?? string.Empty);
            if (ids.Count == 0)
            {
                Error = "Please provide at least one video ID.";
                return;
            }
            if (string.IsNullOrWhiteSpace(Tag) || !AllowedTags.Contains(Tag!))
            {
                Error = "Please select a valid tag.";
                return;
            }
            Info = $"Applying tag '{Tag}' to {ids.Count} video(s).";

            var apiBase = DeriveApiBase();
            if (string.IsNullOrWhiteSpace(apiBase))
            {
                Error = "Catalog API base URL is not configured.";
                return;
            }

            var okCount = 0;
            foreach (var id in ids)
            {
                try
                {
                    using var http = new HttpClient();
                    var url = $"{apiBase}/admin/videos/{id}/tags";
                    using var msg = new HttpRequestMessage(HttpMethod.Patch, url)
                    {
                        Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new { action = "add", tag = Tag }), System.Text.Encoding.UTF8, "application/json")
                    };
                    var resp = http.Send(msg);
                    if (resp.IsSuccessStatusCode)
                    {
                        okCount++;
                        Results.Add(new ResultItem(id, true, ""));
                    }
                    else
                    {
                        var text = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        Results.Add(new ResultItem(id, false, $"HTTP {(int)resp.StatusCode}: {text}"));
                    }
                }
                catch (Exception ex)
                {
                    Results.Add(new ResultItem(id, false, ex.Message));
                }
            }
            Success = $"Applied '{Tag}' to {okCount} of {ids.Count} video(s).";
        }

        private bool CheckIsAdmin()
        {
            var ids = Environment.GetEnvironmentVariable("ADMIN_USER_IDS");
            if (string.IsNullOrWhiteSpace(ids)) return false;
            var current = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            return !string.IsNullOrWhiteSpace(current) && ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(current);
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

        public static List<string> ParseIds(string text)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in (text ?? string.Empty).Split('\n', '\r'))
            {
                var s = (raw ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(s)) continue;
                // Basic YouTube video ID check (len and allowed chars)
                if (s.Length >= 6 && s.Length <= 64 && s.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'))
                {
                    set.Add(s);
                }
            }
            return set.ToList();
        }

        public record ResultItem(string VideoId, bool Ok, string? Message);
    }
}
