using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text;

namespace bwkt_webapp.Pages.Admin.CreatorMappings
{
    public class IndexModel : PageModel
    {
        public bool IsAdmin { get; private set; }
        public List<MappingItem> Items { get; private set; } = new();
        [BindProperty(SupportsGet = true)] public string? Query { get; set; }
        [BindProperty(SupportsGet = true)] public int PageNum { get; set; } = 1;
        [BindProperty(SupportsGet = true)] public int PageSize { get; set; } = 50;
        public int TotalCount { get; private set; }

        [BindProperty(SupportsGet = true)] public string? Try { get; set; }
        public string? TrySource => Try;
        public string? TryResult { get; private set; }

        public void OnGet()
        {
            IsAdmin = CheckIsAdmin();
            if (!IsAdmin) return;
            LoadPage();
            if (!string.IsNullOrWhiteSpace(Try))
            {
                TryResult = ResolveTry(Try!);
            }
        }

        public IActionResult OnPostDelete(string id)
        {
            IsAdmin = CheckIsAdmin();
            if (!IsAdmin) return StatusCode(403);
            var apiBase = DeriveApiBase(); if (string.IsNullOrWhiteSpace(apiBase)) return StatusCode(503);
            try
            {
                using var http = new HttpClient();
                var resp = http.DeleteAsync($"{apiBase}/admin/creators/mappings/{id}").GetAwaiter().GetResult();
                if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode);
            }
            catch { }
            return RedirectToPage("/Admin/CreatorMappings/Index", new { q = Query, page = PageNum, pageSize = PageSize });
        }

        public IActionResult OnPostReapply()
        {
            IsAdmin = CheckIsAdmin();
            if (!IsAdmin) return StatusCode(403);
            var apiBase = DeriveApiBase(); if (string.IsNullOrWhiteSpace(apiBase)) return StatusCode(503);
            try
            {
                using var http = new HttpClient();
                var resp = http.PostAsync($"{apiBase}/admin/creators/mappings/reapply", new StringContent("{}", System.Text.Encoding.UTF8, "application/json")).GetAwaiter().GetResult();
                // Ignore response details; user can check results via other UIs
                if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode);
            }
            catch { }
            return RedirectToPage("/Admin/CreatorMappings/Index", new { q = Query, page = PageNum, pageSize = PageSize });
        }

        private void LoadPage()
        {
            try
            {
                var apiBase = DeriveApiBase();
                if (string.IsNullOrWhiteSpace(apiBase)) return;
                using var http = new HttpClient();
                var url = $"{apiBase}/admin/creators/mappings?page={Math.Max(1, PageNum)}&pageSize={Math.Clamp(PageSize, 1, 200)}";
                if (!string.IsNullOrWhiteSpace(Query)) url += $"&q={Uri.EscapeDataString(Query!)}";
                var json = http.GetStringAsync(url).GetAwaiter().GetResult();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                TotalCount = root.TryGetProperty("totalCount", out var tc) ? tc.GetInt32() : 0;
                if (root.TryGetProperty("items", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in arr.EnumerateArray())
                    {
                        Items.Add(Parse(el));
                    }
                }
            }
            catch { }
        }

        private string? ResolveTry(string source)
        {
            // Local resolution using current page plus a quick fetch of first page with large pageSize
            var key = Normalize(source);
            var list = new List<MappingItem>(Items);
            try
            {
                var apiBase = DeriveApiBase();
                if (!string.IsNullOrWhiteSpace(apiBase))
                {
                    using var http = new HttpClient();
                    var json = http.GetStringAsync($"{apiBase}/admin/creators/mappings?page=1&pageSize=200").GetAwaiter().GetResult();
                    using var doc = JsonDocument.Parse(json);
                    var arr = doc.RootElement.GetProperty("items");
                    foreach (var el in arr.EnumerateArray()) list.Add(Parse(el));
                }
            }
            catch { }
            var hit = list.FirstOrDefault(m => m.SourceNormalized == key);
            return hit?.Canonical;
        }

        private static MappingItem Parse(JsonElement el)
        {
            return new MappingItem(
                el.GetProperty("id").GetString() ?? string.Empty,
                el.GetProperty("source").GetString() ?? string.Empty,
                el.TryGetProperty("source_normalized", out var sn) ? (sn.GetString() ?? string.Empty) : string.Empty,
                el.GetProperty("canonical").GetString() ?? string.Empty,
                el.TryGetProperty("notes", out var n) ? n.GetString() : null,
                el.TryGetProperty("updated_at", out var ua) ? ua.GetDateTimeOffset() : DateTimeOffset.MinValue
            );
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

        public static string Normalize(string input)
        {
            var nfkc = (input ?? string.Empty).Normalize(NormalizationForm.FormKC);
            var parts = nfkc.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", parts).Trim().ToLowerInvariant();
        }

        public record MappingItem(string Id, string Source, string SourceNormalized, string Canonical, string? Notes, DateTimeOffset UpdatedAt);
    }
}
