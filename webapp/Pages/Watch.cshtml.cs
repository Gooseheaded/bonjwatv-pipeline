using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using bwkt_webapp.Models;
using bwkt_webapp.Services;
using System.Net.Http;
using System.Text.Json;

namespace bwkt_webapp.Pages
{
    public class WatchModel : PageModel
    {
        private readonly IVideoService _videoService;
        public VideoInfo? Video { get; private set; }
        public int RedCount { get; private set; }
        public int YellowCount { get; private set; }
        public int GreenCount { get; private set; }
        public int SubtitleVersion { get; private set; } = 1;

        public WatchModel(IVideoService videoService)
        {
            _videoService = videoService;
        }

        public IActionResult OnGet(string v)
        {
            Video = _videoService.GetById(v);
            if (Video == null)
            {
                return NotFound();
            }
            SubtitleVersion = Video.SubtitleVersion;
            TryLoadRatings(Video.VideoId);
            return Page();
        }

        private void TryLoadRatings(string videoId)
        {
            try
            {
                var apiBase = DeriveApiBase();
                if (string.IsNullOrWhiteSpace(apiBase)) return;
                var url = $"{apiBase}/videos/{videoId}/ratings?version=1";
                using var http = new HttpClient();
                var json = http.GetStringAsync(url).GetAwaiter().GetResult();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                RedCount = root.TryGetProperty("Red", out var r) ? r.GetInt32() : 0;
                YellowCount = root.TryGetProperty("Yellow", out var y) ? y.GetInt32() : 0;
                GreenCount = root.TryGetProperty("Green", out var g) ? g.GetInt32() : 0;
            }
            catch
            {
                // leave counts as zero on failure
            }
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
