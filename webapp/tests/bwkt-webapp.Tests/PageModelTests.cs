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

        [Fact]
        public void LoginModel_OnGet_DoesNotThrow()
        {
            var model = new LoginModel();
            model.OnGet();
        }

        [Fact]
        public void SignupModel_OnGet_DoesNotThrow()
        {
            var model = new SignupModel();
            model.OnGet();
        }
    }
}
