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
            // Act: request the homepage
            var client = _factory.CreateClient();
            var response = await client.GetAsync("/");
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync();

            // Assert: known tags for first video should render as badges
            Assert.Contains("<span class=\"badge bg-danger me-1\">Zerg</span>", html);
            Assert.Contains("<span class=\"badge bg-warning me-1\">Protoss</span>", html);
        }
    }
}
