using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.RazorPages;
using bwkt_webapp.Models;
using bwkt_webapp.Services;

namespace bwkt_webapp.Pages;

public class IndexModel : PageModel
{
    private readonly IVideoService _videoService;
    public IEnumerable<VideoInfo> Videos { get; private set; } = new List<VideoInfo>();

    public int CurrentPage { get; private set; } = 1;
    public int PageSize { get; private set; } = 24;
    public int TotalCount { get; private set; } = 0;
    public int TotalPages { get; private set; } = 1;

    public IndexModel(IVideoService videoService)
    {
        _videoService = videoService;
    }

    public void OnGet(int pageNum = 1, int pageSize = 24)
    {
        // Clamp inputs
        CurrentPage = pageNum < 1 ? 1 : pageNum;
        PageSize = pageSize < 1 ? 1 : (pageSize > 100 ? 100 : pageSize);

        var (items, total) = _videoService.GetPaged(CurrentPage, PageSize);
        TotalCount = total;
        TotalPages = Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
        if (CurrentPage > TotalPages) CurrentPage = TotalPages;
        Videos = items;
    }
    // Ratings-based sorting is delegated to the Catalog API via sortBy=rating_desc
}
