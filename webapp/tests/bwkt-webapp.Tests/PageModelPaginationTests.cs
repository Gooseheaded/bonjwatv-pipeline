using System;
using System.Collections.Generic;
using System.Linq;
using bwkt_webapp.Models;
using bwkt_webapp.Pages;
using bwkt_webapp.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Xunit;

namespace bwkt_webapp.Tests
{
    public partial class PageModelPaginationTests
    {
        private class FakeService : IVideoService
        {
            private readonly List<VideoInfo> _videos;
            public FakeService(IEnumerable<VideoInfo> videos) => _videos = videos.ToList();
            public IEnumerable<VideoInfo> GetAll() => _videos;
            public VideoInfo? GetById(string videoId) => _videos.FirstOrDefault(v => v.VideoId == videoId);
            public IEnumerable<VideoInfo> Search(string query) => string.IsNullOrWhiteSpace(query)
                ? _videos
                : _videos.Where(v => v.Title.Contains(query, StringComparison.OrdinalIgnoreCase));
            public IEnumerable<VideoInfo> Search(string query, string? race) => Search(query);
            public (IEnumerable<VideoInfo> Items, int TotalCount) GetPaged(int page, int pageSize)
            {
                var total = _videos.Count;
                var items = _videos.Skip((Math.Max(1,page)-1)*pageSize).Take(pageSize);
                return (items, total);
            }
            public (IEnumerable<VideoInfo> Items, int TotalCount) SearchPaged(string query, string? race, int page, int pageSize)
            {
                var list = Search(query, race).ToList();
                var items = list.Skip((Math.Max(1,page)-1)*pageSize).Take(pageSize);
                return (items, list.Count);
            }
        }

        private static IEnumerable<VideoInfo> MakeVideos(int count)
        {
            for (int i = 1; i <= count; i++)
            {
                yield return new VideoInfo { VideoId = $"vid{i:D3}", Title = $"Video {i:D3}", SubtitleUrl = "u" };
            }
        }

        [Fact]
        public void IndexModel_Paginates_Items_Correctly()
        {
            var svc = new FakeService(MakeVideos(50));
            var model = new IndexModel(svc, new DummyRatings());
            model.OnGet(pageNum: 2, pageSize: 10);
            Assert.Equal(2, model.CurrentPage);
            Assert.Equal(10, model.PageSize);
            Assert.Equal(50, model.TotalCount);
            Assert.Equal(5, model.TotalPages);
            var vids = model.Videos.Select(v => v.VideoId).ToList();
            Assert.Equal("vid011", vids.First());
            Assert.Equal("vid020", vids.Last());
        }

        [Fact]
        public void SearchModel_Paginates_Items_Correctly()
        {
            var svc = new FakeService(MakeVideos(30));
            var model = new SearchModel(svc);
            model.OnGet("Video", pageNum: 3, pageSize: 7);
            Assert.Equal(3, model.CurrentPage);
            Assert.Equal(7, model.PageSize);
            Assert.Equal(30, model.TotalCount);
            Assert.Equal(5, model.TotalPages);
            var vids = model.Videos.Select(v => v.VideoId).ToList();
            Assert.Equal("vid015", vids.First());
            Assert.Equal("vid021", vids.Last());
        }
    }
}

namespace bwkt_webapp.Tests
{
    public partial class PageModelPaginationTests
    {
        private class DummyRatings : bwkt_webapp.Services.IRatingsClient
        {
            public (int Red, int Yellow, int Green) GetSummary(string videoId, int version = 1) => (0, 0, 0);
        }
    }
}
