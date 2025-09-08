using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using bwkt_webapp.Models;
using bwkt_webapp.Helpers;
using Microsoft.AspNetCore.Hosting;

namespace bwkt_webapp.Services
{
    public class VideoService : IVideoService, IDisposable
    {
        private readonly List<VideoInfo> _videos = new List<VideoInfo>();
        private readonly HttpClient _httpClient = new HttpClient();
        private readonly string? _catalogUrl;

        public VideoService(IWebHostEnvironment env)
        {
            _catalogUrl = Environment.GetEnvironmentVariable("DATA_CATALOG_URL");
        }

        public IEnumerable<VideoInfo> GetAll() => _videos;

        public IEnumerable<VideoInfo> GetAll()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_catalogUrl))
                {
                    // Fetch all pages from Catalog API and return
                    var url = BuildCatalogQuery(_catalogUrl!, query: string.Empty, race: null);
                    var data = FetchCatalogAllPages(url).ToList();
                    if (data.Count > 0)
                    {
                        return data;
                    }
                }
            }
            catch { }
            return _videos;
        }

        public VideoInfo? GetById(string videoId)
        {
            // Try local first only if API is not configured
            if (string.IsNullOrWhiteSpace(_catalogUrl))
            {
                return _videos.FirstOrDefault(v => string.Equals(v.VideoId, videoId, StringComparison.OrdinalIgnoreCase));
            }

            // API-first lookup
            try
            {
                var apiBase = DeriveApiBase(_catalogUrl!);
                if (!string.IsNullOrWhiteSpace(apiBase))
                {
                    var url = $"{apiBase}/videos/{videoId}";
                    var json = _httpClient.GetStringAsync(url).GetAwaiter().GetResult();
                    using var doc = JsonDocument.Parse(json);
                    var el = doc.RootElement;
                    // Map API fields into VideoInfo
                    var v = new VideoInfo
                    {
                        VideoId = el.GetProperty("id").GetString() ?? videoId,
                        Title = el.GetProperty("title").GetString() ?? string.Empty,
                        Creator = el.TryGetProperty("creator", out var c) ? c.GetString() : null,
                        Description = el.TryGetProperty("description", out var d) ? d.GetString() : null,
                        Tags = el.TryGetProperty("tags", out var tg) && tg.ValueKind == JsonValueKind.Array
                            ? tg.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray()
                            : null,
                        SubtitleUrl = el.TryGetProperty("subtitleUrl", out var su) ? (su.GetString() ?? string.Empty) : string.Empty
                    };
                    // Default first-party subtitle path if missing
                    if (string.IsNullOrWhiteSpace(v.SubtitleUrl))
                    {
                        v.SubtitleUrl = $"{apiBase}/subtitles/{v.VideoId}/1.srt";
                    }
                    return v;
                }
            }
            catch { }

            // Fallback to local cache
            return _videos.FirstOrDefault(v => string.Equals(v.VideoId, videoId, StringComparison.OrdinalIgnoreCase));
        }

        public IEnumerable<VideoInfo> Search(string query)
        {
            return Search(query, null);
        }

        public IEnumerable<VideoInfo> Search(string query, string? race)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_catalogUrl))
                {
                    var url = BuildCatalogQuery(_catalogUrl!, query, race);
                    var data = FetchCatalogAllPages(url).ToList();
                    // Defensive: if API returns 0 results (e.g., empty catalog on first deploy),
                    // fall back to local search if we have local data.
                    if (data.Count > 0 || !_videos.Any())
                    {
                        return data;
                    }
                    // else: fall through to local search
                }
            }
            catch
            {
                // fall back to local search
            }

            // Local search fallback
            IEnumerable<VideoInfo> baseQuery = _videos;
            if (!string.IsNullOrWhiteSpace(query))
            {
                var tokens = query.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                baseQuery = baseQuery.Where(v =>
                    tokens.All(token =>
                        v.Title.Contains(token, StringComparison.OrdinalIgnoreCase)
                        || (v.Tags?.Any(tag =>
                            string.Equals(tag, token, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(TagBadge.Get(tag).Text, token, StringComparison.OrdinalIgnoreCase)
                        ) ?? false)
                    )
                );
            }
            var r = NormalizeRace(race);
            if (r != null)
            {
                baseQuery = baseQuery.Where(v => v.Tags != null && v.Tags.Any(t => string.Equals(t, r, StringComparison.OrdinalIgnoreCase)));
            }
            return baseQuery;
        }

        private static string? NormalizeRace(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            switch (value.Trim().ToLowerInvariant())
            {
                case "z": case "zerg": return "z";
                case "t": case "terran": return "t";
                case "p": case "protoss": return "p";
                default: return null;
            }
        }

        private static string BuildCatalogQuery(string baseUrl, string query, string? race)
        {
            var uri = new Uri(baseUrl);
            var qb = System.Web.HttpUtility.ParseQueryString(uri.Query);
            if (!string.IsNullOrWhiteSpace(query)) qb.Set("q", query);
            var r = NormalizeRace(race);
            if (r != null) qb.Set("race", r);
            qb.Set("pageSize", "100");
            var builder = new UriBuilder(uri) { Query = qb.ToString() };
            return builder.ToString();
        }

        private IEnumerable<VideoInfo> FetchCatalogAllPages(string url)
        {
            var list = new List<VideoInfo>();
            int page = 1;
            while (true)
            {
                var pageUrl = AddOrReplaceQuery(url, "page", page.ToString());
                var json = _httpClient.GetStringAsync(pageUrl).GetAwaiter().GetResult();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
                {
                    int count = 0;
                    foreach (var el in itemsEl.EnumerateArray())
                    {
                        list.Add(new VideoInfo
                        {
                            VideoId = el.GetProperty("id").GetString() ?? string.Empty,
                            Title = el.GetProperty("title").GetString() ?? string.Empty,
                            Creator = el.TryGetProperty("creator", out var c) ? c.GetString() : null,
                            Description = el.TryGetProperty("description", out var d) ? d.GetString() : null,
                            Tags = el.TryGetProperty("tags", out var tg) && tg.ValueKind == JsonValueKind.Array
                                ? tg.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray()
                                : null,
                            SubtitleUrl = string.Empty
                        });
                        count++;
                    }
                    if (count == 0) break;
                }
                else break;

                var total = root.TryGetProperty("totalCount", out var tc) ? tc.GetInt32() : list.Count;
                if (list.Count >= total) break;
                page++;
                if (page > 1000) break; // safety
            }
            return list;
        }

        private static string? DeriveApiBase(string apiVideosUrl)
        {
            try
            {
                var uri = new Uri(apiVideosUrl);
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

        private static string AddOrReplaceQuery(string url, string key, string value)
        {
            var uri = new Uri(url);
            var qb = System.Web.HttpUtility.ParseQueryString(uri.Query);
            qb.Set(key, value);
            var builder = new UriBuilder(uri) { Query = qb.ToString() };
            return builder.ToString();
        }

        public void Dispose() { }
    }
}
