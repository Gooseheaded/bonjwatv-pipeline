using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using bwkt_webapp;
using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace bwkt_webapp.Tests
{
    public class IntegrationPaginationTests
    {
        private static bwkt_webapp.Models.VideoInfo[] MakeVideos(int count) =>
            Enumerable.Range(1, count)
                .Select(i => new bwkt_webapp.Models.VideoInfo { VideoId = $"vid{i:D3}", Title = $"Video {i:D3}", SubtitleUrl = "http://example.com", Creator = "C", Tags = new []{"z"} })
                .ToArray();

        [Fact]
        public async Task Index_Paginates_Items_Based_On_Query()
        {
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    var vs = services.FirstOrDefault(d => d.ServiceType == typeof(bwkt_webapp.Services.IVideoService));
                    if (vs != null) services.Remove(vs);
                    services.AddSingleton<bwkt_webapp.Services.IVideoService>(new ParamFakeVideoService(MakeVideos(30)));
                });
            });
            var client = factory.CreateClient();

            var res1 = await client.GetAsync("/?pageNum=1&pageSize=12");
            res1.EnsureSuccessStatusCode();
            var html1 = await res1.Content.ReadAsStringAsync();
            Assert.Contains("img.youtube.com/vi/vid001/hqdefault.jpg", html1);
            Assert.Contains("img.youtube.com/vi/vid012/hqdefault.jpg", html1);
            Assert.DoesNotContain("img.youtube.com/vi/vid013/hqdefault.jpg", html1);

            var res2 = await client.GetAsync("/?pageNum=2&pageSize=12");
            res2.EnsureSuccessStatusCode();
            var html2 = await res2.Content.ReadAsStringAsync();
            Assert.Contains("img.youtube.com/vi/vid013/hqdefault.jpg", html2);
            Assert.Contains("img.youtube.com/vi/vid024/hqdefault.jpg", html2);
            Assert.DoesNotContain("img.youtube.com/vi/vid001/hqdefault.jpg", html2);
        }

        [Fact]
        public async Task Search_Paginates_And_Preserves_Query()
        {
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    var vs = services.FirstOrDefault(d => d.ServiceType == typeof(bwkt_webapp.Services.IVideoService));
                    if (vs != null) services.Remove(vs);
                    services.AddSingleton<bwkt_webapp.Services.IVideoService>(new ParamFakeVideoService(MakeVideos(25)));
                });
            });
            var client = factory.CreateClient();

            var res = await client.GetAsync("/search?q=Video&pageNum=3&pageSize=10");
            res.EnsureSuccessStatusCode();
            var html = await res.Content.ReadAsStringAsync();
            // Page 3 with size 10 should show 21-25
            Assert.Contains("img.youtube.com/vi/vid021/hqdefault.jpg", html);
            Assert.Contains("img.youtube.com/vi/vid025/hqdefault.jpg", html);
            Assert.DoesNotContain("img.youtube.com/vi/vid001/hqdefault.jpg", html);
            Assert.DoesNotContain("img.youtube.com/vi/vid011/hqdefault.jpg", html);
        }
    }
}

namespace bwkt_webapp.Tests
{
    internal class ParamFakeVideoService : bwkt_webapp.Services.IVideoService
    {
        private readonly IEnumerable<bwkt_webapp.Models.VideoInfo> _videos;
        public ParamFakeVideoService(IEnumerable<bwkt_webapp.Models.VideoInfo> videos) { _videos = videos; }
        public IEnumerable<bwkt_webapp.Models.VideoInfo> GetAll() => _videos;
        public bwkt_webapp.Models.VideoInfo? GetById(string videoId) => _videos.FirstOrDefault(v => v.VideoId == videoId);
        public IEnumerable<bwkt_webapp.Models.VideoInfo> Search(string query) => _videos;
        public IEnumerable<bwkt_webapp.Models.VideoInfo> Search(string query, string? race) => _videos;
        public (IEnumerable<bwkt_webapp.Models.VideoInfo> Items, int TotalCount) GetPaged(int page, int pageSize)
        {
            var list = _videos.ToList();
            return (list.Skip((Math.Max(1,page)-1)*pageSize).Take(pageSize), list.Count);
        }
        public (IEnumerable<bwkt_webapp.Models.VideoInfo> Items, int TotalCount) SearchPaged(string query, string? race, int page, int pageSize)
        {
            var list = _videos.ToList();
            return (list.Skip((Math.Max(1,page)-1)*pageSize).Take(pageSize), list.Count);
        }
    }
}
