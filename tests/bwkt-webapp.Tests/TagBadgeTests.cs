using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using bwkt_webapp;

namespace bwkt_webapp.Tests
{
    public class TagBadgeTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public TagBadgeTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task IndexPage_ShowsConfiguredTagsAsBadges()
        {
            // Act: request the homepage
            var client = _factory.CreateClient();
            var response = await client.GetAsync("/");
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync();

            // Assert: known tags for first video should render as badges
            Assert.Contains("<span class=\"badge bg-danger\">Zerg</span>", html);
            Assert.Contains("<span class=\"badge bg-primary\">Terran</span>", html);
        }
    }
}