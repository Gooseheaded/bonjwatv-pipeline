using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;

namespace bwkt_webapp.Pages.Admin
{
    public class SubmissionModel : PageModel
    {
        public bool IsAdmin { get; private set; }
        public JsonElement Submission { get; private set; }
        public string? SubtitleText { get; private set; }
        public bool? IsUpdate { get; private set; }

        public void OnGet(string id)
        {
            IsAdmin = CheckIsAdmin();
            if (!IsAdmin) return;
            LoadDetail(id);
        }

        private bool CheckIsAdmin()
        {
            var ids = Environment.GetEnvironmentVariable("ADMIN_USER_IDS");
            if (string.IsNullOrWhiteSpace(ids)) return false;
            var current = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            return !string.IsNullOrWhiteSpace(current) && ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(current);
        }

        private void LoadDetail(string id)
        {
            try
            {
                var apiBase = DeriveApiBase();
                if (string.IsNullOrWhiteSpace(apiBase)) return;
                using var http = new HttpClient();
                var url = $"{apiBase}/admin/submissions/{id}";
                var json = http.GetStringAsync(url).GetAwaiter().GetResult();
                using var doc = JsonDocument.Parse(json);
                Submission = doc.RootElement.Clone();

                // Determine new/update type as early as possible (do not return early)
                var payload = Submission.TryGetProperty("payload", out var p) ? p : default;
                string? vid = null;
                if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("youtube_id", out var y))
                {
                    vid = y.GetString();
                }
                try
                {
                    if (!string.IsNullOrWhiteSpace(vid))
                    {
                        var existsResp = http.GetAsync($"{apiBase}/videos/{vid}").GetAwaiter().GetResult();
                        IsUpdate = existsResp.IsSuccessStatusCode;
                    }
                    else
                    {
                        IsUpdate = null;
                    }
                }
                catch { IsUpdate = false; }

                // Attempt to load subtitle preview:
                // 1) Prefer API admin preview (handles staged or external)
                // 2) Fallback to first-party via youtube_id; 3) Fallback to external subtitle_url
                try
                {
                    // Try admin preview endpoint first
                    try
                    {
                        var previewUrl = $"{apiBase}/admin/submissions/{id}/subtitle";
                        var text = http.GetStringAsync(previewUrl).GetAwaiter().GetResult();
                        if (!string.IsNullOrWhiteSpace(text)) { SubtitleText = text; }
                    }
                    catch { /* fall through to legacy fallbacks */ }

                    if (string.IsNullOrWhiteSpace(SubtitleText))
                    {
                        if (!string.IsNullOrWhiteSpace(vid))
                        {
                            SubtitleText = http.GetStringAsync($"{apiBase}/subtitles/{vid}/1.srt").GetAwaiter().GetResult();
                        }
                        else if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("subtitle_url", out var su))
                        {
                            var ext = su.GetString();
                            if (!string.IsNullOrWhiteSpace(ext))
                            {
                                SubtitleText = http.GetStringAsync(ext!).GetAwaiter().GetResult();
                            }
                        }
                    }
                }
                catch { SubtitleText = null; }
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
    }
}
