using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace bwkt_webapp.Tests;

public class AuthNavTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;
    public AuthNavTests(TestWebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task LoginCallback_SignsIn_And_ShowsLogout()
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var cb = await client.GetAsync("/account/callback?code=demo");
        Assert.Equal(HttpStatusCode.Redirect, cb.StatusCode);
        // follow redirect
        var home = await client.GetAsync("/");
        var html = await home.Content.ReadAsStringAsync();
        Assert.Contains("Hello, Demo User", html);
        Assert.Contains("Logout", html);
    }
}

