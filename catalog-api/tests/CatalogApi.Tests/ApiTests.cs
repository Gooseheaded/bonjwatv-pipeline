using System;
using System.IO;
using System.Collections.Generic;
using System.Net.Http;
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
                var ratingsPath = Path.Combine(dir.FullName, "ratings.json");
                var subsRoot = Path.Combine(dir.FullName, "subtitles");
                var submissionsPath = Path.Combine(dir.FullName, "submissions.json");
                var dict = new Dictionary<string, string?>
                {
                    ["Data:JsonPath"] = path,
                    ["Data:RatingsPath"] = ratingsPath,
                    ["Data:SubtitlesRoot"] = subsRoot,
                    ["Data:SubmissionsPath"] = submissionsPath,
                    ["API_INGEST_TOKENS"] = "TOKEN1"
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

    [Fact]
    public async Task Subtitles_Upload_And_Serve_Works()
    {
        var client = _factory.CreateClient();
        var srt = "1\n00:00:01,000 --> 00:00:02,000\nHello!\n";
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("abc123"), "videoId");
        form.Add(new StringContent("1"), "version");
        form.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(srt)), "file", "sub.srt");
        var post = await client.PostAsync("/api/uploads/subtitles", form);
        post.EnsureSuccessStatusCode();
        var j = await post.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(j.TryGetProperty("storage_key", out var key));
        Assert.Contains("abc123", key.GetString());

        var get = await client.GetAsync("/api/subtitles/abc123/1.srt");
        get.EnsureSuccessStatusCode();
        var text = await get.Content.ReadAsStringAsync();
        Assert.Contains("Hello!", text);
        Assert.Equal("text/plain; charset=utf-8", get.Content.Headers.ContentType!.ToString());
    }

    [Fact]
    public async Task Submissions_Create_List_Detail_Approve()
    {
        var client = _factory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/submissions/videos");
        req.Headers.Add("X-Api-Key", "TOKEN1");
        req.Content = JsonContent.Create(new {
            youtube_id = "abc123",
            title = "Alpha",
            tags = new [] { "z" },
            subtitle_storage_key = "subtitles/abc123/v1.srt"
        });
        var submit = await client.SendAsync(req);
        submit.EnsureSuccessStatusCode();
        var created = await submit.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("submission_id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(id));

        var list = await client.GetFromJsonAsync<JsonElement>("/api/admin/submissions?status=pending");
        Assert.True(list.TryGetProperty("items", out var items));
        Assert.True(items.EnumerateArray().Any());

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/admin/submissions/{id}");
        Assert.Equal("pending", detail.GetProperty("status").GetString());

        var approve = await client.PatchAsync($"/api/admin/submissions/{id}", JsonContent.Create(new { action = "approve" }));
        approve.EnsureSuccessStatusCode();

        var detail2 = await client.GetFromJsonAsync<JsonElement>($"/api/admin/submissions/{id}");
        Assert.Equal("approved", detail2.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Approval_Appends_To_Videos_And_Uses_Stored_Subtitle()
    {
        var client = _factory.CreateClient();

        // Upload SRT to get storage_key
        var content = new MultipartFormDataContent();
        content.Add(new StringContent("xyz123"), "videoId");
        content.Add(new StringContent("1"), "version");
        content.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("1\n00:00:01,000 --> 00:00:02,000\nOK\n")), "file", "x.srt");
        var up = await client.PostAsync("/api/uploads/subtitles", content);
        up.EnsureSuccessStatusCode();
        var upJson = await up.Content.ReadFromJsonAsync<JsonElement>();
        var storageKey = upJson.GetProperty("storage_key").GetString();

        // Submit a video referencing the storage key
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/submissions/videos");
        req.Headers.Add("X-Api-Key", "TOKEN1");
        req.Content = JsonContent.Create(new {
            youtube_id = "xyz123",
            title = "Gamma",
            tags = new [] { "z" },
            subtitle_storage_key = storageKey
        });
        var submit = await client.SendAsync(req);
        submit.EnsureSuccessStatusCode();
        var sid = (await submit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("submission_id").GetString();

        // Approve
        var approve = await client.PatchAsync($"/api/admin/submissions/{sid}", JsonContent.Create(new { action = "approve" }));
        approve.EnsureSuccessStatusCode();

        // Videos now contains xyz123 via search by title
        var videos = await client.GetFromJsonAsync<JsonElement>("/api/videos?q=Gamma&pageSize=100");
        var has = videos.GetProperty("items").EnumerateArray().Any(el => el.GetProperty("id").GetString() == "xyz123");
        Assert.True(has);

        // Subtitle served from first-party
        var srt = await client.GetStringAsync("/api/subtitles/xyz123/1.srt");
        Assert.Contains("OK", srt);
    }

    [Fact]
    public async Task Approval_Mirrors_FileUrl_When_No_StorageKey()
    {
        var client = _factory.CreateClient();

        // Prepare a local file URL for mirroring
        var tmp = Path.GetTempFileName();
        await File.WriteAllTextAsync(tmp, "1\n00:00:01,000 --> 00:00:02,000\nMIRROR\n");
        var fileUrl = "file://" + tmp;

        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/submissions/videos");
        req.Headers.Add("X-Api-Key", "TOKEN1");
        req.Content = JsonContent.Create(new {
            youtube_id = "file001",
            title = "Delta",
            tags = new [] { "t" },
            subtitle_url = fileUrl
        });
        var submit = await client.SendAsync(req);
        submit.EnsureSuccessStatusCode();
        var sid = (await submit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("submission_id").GetString();

        var approve = await client.PatchAsync($"/api/admin/submissions/{sid}", JsonContent.Create(new { action = "approve" }));
        approve.EnsureSuccessStatusCode();

        // Subtitle is mirrored and served
        var srt = await client.GetStringAsync("/api/subtitles/file001/1.srt");
        Assert.Contains("MIRROR", srt);

        // Videos now contains file001 via search by title
        var videos = await client.GetFromJsonAsync<JsonElement>("/api/videos?q=Delta&pageSize=100");
        var has = videos.GetProperty("items").EnumerateArray().Any(el => el.GetProperty("id").GetString() == "file001");
        Assert.True(has);
    }
}
