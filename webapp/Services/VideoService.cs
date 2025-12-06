using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using bwkt_webapp.Models;
using bwkt_webapp.Helpers;
using Microsoft.AspNetCore.Hosting;
using System.IO;

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
                    var vidFromApi = el.GetProperty("id").GetString() ?? videoId;
                    var apiSubtitleUrl = el.TryGetProperty("subtitleUrl", out var su) ? su.GetString() : null;
                    var (proxyUrl, version) = BuildSubtitleProxy(vidFromApi, apiSubtitleUrl);
                    var v = new VideoInfo
                    {
                        VideoId = vidFromApi,
                        Title = el.GetProperty("title").GetString() ?? string.Empty,
                        Creator = el.TryGetProperty("creator", out var c) ? c.GetString() : null,
                        Description = el.TryGetProperty("description", out var d) ? d.GetString() : null,
                        Tags = el.TryGetProperty("tags", out var tg) && tg.ValueKind == JsonValueKind.Array
                            ? tg.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray()
                            : null,
                        SubtitleUrl = proxyUrl,
                        SubtitleVersion = version
                    };
                    if (el.TryGetProperty("subtitleContributors", out var contribEl) && contribEl.ValueKind == JsonValueKind.Array)
                    {
                        var list = new List<SubtitleContributorInfo>();
                        foreach (var ce in contribEl.EnumerateArray())
                        {
                            var info = new SubtitleContributorInfo
                            {
                                Version = ce.TryGetProperty("version", out var vEl) ? vEl.GetInt32() : 0,
                                UserId = ce.TryGetProperty("userId", out var uEl) ? uEl.GetString() : null,
                                DisplayName = ce.TryGetProperty("displayName", out var dnEl) ? dnEl.GetString() : null
                            };
                            if (ce.TryGetProperty("submittedAt", out var atEl) && atEl.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(atEl.GetString(), out var ts))
                            {
                                info.SubmittedAt = ts;
                            }
                            list.Add(info);
                        }
                        v.SubtitleContributors = list.OrderBy(c => c.Version).ToList();
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
                        || (!string.IsNullOrWhiteSpace(v.Creator) && v.Creator.Contains(token, StringComparison.OrdinalIgnoreCase))
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

        public (IEnumerable<VideoInfo> Items, int TotalCount) GetPaged(int page, int pageSize)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_catalogUrl))
                {
                    var url = BuildCatalogQuery(_catalogUrl!, query: string.Empty, race: null);
                    // Request ratings-based sort for homepage
                    url = AddOrReplaceQuery(url, "sortBy", "rating_desc");
                    url = AddOrReplaceQuery(url, "page", Math.Max(1, page).ToString());
                    url = AddOrReplaceQuery(url, "pageSize", Math.Clamp(pageSize, 1, 100).ToString());
                    var json = _httpClient.GetStringAsync(url).GetAwaiter().GetResult();
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    var items = new List<VideoInfo>();
                    if (root.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in itemsEl.EnumerateArray())
                        {
                            items.Add(new VideoInfo
                            {
                                VideoId = el.GetProperty("id").GetString() ?? string.Empty,
                                Title = el.GetProperty("title").GetString() ?? string.Empty,
                                Creator = el.TryGetProperty("creator", out var c) ? c.GetString() : null,
                                Description = el.TryGetProperty("description", out var d) ? d.GetString() : null,
                                Tags = el.TryGetProperty("tags", out var tg) && tg.ValueKind == JsonValueKind.Array
                                    ? tg.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray()
                                    : null,
                                SubtitleUrl = string.Empty,
                                Red = GetIntCaseInsensitive(el, "Red"),
                                Yellow = GetIntCaseInsensitive(el, "Yellow"),
                                Green = GetIntCaseInsensitive(el, "Green")
                            });
                        }
                    }
                    var total = root.TryGetProperty("totalCount", out var tc) ? tc.GetInt32() : items.Count;
                    return (items, total);
                }
            }
            catch { }
            // Local fallback
            var all = _videos;
            var totalLocal = all.Count;
            var slice = all.Skip((Math.Max(1, page) - 1) * Math.Clamp(pageSize, 1, 100)).Take(Math.Clamp(pageSize, 1, 100));
            return (slice, totalLocal);
        }

        public (IEnumerable<VideoInfo> Items, int TotalCount) SearchPaged(string query, string? race, int page, int pageSize)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_catalogUrl))
                {
                    var url = BuildCatalogQuery(_catalogUrl!, query, race);
                    url = AddOrReplaceQuery(url, "page", Math.Max(1, page).ToString());
                    url = AddOrReplaceQuery(url, "pageSize", Math.Clamp(pageSize, 1, 100).ToString());
                    var json = _httpClient.GetStringAsync(url).GetAwaiter().GetResult();
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    var items = new List<VideoInfo>();
                    if (root.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in itemsEl.EnumerateArray())
                        {
                            items.Add(new VideoInfo
                            {
                                VideoId = el.GetProperty("id").GetString() ?? string.Empty,
                                Title = el.GetProperty("title").GetString() ?? string.Empty,
                                Creator = el.TryGetProperty("creator", out var c) ? c.GetString() : null,
                                Description = el.TryGetProperty("description", out var d) ? d.GetString() : null,
                                Tags = el.TryGetProperty("tags", out var tg) && tg.ValueKind == JsonValueKind.Array
                                    ? tg.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray()
                                    : null,
                                SubtitleUrl = string.Empty,
                                Red = GetIntCaseInsensitive(el, "Red"),
                                Yellow = GetIntCaseInsensitive(el, "Yellow"),
                                Green = GetIntCaseInsensitive(el, "Green")
                            });
                        }
                    }
                    var total = root.TryGetProperty("totalCount", out var tc) ? tc.GetInt32() : items.Count;
                    return (items, total);
                }
            }
            catch { }
            // Local fallback
            var all = Search(query, race).ToList();
            var totalLocal = all.Count;
            var slice = all.Skip((Math.Max(1, page) - 1) * Math.Clamp(pageSize, 1, 100)).Take(Math.Clamp(pageSize, 1, 100));
            return (slice, totalLocal);
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

        private static (string Url, int Version) BuildSubtitleProxy(string videoId, string? apiSubtitleUrl)
        {
            var version = TryParseSubtitleVersion(apiSubtitleUrl) ?? 1;
            return ($"/subtitles/{videoId}/{version}.srt", version);
        }

        private static int? TryParseSubtitleVersion(string? apiSubtitleUrl)
        {
            if (string.IsNullOrWhiteSpace(apiSubtitleUrl)) return null;
            try
            {
                var file = Path.GetFileName(apiSubtitleUrl);
                if (string.IsNullOrWhiteSpace(file)) return null;
                var withoutExt = file.EndsWith(".srt", StringComparison.OrdinalIgnoreCase)
                    ? file.Substring(0, file.Length - 4)
                    : file;
                if (withoutExt.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                {
                    withoutExt = withoutExt.Substring(1);
                }
                if (int.TryParse(withoutExt, out var version) && version > 0)
                {
                    return version;
                }
            }
            catch { }
            return null;
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
                            SubtitleUrl = string.Empty,
                            Red = GetIntCaseInsensitive(el, "Red"),
                            Yellow = GetIntCaseInsensitive(el, "Yellow"),
                            Green = GetIntCaseInsensitive(el, "Green")
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

        private static int GetIntCaseInsensitive(JsonElement el, string name)
        {
            if (el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number) return v.GetInt32();
            // Try lower-camel variant (e.g., Red -> red)
            if (!string.IsNullOrEmpty(name))
            {
                var lower = char.ToLowerInvariant(name[0]) + name.Substring(1);
                if (el.TryGetProperty(lower, out var v2) && v2.ValueKind == JsonValueKind.Number) return v2.GetInt32();
            }
            return 0;
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
