using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using bwkt_webapp;

namespace bwkt_webapp.Tests
{
    public class HomePageTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public HomePageTests(WebApplicationFactory<Program> factory)
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

            Assert.Contains("Two-Hatchery Against Mech Terran", html);
            Assert.Contains("img.youtube.com/vi/isIm67yGPzo", html);
        }
    }
}