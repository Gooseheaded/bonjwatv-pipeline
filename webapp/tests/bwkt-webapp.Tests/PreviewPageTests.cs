using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace bwkt_webapp.Tests;

public class PreviewPageTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;
    public PreviewPageTests(TestWebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task Preview_Page_Renders_And_Notes_Precedence()
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = true
        });
        var res = await client.GetAsync("/Preview");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var html = await res.Content.ReadAsStringAsync();
        Assert.Contains("Preview Subtitles", html);
        // Verify the precedence note is present
        Assert.Contains("if both are provided, pasted SRT is used", html);

        // Sanity: ensure the font size controls appear after the video wrapper markup
        var idxVideo = html.IndexOf("video-wrapper");
        var idxControls = html.IndexOf("Subtitle Font Size:");
        Assert.True(idxVideo >= 0 && idxControls > idxVideo, "Font size controls should appear after the video wrapper");
    }
}

