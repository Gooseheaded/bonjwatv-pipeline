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
            var client = _factory.CreateClient();
            var response = await client.GetAsync("/");
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync();

            // Should display our test data entry
            Assert.Contains("Test Video", html);
            Assert.Contains("img.youtube.com/vi/test1/hqdefault.jpg", html);
        }
    }
}