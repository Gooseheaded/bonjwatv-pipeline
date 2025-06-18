using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.RazorPages;
using bwkt_webapp.Models;
using bwkt_webapp.Services;

namespace bwkt_webapp.Pages;

public class IndexModel : PageModel
{
    private readonly IVideoService _videoService;
    public IEnumerable<VideoInfo> Videos { get; private set; } = new List<VideoInfo>();

    public IndexModel(IVideoService videoService)
    {
        _videoService = videoService;
    }

    public void OnGet()
    {
        Videos = _videoService.GetAll();
    }
}
