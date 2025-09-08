using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.RazorPages;
using bwkt_webapp.Models;
using bwkt_webapp.Services;

namespace bwkt_webapp.Pages;

public class IndexModel : PageModel
{
    private readonly IVideoService _videoService;
    private readonly IRatingsClient _ratings;
    public IEnumerable<VideoInfo> Videos { get; private set; } = new List<VideoInfo>();

    public int CurrentPage { get; private set; } = 1;
    public int PageSize { get; private set; } = 24;
    public int TotalCount { get; private set; } = 0;
    public int TotalPages { get; private set; } = 1;

    public IndexModel(IVideoService videoService, IRatingsClient ratings)
    {
        _videoService = videoService;
        _ratings = ratings;
    }

    public void OnGet(int pageNum = 1, int pageSize = 24)
    {
        // Clamp inputs
        CurrentPage = pageNum < 1 ? 1 : pageNum;
        PageSize = pageSize < 1 ? 1 : (pageSize > 100 ? 100 : pageSize);

        var (items, total) = _videoService.GetPaged(CurrentPage, PageSize);
        // Sort the returned page by ratings score for a stable UX
        var scoredPage = ScoreAndSort(items.ToList());
        TotalCount = total;
        TotalPages = Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
        if (CurrentPage > TotalPages) CurrentPage = TotalPages;
        Videos = scoredPage;
    }

    private IEnumerable<VideoInfo> ScoreAndSort(List<VideoInfo> videos)
    {
        var scores = new Dictionary<string, (double score, int total, int red, int yellow, int green)>();
        foreach (var v in videos)
        {
            var (r, y, g) = _ratings.GetSummary(v.VideoId, 1);
            int n = Math.Max(0, r + y + g);
            var s = ComputeScore(r, y, g);
            scores[v.VideoId] = (s, n, r, y, g);
        }

        return videos.OrderByDescending(v => scores.TryGetValue(v.VideoId, out var t) ? t.score : -1)
                      .ThenByDescending(v => scores.TryGetValue(v.VideoId, out var t) ? t.total : 0)
                      .ThenBy(v => v.Title);
    }

    private static double ComputeScore(int red, int yellow, int green)
    {
        int n = Math.Max(0, red + yellow + green);
        if (n <= 0) return 0;
        // Treat Green as positive, Yellow as half-positive
        double pos = green + 0.5 * yellow;
        double p = pos / n;
        // Wilson lower bound for binomial proportion (approx.), z ~ 1.96 (95% confidence)
        double z = 1.96;
        double z2 = z * z;
        double denom = 1 + z2 / n;
        double centre = p + z2 / (2 * n);
        double adj = z * Math.Sqrt((p * (1 - p) + z2 / (4 * n)) / n);
        double lb = (centre - adj) / denom;
        return lb;
    }
}
