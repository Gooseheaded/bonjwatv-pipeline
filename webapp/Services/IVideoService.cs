using bwkt_webapp.Models;
using System.Collections.Generic;

namespace bwkt_webapp.Services
{
    public interface IVideoService
    {
        IEnumerable<VideoInfo> GetAll();
        VideoInfo? GetById(string videoId);
        IEnumerable<VideoInfo> Search(string query);
    }
}