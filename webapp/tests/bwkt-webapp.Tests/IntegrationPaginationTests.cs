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
        private static string PrepareIsolatedContentRootWithVideos(int count)
        {
            var testDataRoot = Path.Combine(Path.GetTempPath(), "bwkt-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(testDataRoot, "data"));
            var path = Path.Combine(testDataRoot, "data", "videos.json");
            var items = Enumerable.Range(1, count).Select(i => new
            {
                v = $"vid{i:D3}",
                title = $"Video {i:D3}",
                description = "d",
                creator = "C",
                subtitleUrl = "http://example.com",
                tags = new[] { "z" }
            });
            var json = JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = false });
            File.WriteAllText(path, json, Encoding.UTF8);
            return testDataRoot;
        }

        [Fact]
        public async Task Index_Paginates_Items_Based_On_Query()
        {
            var contentRoot = PrepareIsolatedContentRootWithVideos(30);
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseContentRoot(contentRoot);
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
            var contentRoot = PrepareIsolatedContentRootWithVideos(25);
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseContentRoot(contentRoot);
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
