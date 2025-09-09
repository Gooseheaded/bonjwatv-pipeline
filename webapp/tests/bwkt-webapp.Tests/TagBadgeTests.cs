using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using bwkt_webapp.Tests;
using Xunit;
using bwkt_webapp;

namespace bwkt_webapp.Tests
{
public class TagBadgeTests : IClassFixture<TestWebAppFactory>
    {
        private readonly TestWebAppFactory _factory;

        public TagBadgeTests(TestWebAppFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task IndexPage_ShowsConfiguredTagsAsBadges()
        {
            // Arrange: inject a fake video with tags z and p
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    var vs = services.FirstOrDefault(d => d.ServiceType == typeof(bwkt_webapp.Services.IVideoService));
                    if (vs != null) services.Remove(vs);
                    services.AddSingleton<bwkt_webapp.Services.IVideoService>(new TagBadgeFakeVideoService(
                        new [] { new bwkt_webapp.Models.VideoInfo { VideoId = "x1", Title = "X", SubtitleUrl = "u", Tags = new [] { "z", "p" } } }
                    ));
                });
            });
            var client = factory.CreateClient();
            var response = await client.GetAsync("/");
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync();

            // Assert: known tags for first video should render as badges
            Assert.Contains("<span class=\"badge bg-danger\">Zerg</span>", html);
            Assert.Contains("<span class=\"badge bg-warning\">Protoss</span>", html);
        }
    }
}
    
    public class TagBadgeMappingUnitTests
    {
        [Theory]
        [InlineData("zvz", "ZvZ")]
        [InlineData("zvp", "ZvP")]
        [InlineData("pvz", "PvZ")]
        [InlineData("pvt", "PvT")]
        [InlineData("pvp", "PvP")]
        [InlineData("tvz", "TvZ")]
        [InlineData("tvt", "TvT")]
        [InlineData("tvp", "TvP")]
        public void MatchupTags_MapToExpectedDisplay(string code, string expected)
        {
            var (text, _) = bwkt_webapp.Helpers.TagBadge.Get(code);
            Assert.Equal(expected, text);
        }
    }
namespace bwkt_webapp.Tests
{
    internal class TagBadgeFakeVideoService : bwkt_webapp.Services.IVideoService
    {
        private readonly IEnumerable<bwkt_webapp.Models.VideoInfo> _videos;
        public TagBadgeFakeVideoService(IEnumerable<bwkt_webapp.Models.VideoInfo> videos) { _videos = videos; }
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
