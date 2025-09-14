using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;

namespace bwkt_webapp.Pages.Admin.CreatorMappings
{
    public class EditModel : PageModel
    {
        public bool IsAdmin { get; private set; }
        [BindProperty(SupportsGet = true)] public string? Id { get; set; }
        [BindProperty(SupportsGet = true)] public string? Source { get; set; }
        [BindProperty(SupportsGet = true)] public string? Canonical { get; set; }
        [BindProperty(SupportsGet = true)] public string? Notes { get; set; }
        public string? Error { get; private set; }

        public void OnGet()
        {
            IsAdmin = CheckIsAdmin();
        }

        public IActionResult OnPost()
        {
            IsAdmin = CheckIsAdmin();
            if (!IsAdmin) return StatusCode(403);
            if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Source) || string.IsNullOrWhiteSpace(Canonical))
            {
                Error = "Id, Source and Canonical are required.";
                return Page();
            }
            var apiBase = DeriveApiBase(); if (string.IsNullOrWhiteSpace(apiBase)) return StatusCode(503);
            try
            {
                using var http = new HttpClient();
                var payload = System.Text.Json.JsonSerializer.Serialize(new { source = Source, canonical = Canonical, notes = Notes });
                using var msg = new HttpRequestMessage(HttpMethod.Put, $"{apiBase}/admin/creators/mappings/{Id}")
                {
                    Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
                };
                var resp = http.Send(msg);
                if ((int)resp.StatusCode == 409)
                {
                    Error = "A mapping for this source already exists.";
                    return Page();
                }
                if (!resp.IsSuccessStatusCode)
                {
                    Error = $"HTTP {(int)resp.StatusCode}";
                    return Page();
                }
                return RedirectToPage("/Admin/CreatorMappings/Index");
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                return Page();
            }
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
    }
}

