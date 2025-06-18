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

        public IActionResult OnGet(string v)
        {
            Video = _videoService.GetById(v);
            if (Video == null)
            {
                return NotFound();
            }
            return Page();
        }
    }
}