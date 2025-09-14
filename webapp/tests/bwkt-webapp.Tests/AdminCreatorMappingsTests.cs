using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace bwkt_webapp.Tests;

public class AdminCreatorMappingsTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;
    public AdminCreatorMappingsTests(TestWebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task CreatorMappings_Index_Renders_For_NonAdmin_With_Warning()
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = true
        });
        var res = await client.GetAsync("/Admin/CreatorMappings");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var html = await res.Content.ReadAsStringAsync();
        Assert.Contains("Creator Mappings", html);
        Assert.Contains("You do not have access to this page.", html);
    }
}

