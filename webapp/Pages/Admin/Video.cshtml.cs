using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;
using bwkt_webapp.Services;

namespace bwkt_webapp.Pages.Admin
{
    public class VideoModel : PageModel
    {
        private readonly IVideoService _videoService;
        public bool IsAdmin { get; private set; }
        public JsonElement Video { get; private set; }
        public int RedCount { get; private set; }
        public int YellowCount { get; private set; }
        public int GreenCount { get; private set; }
        public string? SubtitleText { get; private set; }
        public string? Submitter { get; private set; }
        public string? SubmissionDate { get; private set; }
        public List<string> AllowedTags { get; } = new() { "z","p","t","story","zvz","zvt","zvp","pvz","pvt","pvp","tvz","tvt","tvp" };
        public HashSet<string> SelectedTags { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

        public VideoModel(IVideoService videoService)
        {
            _videoService = videoService;
        }

        public void OnGet(string id)
        {
            IsAdmin = CheckIsAdmin();
            if (!IsAdmin) return;
            Load(id);
        }

        private bool CheckIsAdmin()
        {
            var ids = Environment.GetEnvironmentVariable("ADMIN_USER_IDS");
            if (string.IsNullOrWhiteSpace(ids)) return false;
            var current = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            return !string.IsNullOrWhiteSpace(current) && ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(current);
        }

        private void Load(string id)
        {
            try
            {
                var apiBase = DeriveApiBase();
                if (string.IsNullOrWhiteSpace(apiBase)) return;
                using var http = new HttpClient();
                // Load video details
                var url = $"{apiBase}/admin/videos/{id}";
                var json = http.GetStringAsync(url).GetAwaiter().GetResult();
                using var doc = JsonDocument.Parse(json);
                Video = doc.RootElement.Clone();
                try
                {
                    if (Video.TryGetProperty("tags", out var tg) && tg.ValueKind == JsonValueKind.Array)
                    {
                        SelectedTags = tg.EnumerateArray().Select(e => e.GetString() ?? string.Empty).Where(s => !string.IsNullOrWhiteSpace(s)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    }
                }
                catch { }

                // Load ratings summary (version 1)
                try
                {
                    var ratingsJson = http.GetStringAsync($"{apiBase}/videos/{id}/ratings?version=1").GetAwaiter().GetResult();
                    using var rdoc = JsonDocument.Parse(ratingsJson);
                    var root = rdoc.RootElement;
                    RedCount = root.TryGetProperty("Red", out var r) ? r.GetInt32() : 0;
                    YellowCount = root.TryGetProperty("Yellow", out var y) ? y.GetInt32() : 0;
                    GreenCount = root.TryGetProperty("Green", out var g) ? g.GetInt32() : 0;
                }
                catch { }

                // Load subtitle text (first‑party SRT, version 1)
                try
                {
                    SubtitleText = http.GetStringAsync($"{apiBase}/subtitles/{id}/1.srt").GetAwaiter().GetResult();
                }
                catch { SubtitleText = null; }

                // Load submitter info from local videos.json via VideoService
                try
                {
                    var entry = _videoService.GetById(id);
                    if (entry != null)
                    {
                        Submitter = entry.Submitter;
                        SubmissionDate = entry.SubmissionDate;
                    }
                }
                catch { }
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
