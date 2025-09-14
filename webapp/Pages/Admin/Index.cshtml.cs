using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Net.Http;
using System.Text.Json;

namespace bwkt_webapp.Pages.Admin
{
    public class IndexModel : PageModel
    {
        public bool IsAdmin { get; private set; }
        public List<RatingEvent> Events { get; private set; } = new();
        public List<SubmissionItem> Pending { get; private set; } = new();
        public List<HiddenItem> Hidden { get; private set; } = new();

        // Debug metadata surfaced to the Admin page
        public string? ApiBaseForDebug { get; private set; }
        public string? DataCatalogUrlEnv { get; private set; }
        public string? CatalogApiBaseEnv { get; private set; }
        public DateTimeOffset PageGeneratedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

        public void OnGet()
        {
            IsAdmin = CheckIsAdmin();
            if (!IsAdmin) return;
            // Capture debug context for client-side logging
            ApiBaseForDebug = DeriveApiBase();
            DataCatalogUrlEnv = Environment.GetEnvironmentVariable("DATA_CATALOG_URL");
            CatalogApiBaseEnv = Environment.GetEnvironmentVariable("CATALOG_API_BASE_URL");
            TryLoadRecent();
            TryLoadPending();
            TryLoadHidden();
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
                // Prefer direct Catalog API call; fall back to same-origin proxy with cookie forwarding
                var apiBase = DeriveApiBase();
                using var http = new HttpClient();
                string json;
                if (!string.IsNullOrWhiteSpace(apiBase))
                {
                    var url = $"{apiBase}/admin/ratings/recent?limit=10";
                    json = http.GetStringAsync(url).GetAwaiter().GetResult();
                }
                else
                {
                    var url = $"{Request.Scheme}://{Request.Host}/admin/ratings/recent?limit=10";
                    using var msg = new HttpRequestMessage(HttpMethod.Get, url);
                    var cookie = Request.Headers["Cookie"].ToString();
                    if (!string.IsNullOrWhiteSpace(cookie)) msg.Headers.Add("Cookie", cookie);
                    var resp = http.Send(msg);
                    resp.EnsureSuccessStatusCode();
                    json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                }
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var items = JsonSerializer.Deserialize<List<RatingEvent>>(json, opts) ?? new();
                Events = items;
            }
            catch { }
        }

        private void TryLoadPending()
        {
            try
            {
                var apiBase = DeriveApiBase();
                if (string.IsNullOrWhiteSpace(apiBase)) return;
                using var http = new HttpClient();
                var url = $"{apiBase}/admin/submissions?status=pending&page=1&pageSize=50";
                var json = http.GetStringAsync(url).GetAwaiter().GetResult();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in items.EnumerateArray())
                    {
                        var item = ParseSubmission(el);
                        // Determine if this submission updates an existing video (by youtube_id)
                        bool isUpdate = false;
                        try
                        {
                            if (!string.IsNullOrWhiteSpace(item.YoutubeId))
                            {
                                var check = http.GetAsync($"{apiBase}/videos/{item.YoutubeId}").GetAwaiter().GetResult();
                                isUpdate = check.IsSuccessStatusCode;
                            }
                        }
                        catch { isUpdate = false; }
                        Pending.Add(item with { IsUpdate = isUpdate });
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

        private void TryLoadHidden()
        {
            try
            {
                var apiBase = DeriveApiBase();
                if (string.IsNullOrWhiteSpace(apiBase)) return;
                using var http = new HttpClient();
                var json = http.GetStringAsync($"{apiBase}/admin/videos/hidden").GetAwaiter().GetResult();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in doc.RootElement.EnumerateArray())
                    {
                        var id = el.GetProperty("id").GetString() ?? string.Empty;
                        var title = el.TryGetProperty("title", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                        var reason = el.TryGetProperty("hiddenReason", out var r) ? r.GetString() : null;
                        var at = el.TryGetProperty("hiddenAt", out var ha) ? ha.GetString() : null;
                        Hidden.Add(new HiddenItem(id, title, reason, at));
                    }
                }
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
        public record SubmissionItem(string Id, string Status, DateTimeOffset SubmittedAt, string? SubmittedBy, string? YoutubeId, string? Title)
        {
            public bool IsUpdate { get; init; }
        }
        public record HiddenItem(string Id, string Title, string? Reason, string? HiddenAt);
    }
}
