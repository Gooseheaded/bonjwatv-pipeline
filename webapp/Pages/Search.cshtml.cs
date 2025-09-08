using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.RazorPages;
using bwkt_webapp.Models;
using bwkt_webapp.Services;

namespace bwkt_webapp.Pages
{
    public class SearchModel : PageModel
    {
        private readonly IVideoService _videoService;
        public string Query { get; private set; } = string.Empty;
        public IEnumerable<VideoInfo> Videos { get; private set; } = new List<VideoInfo>();
        public string SelectedRace { get; private set; } = "all"; // all | z | t | p

        public int CurrentPage { get; private set; } = 1;
        public int PageSize { get; private set; } = 24;
        public int TotalCount { get; private set; } = 0;
        public int TotalPages { get; private set; } = 1;

        public SearchModel(IVideoService videoService)
        {
            _videoService = videoService;
        }

        public void OnGet(string q, string? race = null, int pageNum = 1, int pageSize = 24)
        {
            Query = q ?? string.Empty;
            CurrentPage = pageNum < 1 ? 1 : pageNum;
            PageSize = pageSize < 1 ? 1 : (pageSize > 100 ? 100 : pageSize);
            // Determine race preference: query param > cookie > default("all")
            var cookieRace = HttpContext != null ? Request.Cookies["race"] : null;
            SelectedRace = string.IsNullOrWhiteSpace(race) ? (string.IsNullOrWhiteSpace(cookieRace) ? "all" : cookieRace!) : race!;
            SelectedRace = NormalizeRace(SelectedRace);

            // Persist preference for future searches (when HTTP context is available)
            if (HttpContext != null)
            {
                Response.Cookies.Append("race", SelectedRace, new Microsoft.AspNetCore.Http.CookieOptions
                {
                    HttpOnly = false,
                    IsEssential = true,
                    SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax,
                    Expires = DateTimeOffset.UtcNow.AddDays(180)
                });
            }

            var raceParam = SelectedRace == "all" ? null : SelectedRace;
            var (items, total) = _videoService.SearchPaged(Query, raceParam, CurrentPage, PageSize);
            TotalCount = total;
            TotalPages = Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
            if (CurrentPage > TotalPages) CurrentPage = TotalPages;
            Videos = items;
        }

        private static string NormalizeRace(string value)
        {
            return value?.Trim().ToLowerInvariant() switch
            {
                "z" or "zerg" => "z",
                "t" or "terran" => "t",
                "p" or "protoss" => "p",
                _ => "all"
            };
        }

        // Race filtering is applied in the service when using the Catalog API.
    }
}
