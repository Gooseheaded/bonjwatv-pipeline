using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using bwkt_webapp.Models;
using bwkt_webapp.Services;

namespace bwkt_webapp.Pages
{
    public class WatchModel : PageModel
    {
        private readonly IVideoService _videoService;
        public VideoInfo? Video { get; private set; }

        public WatchModel(IVideoService videoService)
        {
            _videoService = videoService;
        }

        public IActionResult OnGet(string videoId)
        {
            Video = _videoService.GetById(videoId);
            if (Video == null)
            {
                return NotFound();
            }
            return Page();
        }
    }
}