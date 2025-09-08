using bwkt_webapp.Models;
using System.Collections.Generic;

namespace bwkt_webapp.Services
{
    public interface IVideoService
    {
        IEnumerable<VideoInfo> GetAll();
        VideoInfo? GetById(string videoId);
        IEnumerable<VideoInfo> Search(string query);
        IEnumerable<VideoInfo> Search(string query, string? race);
        (IEnumerable<VideoInfo> Items, int TotalCount) GetPaged(int page, int pageSize);
        (IEnumerable<VideoInfo> Items, int TotalCount) SearchPaged(string query, string? race, int page, int pageSize);
    }
}
