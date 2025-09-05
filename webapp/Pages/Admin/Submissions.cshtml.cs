using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;

namespace bwkt_webapp.Pages.Admin
{
    public class SubmissionsModel : PageModel
    {
        public bool IsAdmin { get; private set; }
        public List<SubmissionItem> Items { get; private set; } = new();

        public void OnGet()
        {
            IsAdmin = CheckIsAdmin();
            if (!IsAdmin) return;
            LoadPending();
        }

        private bool CheckIsAdmin()
        {
            var ids = Environment.GetEnvironmentVariable("ADMIN_USER_IDS");
            if (string.IsNullOrWhiteSpace(ids)) return false;
            var current = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            return !string.IsNullOrWhiteSpace(current) && ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(current);
        }

        private void LoadPending()
        {
            try
            {
                var apiBase = DeriveApiBase();
                if (string.IsNullOrWhiteSpace(apiBase)) return;
                using var http = new HttpClient();
                var url = $"{apiBase}/admin/submissions?status=pending&page=1&pageSize=100";
                var json = http.GetStringAsync(url).GetAwaiter().GetResult();
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in items.EnumerateArray())
                    {
                        Items.Add(ParseSubmission(el));
                    }
                }
            }
            catch { }
        }

        private static SubmissionItem ParseSubmission(JsonElement el)
        {
            var id = el.GetProperty("id").GetString() ?? string.Empty;
            var status = el.TryGetProperty("status", out var st) ? (st.GetString() ?? "") : "";
            var submittedAt = el.TryGetProperty("submitted_at", out var sa) ? sa.GetDateTimeOffset() : DateTimeOffset.MinValue;
            string? youtubeId = null; string? title = null; string? submittedBy = null;
            if (el.TryGetProperty("payload", out var payload))
            {
                youtubeId = payload.TryGetProperty("youtube_id", out var y) ? y.GetString() : null;
                title = payload.TryGetProperty("title", out var t) ? t.GetString() : null;
            }
            submittedBy = el.TryGetProperty("submitted_by", out var sb) ? sb.GetString() : null;
            return new SubmissionItem(id, status, submittedAt, submittedBy, youtubeId, title);
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

        public record SubmissionItem(string Id, string Status, DateTimeOffset SubmittedAt, string? SubmittedBy, string? YoutubeId, string? Title);
    }
}
