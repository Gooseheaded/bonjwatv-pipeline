using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Collections.Generic;
using System.Text.Json;

namespace bwkt_webapp.Pages.Admin
{
    public class SubmissionModel : PageModel
    {
        public bool IsAdmin { get; private set; }
        public JsonElement Submission { get; private set; }
        public string? SubtitleText { get; private set; }
        public bool? IsUpdate { get; private set; } = false; // default to New on failures
        public string? ExistingSubtitleText { get; private set; }
        public bool IsCorrection { get; private set; }
        public string? CorrectionVideoId { get; private set; }
        public int CorrectionVersion { get; private set; } = 1;
        public string? CorrectionNotes { get; private set; }
        public List<CorrectionCueModel> CorrectionCues { get; } = new();

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

                var submissionType = Submission.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : "video";
                IsCorrection = string.Equals(submissionType, "subtitle_correction", StringComparison.OrdinalIgnoreCase);
                if (IsCorrection)
                {
                    LoadCorrectionDetails(apiBase, http);
                    return;
                }

                LoadVideoSubmissionDetails(apiBase, http, id);
            }
            catch { IsUpdate = false; }
        }

        private void LoadCorrectionDetails(string apiBase, HttpClient http)
        {
            try
            {
                var corr = Submission.TryGetProperty("correction", out var corrEl) ? corrEl : default;
                if (corr.ValueKind != JsonValueKind.Object) return;
                CorrectionVideoId = corr.TryGetProperty("video_id", out var vidEl) ? vidEl.GetString() : null;
                CorrectionVersion = corr.TryGetProperty("subtitle_version", out var verEl) ? verEl.GetInt32() : 1;
                CorrectionNotes = corr.TryGetProperty("notes", out var notesEl) ? notesEl.GetString() : null;
                if (corr.TryGetProperty("cues", out var cuesEl) && cuesEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var cue in cuesEl.EnumerateArray())
                    {
                        var seq = cue.TryGetProperty("sequence", out var seqEl) ? seqEl.GetInt32() : 0;
                        var start = cue.TryGetProperty("start_seconds", out var stEl) ? stEl.GetDouble() : 0;
                        var end = cue.TryGetProperty("end_seconds", out var enEl) ? enEl.GetDouble() : 0;
                        var original = cue.TryGetProperty("original_text", out var oEl) ? oEl.GetString() ?? string.Empty : string.Empty;
                        var updated = cue.TryGetProperty("updated_text", out var uEl) ? uEl.GetString() ?? string.Empty : string.Empty;
                        CorrectionCues.Add(new CorrectionCueModel(seq, start, end, original, updated));
                    }
                }
                if (!string.IsNullOrWhiteSpace(CorrectionVideoId))
                {
                    try
                    {
                        SubtitleText = http.GetStringAsync($"{apiBase}/subtitles/{CorrectionVideoId}/{CorrectionVersion}.srt").GetAwaiter().GetResult();
                    }
                    catch { SubtitleText = null; }
                }
            }
            catch { }
        }

        private void LoadVideoSubmissionDetails(string apiBase, HttpClient http, string id)
        {
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

            try
            {
                try
                {
                    var previewUrl = $"{apiBase}/admin/submissions/{id}/subtitle";
                    var text = http.GetStringAsync(previewUrl).GetAwaiter().GetResult();
                    if (!string.IsNullOrWhiteSpace(text)) { SubtitleText = text; }
                }
                catch { }

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

            try
            {
                if (IsUpdate == true && !string.IsNullOrWhiteSpace(vid))
                {
                    try
                    {
                        var vjson = http.GetStringAsync($"{apiBase}/videos/{vid}").GetAwaiter().GetResult();
                        using var vdoc = JsonDocument.Parse(vjson);
                        var root = vdoc.RootElement;
                        var su = root.TryGetProperty("subtitleUrl", out var suEl) ? suEl.GetString() : null;
                        if (!string.IsNullOrWhiteSpace(su))
                        {
                            ExistingSubtitleText = http.GetStringAsync(su!).GetAwaiter().GetResult();
                        }
                    }
                    catch { }

                    if (string.IsNullOrWhiteSpace(ExistingSubtitleText))
                    {
                        try
                        {
                            ExistingSubtitleText = http.GetStringAsync($"{apiBase}/subtitles/{vid}/1.srt").GetAwaiter().GetResult();
                        }
                        catch { ExistingSubtitleText = null; }
                    }
                }
            }
            catch { ExistingSubtitleText = null; }
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
        public record CorrectionCueModel(int Sequence, double StartSeconds, double EndSeconds, string OriginalText, string UpdatedText);
    }
}
