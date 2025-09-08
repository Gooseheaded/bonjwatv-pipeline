using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace bwkt_webapp.Tests
{
    public class HomeSortByRatingsTests
    {
        private static string PrepareVideos()
        {
            var root = Path.Combine(Path.GetTempPath(), "bwkt-home-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "data"));
            var path = Path.Combine(root, "data", "videos.json");
            var items = new[]
            {
                new { v = "vidA", title = "Alpha", subtitleUrl = "u", tags = new [] { "z" } },
                new { v = "vidB", title = "Beta", subtitleUrl = "u", tags = new [] { "z" } },
                new { v = "vidC", title = "Gamma", subtitleUrl = "u", tags = new [] { "z" } },
            };
            File.WriteAllText(path, JsonSerializer.Serialize(items));
            return root;
        }

        [Fact]
        public async Task Homepage_Sorts_By_Ratings_Descending()
        {
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    // Replace ratings client with fake
                    var rc = services.FirstOrDefault(d => d.ServiceType == typeof(bwkt_webapp.Services.IRatingsClient));
                    if (rc != null) services.Remove(rc);
                    services.AddSingleton<bwkt_webapp.Services.IRatingsClient>(new FakeRatingsClient());
                    // Replace video service with fake data set
                    var vs = services.FirstOrDefault(d => d.ServiceType == typeof(bwkt_webapp.Services.IVideoService));
                    if (vs != null) services.Remove(vs);
                    services.AddSingleton<bwkt_webapp.Services.IVideoService>(new FakeVideoService());
                });
            });
                var client = factory.CreateClient();
                var res = await client.GetAsync("/");
                res.EnsureSuccessStatusCode();
                var html = await res.Content.ReadAsStringAsync();

                // Validate order in HTML: vidC first, then vidB, then vidA
                var idxC = html.IndexOf("img.youtube.com/vi/vidC/hqdefault.jpg", StringComparison.Ordinal);
                var idxB = html.IndexOf("img.youtube.com/vi/vidB/hqdefault.jpg", StringComparison.Ordinal);
                var idxA = html.IndexOf("img.youtube.com/vi/vidA/hqdefault.jpg", StringComparison.Ordinal);
                Assert.True(idxC >= 0 && idxB > idxC && idxA > idxB, $"Order wrong: C({idxC}) B({idxB}) A({idxA})\nHTML: {html}");
            Environment.SetEnvironmentVariable("DATA_CATALOG_URL", null);
            Environment.SetEnvironmentVariable("HOMEPAGE_RATINGS_BASE_URL", null);
            Environment.SetEnvironmentVariable("CATALOG_API_BASE_URL", null);
        }

        private class FakeRatingsClient : bwkt_webapp.Services.IRatingsClient
        {
            public (int Red, int Yellow, int Green) GetSummary(string videoId, int version = 1) => videoId switch
            {
                "vidC" => (0, 1, 5),
                "vidB" => (1, 1, 2),
                "vidA" => (2, 1, 0),
                _ => (0, 0, 0)
            };
        }
    }
}

namespace bwkt_webapp.Tests
{
    internal class FakeVideoService : bwkt_webapp.Services.IVideoService
    {
        private readonly bwkt_webapp.Models.VideoInfo[] _videos = new []
        {
            new bwkt_webapp.Models.VideoInfo { VideoId = "vidA", Title = "Alpha", SubtitleUrl = "u", Tags = new []{"z"} },
            new bwkt_webapp.Models.VideoInfo { VideoId = "vidB", Title = "Beta", SubtitleUrl = "u", Tags = new []{"z"} },
            new bwkt_webapp.Models.VideoInfo { VideoId = "vidC", Title = "Gamma", SubtitleUrl = "u", Tags = new []{"z"} },
        };
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
