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
            var contentRoot = PrepareVideos();

            try
            {
                await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
                {
                    builder.UseContentRoot(contentRoot);
                    builder.ConfigureServices(services =>
                    {
                        // Remove existing IRatingsClient registration, then inject a fake
                        var toRemove = services.FirstOrDefault(d => d.ServiceType == typeof(bwkt_webapp.Services.IRatingsClient));
                        if (toRemove != null) services.Remove(toRemove);
                        services.AddSingleton<bwkt_webapp.Services.IRatingsClient>(new FakeRatingsClient());
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
            }
            finally
            {
                Environment.SetEnvironmentVariable("DATA_CATALOG_URL", null);
                Environment.SetEnvironmentVariable("HOMEPAGE_RATINGS_BASE_URL", null);
                Environment.SetEnvironmentVariable("CATALOG_API_BASE_URL", null);
            }
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
