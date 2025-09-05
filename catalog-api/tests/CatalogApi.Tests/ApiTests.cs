using System;
using System.IO;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CatalogApi.Tests;

public class ApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((ctx, cfg) =>
            {
                // Write a temp JSON file for the repository
                var dir = Directory.CreateTempSubdirectory();
                var path = Path.Combine(dir.FullName, "videos.json");
                File.WriteAllText(path, "[ {\"v\":\"a\",\"title\":\"Alpha Z\",\"tags\":[\"z\"]}, {\"v\":\"b\",\"title\":\"Beta T\",\"tags\":[\"t\"]} ]");
                var dict = new Dictionary<string, string?>
                {
                    ["Data:JsonPath"] = path
                };
                cfg.AddInMemoryCollection(dict!);
            });
        });
    }

    [Fact]
    public async Task VideosEndpoint_Filters_ByQueryAndRace_AndPaginates()
    {
        var client = _factory.CreateClient();
        var res = await client.GetFromJsonAsync<JsonElement>("/api/videos?q=Alpha&race=z&page=1&pageSize=1");
        Assert.True(res.TryGetProperty("items", out var items));
        Assert.Equal(1, items.GetArrayLength());
        var first = items.EnumerateArray().First();
        Assert.Equal("a", first.GetProperty("id").GetString());
        Assert.Equal(1, res.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, res.GetProperty("page").GetInt32());
        Assert.Equal(1, res.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task Ratings_Endpoint_Allows_Post_And_Summarizes()
    {
        var client = _factory.CreateClient();
        var vid = "a";
        var post1 = await client.PostAsJsonAsync($"/api/videos/{vid}/ratings", new { value = "red", version = 1 });
        post1.EnsureSuccessStatusCode();
        var post2 = await client.PostAsJsonAsync($"/api/videos/{vid}/ratings", new { value = "green", version = 1 });
        post2.EnsureSuccessStatusCode();
        var sum = await client.GetFromJsonAsync<JsonElement>($"/api/videos/{vid}/ratings?version=1");
        Assert.Equal(1, sum.GetProperty("Red").GetInt32() + sum.GetProperty("Green").GetInt32() + sum.GetProperty("Yellow").GetInt32());
    }
}
