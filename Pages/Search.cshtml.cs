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

        public SearchModel(IVideoService videoService)
        {
            _videoService = videoService;
        }

        public void OnGet(string q)
        {
            Query = q ?? string.Empty;
            Videos = _videoService.Search(Query);
        }
    }
}