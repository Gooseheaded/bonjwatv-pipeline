using System;
using System.Collections.Generic;
using System.Linq;
using bwkt_webapp.Models;
using bwkt_webapp.Pages;
using bwkt_webapp.Pages.Account;
using bwkt_webapp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Xunit;

namespace bwkt_webapp.Tests
{
    public class PageModelTests
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
            public IEnumerable<VideoInfo> Search(string query, string? race)
                => Search(query);
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

        private List<VideoInfo> SampleVideos => new List<VideoInfo>
        {
            new VideoInfo { VideoId = "a", Title = "Alpha", SubtitleUrl = "u1" },
            new VideoInfo { VideoId = "b", Title = "Beta", SubtitleUrl = "u2" }
        };

        [Fact]
        public void IndexModel_OnGet_PopulatesVideos()
        {
            var svc = new FakeService(SampleVideos);
            var model = new IndexModel(svc);
            model.OnGet();
            Assert.Equal(2, model.Videos.Count());
        }

        [Fact]
        public void SearchModel_OnGet_FiltersVideos()
        {
            var svc = new FakeService(SampleVideos);
            var model = new SearchModel(svc);
            model.OnGet("alpha");
            Assert.Single(model.Videos);
            Assert.Equal("a", model.Videos.First().VideoId);
        }

        [Fact]
        public void WatchModel_OnGet_ReturnsPage_ForValidId()
        {
            var svc = new FakeService(SampleVideos);
            var model = new WatchModel(svc);
            var result = model.OnGet("b");
            Assert.IsType<PageResult>(result);
            Assert.Equal("b", model.Video?.VideoId);
        }

        [Fact]
        public void WatchModel_OnGet_ReturnsNotFound_ForInvalidId()
        {
            var svc = new FakeService(SampleVideos);
            var model = new WatchModel(svc);
            var result = model.OnGet("x");
            Assert.IsType<NotFoundResult>(result);
        }

        // Removed legacy Account page tests: Login/Signup pages are not present in current webapp.

        [Fact]
        public void SearchModel_OnGet_PassesRaceToService()
        {
            string? capturedRace = null;
            var svc = new CapturingService(SampleVideos, r => capturedRace = r);
            var model = new SearchModel(svc);
            model.OnGet("", "Zerg");
            Assert.Equal("z", capturedRace);
        }

        private class CapturingService : IVideoService
        {
            private readonly List<VideoInfo> _videos;
            private readonly Action<string?> _onRace;
            public CapturingService(IEnumerable<VideoInfo> videos, Action<string?> onRace)
            { _videos = videos.ToList(); _onRace = onRace; }
            public IEnumerable<VideoInfo> GetAll() => _videos;
            public VideoInfo? GetById(string videoId) => _videos.FirstOrDefault(v => v.VideoId == videoId);
            public IEnumerable<VideoInfo> Search(string query) => _videos;
            public IEnumerable<VideoInfo> Search(string query, string? race)
            { _onRace(race); return _videos; }
            public (IEnumerable<VideoInfo> Items, int TotalCount) GetPaged(int page, int pageSize)
            {
                var total = _videos.Count;
                var items = _videos.Skip((Math.Max(1,page)-1)*pageSize).Take(pageSize);
                return (items, total);
            }
            public (IEnumerable<VideoInfo> Items, int TotalCount) SearchPaged(string query, string? race, int page, int pageSize)
            {
                _onRace(race);
                var list = _videos.ToList();
                var items = list.Skip((Math.Max(1,page)-1)*pageSize).Take(pageSize);
                return (items, list.Count);
            }
        }

        private class DummyRatings : bwkt_webapp.Services.IRatingsClient
        {
            public (int Red, int Yellow, int Green) GetSummary(string videoId, int version = 1) => (0, 0, 0);
        }
    }
}
