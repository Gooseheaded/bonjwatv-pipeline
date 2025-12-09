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
        public List<SubtitleVersionRow> SubtitleVersions { get; private set; } = new();
        public int CurrentSubtitleVersion { get; private set; }
        public List<string> AllowedTags { get; } = new() { "z","p","t","story","zvz","zvt","zvp","pvz","pvt","pvp","tvz","tvt","tvp" };
        public HashSet<string> SelectedTags { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
        public string? Info { get; private set; }

        public VideoModel(IVideoService videoService)
        {
            _videoService = videoService;
        }

        public void OnGet(string id)
        {
            IsAdmin = CheckIsAdmin();
            if (!IsAdmin) return;
            Load(id);
            try
            {
                var ok = Request.Query["ok"].FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(ok))
                {
                    Info = ok switch
                    {
                        "1" or "true" or "tags_saved" => "Tags saved successfully.",
                        "subtitle_promoted" => "Subtitle version promoted.",
                        "subtitle_deleted" => "Subtitle version deleted.",
                        _ => Info
                    };
                }
            }
            catch { }
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
                    if (Video.TryGetProperty("subtitleUrl", out var su) && su.ValueKind == JsonValueKind.String)
                    {
                        var parsed = TryParseSubtitleVersion(su.GetString());
                        if (parsed.HasValue) CurrentSubtitleVersion = parsed.Value;
                    }
                }
                catch { }
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

                // Load subtitle text (first-party SRT, current version fallback to v1)
                try
                {
                    var previewVersion = CurrentSubtitleVersion > 0 ? CurrentSubtitleVersion : 1;
                    SubtitleText = http.GetStringAsync($"{apiBase}/subtitles/{id}/{previewVersion}.srt").GetAwaiter().GetResult();
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

                // Load subtitle versions metadata
                try
                {
                    SubtitleVersions.Clear();
                    var versionsJson = http.GetStringAsync($"{apiBase}/admin/videos/{id}/subtitles").GetAwaiter().GetResult();
                    using var vdoc = JsonDocument.Parse(versionsJson);
                    var root = vdoc.RootElement;
                    CurrentSubtitleVersion = root.TryGetProperty("currentVersion", out var cv) ? cv.GetInt32() : 0;
                    if (root.TryGetProperty("items", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in arr.EnumerateArray())
                        {
                            var row = new SubtitleVersionRow
                            {
                                Version = item.TryGetProperty("version", out var vEl) ? vEl.GetInt32() : 0,
                                DisplayName = item.TryGetProperty("displayName", out var dnEl) ? dnEl.GetString() : null,
                                UserId = item.TryGetProperty("userId", out var uEl) ? uEl.GetString() : null,
                                SizeBytes = item.TryGetProperty("sizeBytes", out var szEl) ? szEl.GetInt64() : 0,
                                AddedLines = item.TryGetProperty("addedLines", out var addEl) ? addEl.GetInt32() : 0,
                                RemovedLines = item.TryGetProperty("removedLines", out var remEl) ? remEl.GetInt32() : 0,
                                IsCurrent = item.TryGetProperty("isCurrent", out var curEl) && curEl.GetBoolean()
                            };
                            if (item.TryGetProperty("submittedAt", out var atEl) && atEl.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(atEl.GetString(), out var ts))
                            {
                                row.SubmittedAt = ts;
                            }
                            SubtitleVersions.Add(row);
                        }
                        SubtitleVersions.Sort((a, b) => a.Version.CompareTo(b.Version));
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

        private static int? TryParseSubtitleVersion(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            try
            {
                var last = Path.GetFileName(url);
                if (string.IsNullOrWhiteSpace(last)) return null;
                if (last.EndsWith(".srt", StringComparison.OrdinalIgnoreCase))
                {
                    last = last[..^4];
                }
                if (last.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                {
                    last = last.Substring(1);
                }
                if (int.TryParse(last, out var ver) && ver > 0)
                {
                    return ver;
                }
            }
            catch { }
            return null;
        }
    }

    public class SubtitleVersionRow
    {
        public int Version { get; set; }
        public string? DisplayName { get; set; }
        public string? UserId { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public long SizeBytes { get; set; }
        public int AddedLines { get; set; }
        public int RemovedLines { get; set; }
        public bool IsCurrent { get; set; }

        public string ContributorLabel => !string.IsNullOrWhiteSpace(DisplayName) ? DisplayName! : (UserId ?? "(unknown)");
    }
}
