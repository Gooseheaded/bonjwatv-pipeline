using System.Net;
using System.Threading.Tasks;
using bwkt_webapp.Pages.Admin;
using Xunit;

namespace bwkt_webapp.Tests;

public class AdminBatchTagTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;
    public AdminBatchTagTests(TestWebAppFactory factory) { _factory = factory; }

    [Fact]
    public void ParseIds_FiltersAndDedupes()
    {
        var input = "\n abcDEF123 \nabcDEF123\n bad id ! \nXYZ_987\n";
        var ids = BatchTagModel.ParseIds(input);
        Assert.Contains("abcDEF123", ids);
        Assert.Contains("XYZ_987", ids);
        Assert.DoesNotContain("bad id !", ids);
        Assert.Equal(2, ids.Count);
    }

    [Fact]
    public async Task BatchTag_Page_Renders_For_NonAdmin_With_Warning()
    {
        // Not admin: ADMIN_USER_IDS unset
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = true
        });
        var res = await client.GetAsync("/Admin/BatchTag");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var html = await res.Content.ReadAsStringAsync();
        Assert.Contains("Batch-Tagging Tool", html);
        Assert.Contains("You do not have access to this page.", html);
    }
}

