using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using bwkt_webapp;

namespace bwkt_webapp.Tests
{
public class HomePageTests : IClassFixture<TestWebAppFactory>
    {
        private readonly TestWebAppFactory _factory;

        public HomePageTests(TestWebAppFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task IndexPage_DisplaysAllVideos()
        {
            await using var factory = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    var vs = services.FirstOrDefault(d => d.ServiceType == typeof(bwkt_webapp.Services.IVideoService));
                    if (vs != null) services.Remove(vs);
                    services.AddSingleton<bwkt_webapp.Services.IVideoService>(new FakeVideoService(
                        new [] { new bwkt_webapp.Models.VideoInfo { VideoId = "test1", Title = "Test Video", SubtitleUrl = "http://example.com", Creator = "Test Creator", Tags = new []{"z","p"} } }
                    ));
                });
            });
            var client = factory.CreateClient();
            var response = await client.GetAsync("/");
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync();

            // Should display our test data entry
            Assert.Contains("Test Video", html);
            Assert.Contains("img.youtube.com/vi/test1/hqdefault.jpg", html);
            Assert.Contains("By Test Creator", html);
        }
    }
}
namespace bwkt_webapp.Tests
{
    internal class FakeVideoService : bwkt_webapp.Services.IVideoService
    {
        private readonly IEnumerable<bwkt_webapp.Models.VideoInfo> _videos;
        public FakeVideoService(IEnumerable<bwkt_webapp.Models.VideoInfo> videos) { _videos = videos; }
        public IEnumerable<bwkt_webapp.Models.VideoInfo> GetAll() => _videos;
        public bwkt_webapp.Models.VideoInfo? GetById(string videoId) => _videos.FirstOrDefault(v => v.VideoId == videoId);
        public IEnumerable<bwkt_webapp.Models.VideoInfo> Search(string query) => _videos;
        public IEnumerable<bwkt_webapp.Models.VideoInfo> Search(string query, string? race) => _videos;
    }
}
