using System;
using System.IO;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
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
            var solutionDir = FindSolutionDirectory();
            var projectDir = Path.Combine(solutionDir, "catalog-api");
            builder.UseContentRoot(projectDir);

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
                    ["Data:VideosStorePath"] = path,
                    ["Data:RatingsPath"] = ratingsPath,
                    ["Data:SubtitlesRoot"] = subsRoot,
                    ["Data:SubmissionsPath"] = submissionsPath,
                    ["API_INGEST_TOKENS"] = "TOKEN1"
                };
                cfg.AddInMemoryCollection(dict!);
            });
        });
    }

    private static string FindSolutionDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.GetFiles("*.sln").Any())
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Solution directory not found.");
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
    public async Task Search_By_Creator_Canonical_And_Original_Works()
    {
        // Use a dedicated factory to control paths and seed mappings
        var solutionDir = FindSolutionDirectory();
        var projectDir = Path.Combine(solutionDir, "catalog-api");
        var tmp = Directory.CreateTempSubdirectory();
        var vidsPath = Path.Combine(tmp.FullName, "videos.json");
        await File.WriteAllTextAsync(vidsPath, "[]");
        var ratingsPath = Path.Combine(tmp.FullName, "ratings.json");
        var subsRoot = Path.Combine(tmp.FullName, "subtitles");
        var submissionsPath = Path.Combine(tmp.FullName, "submissions.json");
        var mappingsPath = Path.Combine(tmp.FullName, "creator-mappings.json");

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseContentRoot(projectDir);
            builder.ConfigureAppConfiguration((ctx, cfg) =>
            {
                var dict = new Dictionary<string, string?>
                {
                    ["Data:VideosStorePath"] = vidsPath,
                    ["Data:RatingsPath"] = ratingsPath,
                    ["Data:SubtitlesRoot"] = subsRoot,
                    ["Data:SubmissionsPath"] = submissionsPath,
                    ["Data:CreatorMappingsPath"] = mappingsPath,
                    ["API_INGEST_TOKENS"] = "TOK2"
                };
                cfg.AddInMemoryCollection(dict!);
            });
        });

        var client = factory.CreateClient();

        // Create mapping: original -> canonical
        var create = await client.PostAsJsonAsync("/api/admin/creators/mappings", new { source = "파도튜브[PADOTUBE]", canonical = "Pado" });
        create.EnsureSuccessStatusCode();

        // Create a submission with original creator; approve to write to videos store with canonical
        using var submitReq = new HttpRequestMessage(HttpMethod.Post, "/api/submissions/videos");
        submitReq.Headers.Add("X-Api-Key", "TOK2");
        submitReq.Content = JsonContent.Create(new { youtube_id = "padotube1", title = "Pado VOD", creator = "파도튜브[PADOTUBE]" });
        var submit = await client.SendAsync(submitReq);
        submit.EnsureSuccessStatusCode();
        var sid = (await submit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("submission_id").GetString();
        var approve = await client.PatchAsync($"/api/admin/submissions/{sid}", JsonContent.Create(new { action = "approve" }));
        approve.EnsureSuccessStatusCode();

        // Search by canonical creator
        var res1 = await client.GetFromJsonAsync<JsonElement>("/api/videos?q=Pado&pageSize=100");
        Assert.True(res1.GetProperty("items").EnumerateArray().Any(el => el.GetProperty("id").GetString() == "padotube1"));
        // Search by original creator (mapping resolution)
        var res2 = await client.GetFromJsonAsync<JsonElement>("/api/videos?q=%ED%8C%8C%EB%8F%84%ED%8A%9C%EB%B8%8C%5BPADOTUBE%5D&pageSize=100");
        Assert.True(res2.GetProperty("items").EnumerateArray().Any(el => el.GetProperty("id").GetString() == "padotube1"));
    }

    [Fact]
    public async Task Health_Returns_Ok()
    {
        var client = _factory.CreateClient();
        var res = await client.GetFromJsonAsync<JsonElement>("/healthz");
        Assert.True(res.TryGetProperty("ok", out var ok) && ok.GetBoolean());
    }

    [Fact]
    public async Task GetVideoById_Returns_Item()
    {
        var client = _factory.CreateClient();
        var dto = await client.GetFromJsonAsync<JsonElement>("/api/videos/a");
        Assert.Equal("a", dto.GetProperty("id").GetString());
        Assert.Equal("Alpha Z", dto.GetProperty("title").GetString());
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
    public async Task Ratings_Remove_Clears_User_And_Counts()
    {
        var client = _factory.CreateClient();
        var vid = "rmt1";
        // Submit rating as forwarded user
        using (var req = new HttpRequestMessage(HttpMethod.Post, $"/api/videos/{vid}/ratings"))
        {
            req.Content = JsonContent.Create(new { value = "green", version = 1 });
            req.Headers.Add("X-User-Id", "user123");
            req.Headers.Add("X-User-Name", "User 123");
            var post = await client.SendAsync(req);
            post.EnsureSuccessStatusCode();
        }

        // GET summary with forwarded user should show UserRating
        var getWithUser = new HttpRequestMessage(HttpMethod.Get, $"/api/videos/{vid}/ratings?version=1");
        getWithUser.Headers.Add("X-User-Id", "user123");
        var sum1 = await (await client.SendAsync(getWithUser)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("green", (sum1.GetProperty("UserRating").GetString() ?? string.Empty).ToLowerInvariant());
        Assert.True(sum1.GetProperty("Green").GetInt32() >= 1);

        // DELETE rating for that user
        var delReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/videos/{vid}/ratings?version=1");
        delReq.Headers.Add("X-User-Id", "user123");
        var del = await client.SendAsync(delReq);
        del.EnsureSuccessStatusCode();

        // Counts should drop back to 0; user rating should be null
        var getWithUser2 = new HttpRequestMessage(HttpMethod.Get, $"/api/videos/{vid}/ratings?version=1");
        getWithUser2.Headers.Add("X-User-Id", "user123");
        var sum2 = await (await client.SendAsync(getWithUser2)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, sum2.GetProperty("Red").GetInt32() + sum2.GetProperty("Yellow").GetInt32() + sum2.GetProperty("Green").GetInt32());
        Assert.True(sum2.GetProperty("UserRating").ValueKind == JsonValueKind.Null || string.IsNullOrEmpty(sum2.GetProperty("UserRating").GetString()));
    }

    [Fact]
    public async Task Admin_Subtitles_List_Returns_Versions_With_Current_Flag()
    {
        await using var ctx = await SeedVideoWithSubtitlesAsync(currentVersion: 2);
        var client = ctx.Factory.CreateClient();
        var payload = await client.GetFromJsonAsync<JsonElement>("/api/admin/videos/vid1/subtitles");
        var items = payload.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal(2, payload.GetProperty("currentVersion").GetInt32());
        var v1 = items.First(el => el.GetProperty("version").GetInt32() == 1);
        var v2 = items.First(el => el.GetProperty("version").GetInt32() == 2);
        Assert.False(v1.GetProperty("isCurrent").GetBoolean());
        Assert.True(v2.GetProperty("isCurrent").GetBoolean());
        Assert.True(v2.GetProperty("sizeBytes").GetInt64() > 0);
        Assert.True(v2.GetProperty("addedLines").GetInt32() >= 0);
        Assert.True(v2.GetProperty("removedLines").GetInt32() >= 0);
    }

    [Fact]
    public async Task Admin_Subtitles_Diff_Shows_Previous_And_Current_Lines()
    {
        await using var ctx = await SeedVideoWithSubtitlesAsync(currentVersion: 2);
        var client = ctx.Factory.CreateClient();
        var text = await client.GetStringAsync("/api/admin/videos/vid1/subtitles/2/diff");
        Assert.Contains("--- v1", text);
        Assert.Contains("+++ v2", text);
        Assert.Contains("+Second line updated", text);
        Assert.Contains("-Second line original", text);
    }

    [Fact]
    public async Task Admin_Subtitles_Promote_Updates_SubtitleUrl()
    {
        await using var ctx = await SeedVideoWithSubtitlesAsync(currentVersion: 1);
        var client = ctx.Factory.CreateClient();
        var res = await client.PostAsync("/api/admin/videos/vid1/subtitles/2/promote", JsonContent.Create(new { }));
        res.EnsureSuccessStatusCode();
        var json = await File.ReadAllTextAsync(ctx.VideosPath);
        using var doc = JsonDocument.Parse(json);
        var video = doc.RootElement.EnumerateArray().First();
        Assert.Equal("/api/subtitles/vid1/2.srt", video.GetProperty("subtitleUrl").GetString());
    }

    [Fact]
    public async Task Admin_Subtitles_Delete_Removes_File_When_Not_Current()
    {
        await using var ctx = await SeedVideoWithSubtitlesAsync(currentVersion: 2);
        var client = ctx.Factory.CreateClient();
        var bad = await client.DeleteAsync("/api/admin/videos/vid1/subtitles/2");
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, bad.StatusCode);

        var ok = await client.DeleteAsync("/api/admin/videos/vid1/subtitles/1");
        ok.EnsureSuccessStatusCode();
        Assert.False(File.Exists(Path.Combine(ctx.SubtitlesRoot, "vid1", "v1.srt")));
        var json = await File.ReadAllTextAsync(ctx.VideosPath);
        using var doc = JsonDocument.Parse(json);
        var contributors = doc.RootElement.EnumerateArray().First().GetProperty("subtitleContributors").EnumerateArray().ToList();
        Assert.Single(contributors);
        Assert.Equal(2, contributors[0].GetProperty("version").GetInt32());
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
        var upReq = new HttpRequestMessage(HttpMethod.Post, "/api/uploads/subtitles") { Content = form };
        upReq.Headers.Add("X-Api-Key", "TOKEN1");
        var post = await client.SendAsync(upReq);
        post.EnsureSuccessStatusCode();
        var j = await post.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(j.TryGetProperty("storage_key", out var key));
        // Now staged; should not be publicly served yet
        Assert.Contains("staging/abc123", key.GetString());
        var get = await client.GetAsync("/api/subtitles/abc123/1.srt");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, get.StatusCode);
        // Create submission and approve to promote staged file
        using var submitReq = new HttpRequestMessage(HttpMethod.Post, "/api/submissions/videos");
        submitReq.Headers.Add("X-Api-Key", "TOKEN1");
        submitReq.Content = JsonContent.Create(new { youtube_id = "abc123", title = "Alpha", subtitle_storage_key = key.GetString() });
        var submit = await client.SendAsync(submitReq);
        submit.EnsureSuccessStatusCode();
        var sid = (await submit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("submission_id").GetString();
        var approve = await client.PatchAsync($"/api/admin/submissions/{sid}", JsonContent.Create(new { action = "approve" }));
        approve.EnsureSuccessStatusCode();
        var get2 = await client.GetAsync("/api/subtitles/abc123/1.srt");
        get2.EnsureSuccessStatusCode();
        var text = await get2.Content.ReadAsStringAsync();
        Assert.Contains("Hello!", text);
        Assert.Equal("text/plain; charset=utf-8", get2.Content.Headers.ContentType!.ToString());
    }

    [Fact]
    public async Task SubtitleHashes_Returns_Hash_Or_Null()
    {
        var client = _factory.CreateClient();
        // Upload and approve a subtitle for abc123 so it becomes first-party and referenced
        var srt = "1\n00:00:01,000 --> 00:00:02,000\nHello!\n";
        using (var form = new MultipartFormDataContent())
        {
            form.Add(new StringContent("abc123"), "videoId");
            form.Add(new StringContent("1"), "version");
            form.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(srt)), "file", "sub.srt");
            var upReq = new HttpRequestMessage(HttpMethod.Post, "/api/uploads/subtitles") { Content = form };
            upReq.Headers.Add("X-Api-Key", "TOKEN1");
            var post = await client.SendAsync(upReq);
            post.EnsureSuccessStatusCode();
            var key = (await post.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("storage_key").GetString();
            using var submitReq = new HttpRequestMessage(HttpMethod.Post, "/api/submissions/videos");
            submitReq.Headers.Add("X-Api-Key", "TOKEN1");
            submitReq.Content = JsonContent.Create(new { youtube_id = "abc123", title = "Alpha", subtitle_storage_key = key });
            var submit = await client.SendAsync(submitReq);
            submit.EnsureSuccessStatusCode();
            var sid = (await submit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("submission_id").GetString();
            var approve = await client.PatchAsync($"/api/admin/submissions/{sid}", JsonContent.Create(new { action = "approve" }));
            approve.EnsureSuccessStatusCode();
        }

        // Request hashes for known id and unknown id
        var resp = await client.GetAsync("/api/subtitles/hashes?ids=abc123&ids=nope999");
        resp.EnsureSuccessStatusCode();
        var map = await resp.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>();
        Assert.NotNull(map);
        Assert.True(map!.ContainsKey("abc123"));
        var hashEl = map["abc123"];
        Assert.Equal(JsonValueKind.String, hashEl.ValueKind);
        var hash = hashEl.GetString();
        Assert.False(string.IsNullOrWhiteSpace(hash));
        // Basic property: sha256 hex of the bytes
        using var sha = System.Security.Cryptography.SHA256.Create();
        var expected = Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(srt))).ToLowerInvariant();
        Assert.Equal(expected, hash);
        // Unknown should be null
        Assert.True(map.ContainsKey("nope999"));
        Assert.Equal(JsonValueKind.Null, map["nope999"].ValueKind);
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
        var upReq2 = new HttpRequestMessage(HttpMethod.Post, "/api/uploads/subtitles") { Content = content };
        upReq2.Headers.Add("X-Api-Key", "TOKEN1");
        var up = await client.SendAsync(upReq2);
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

    [Fact]
    public async Task Reject_Deletes_Unreferenced_Subtitle()
    {
        // Use a dedicated factory with known temp paths we can inspect
        var tmpRoot = Directory.CreateTempSubdirectory();
        var vidsPath = Path.Combine(tmpRoot.FullName, "videos.json");
        await File.WriteAllTextAsync(vidsPath, "[]");
        var ratingsPath = Path.Combine(tmpRoot.FullName, "ratings.json");
        var subsRoot = Path.Combine(tmpRoot.FullName, "subtitles");
        var submissionsPath = Path.Combine(tmpRoot.FullName, "submissions.json");

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((ctx, cfg) =>
            {
                var dict = new Dictionary<string, string?>
                {
                    ["Data:JsonPath"] = vidsPath,
                    ["Data:VideosStorePath"] = vidsPath,
                    ["Data:RatingsPath"] = ratingsPath,
                    ["Data:SubtitlesRoot"] = subsRoot,
                    ["Data:SubmissionsPath"] = submissionsPath,
                    ["API_INGEST_TOKENS"] = "TOKEN1"
                };
                cfg.AddInMemoryCollection(dict!);
            });
        });

        var client = factory.CreateClient();

        // Upload an SRT to produce a storage_key and create the file on disk
        var up = new MultipartFormDataContent();
        up.Add(new StringContent("rej001"), "videoId");
        up.Add(new StringContent("1"), "version");
        up.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("1\n00:00:01,000 --> 00:00:02,000\nDEL\n")), "file", "d.srt");
        var upReq3 = new HttpRequestMessage(HttpMethod.Post, "/api/uploads/subtitles") { Content = up };
        upReq3.Headers.Add("X-Api-Key", "TOKEN1");
        var respUp = await client.SendAsync(upReq3);
        respUp.EnsureSuccessStatusCode();
        var storageKey = (await respUp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("storage_key").GetString();

        // Create a submission referencing the stored subtitle
        using var submitReq = new HttpRequestMessage(HttpMethod.Post, "/api/submissions/videos");
        submitReq.Headers.Add("X-Api-Key", "TOKEN1");
        submitReq.Content = JsonContent.Create(new { youtube_id = "rej001", title = "DeleteMe", subtitle_storage_key = storageKey });
        var submit = await client.SendAsync(submitReq);
        submit.EnsureSuccessStatusCode();
        var sid = (await submit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("submission_id").GetString();

        // Reject the submission — file should be deleted because videos.json does not reference it
        var reject = await client.PatchAsync($"/api/admin/submissions/{sid}", JsonContent.Create(new { action = "reject" }));
        reject.EnsureSuccessStatusCode();

        // Subtitle should now be gone
        var get = await client.GetAsync("/api/subtitles/rej001/1.srt");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task Reject_DoesNot_Delete_When_Referenced_In_Videos()
    {
        // Set up known paths
        var tmpRoot = Directory.CreateTempSubdirectory();
        var vidsPath = Path.Combine(tmpRoot.FullName, "videos.json");
        await File.WriteAllTextAsync(vidsPath, "[]");
        var ratingsPath = Path.Combine(tmpRoot.FullName, "ratings.json");
        var subsRoot = Path.Combine(tmpRoot.FullName, "subtitles");
        var submissionsPath = Path.Combine(tmpRoot.FullName, "submissions.json");

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((ctx, cfg) =>
            {
                var dict = new Dictionary<string, string?>
                {
                    ["Data:JsonPath"] = vidsPath,
                    ["Data:VideosStorePath"] = vidsPath,
                    ["Data:RatingsPath"] = ratingsPath,
                    ["Data:SubtitlesRoot"] = subsRoot,
                    ["Data:SubmissionsPath"] = submissionsPath,
                    ["API_INGEST_TOKENS"] = "TOKEN1"
                };
                cfg.AddInMemoryCollection(dict!);
            });
        });

        var client = factory.CreateClient();

        // Upload and store subtitle
        var up = new MultipartFormDataContent();
        up.Add(new StringContent("keep001"), "videoId");
        up.Add(new StringContent("1"), "version");
        up.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("1\n00:00:01,000 --> 00:00:02,000\nKEEP\n")), "file", "k.srt");
        var upReq4 = new HttpRequestMessage(HttpMethod.Post, "/api/uploads/subtitles") { Content = up };
        upReq4.Headers.Add("X-Api-Key", "TOKEN1");
        var respUp = await client.SendAsync(upReq4);
        respUp.EnsureSuccessStatusCode();
        var storageKey = (await respUp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("storage_key").GetString();

        // Manually add an entry in videos.json that references this subtitle URL
        var internalUrl = "/api/subtitles/keep001/1.srt";
        var publicSubsDir = Path.Combine(subsRoot, "keep001");
        Directory.CreateDirectory(publicSubsDir);
        await File.WriteAllTextAsync(Path.Combine(publicSubsDir, "v1.srt"), "1\n00:00:01,000 --> 00:00:02,000\nKEEP\n");

        var json = System.Text.Json.JsonSerializer.Serialize(new [] { new Dictionary<string, object?> { ["v"] = "keep001", ["title"] = "Gamma", ["subtitleUrl"] = internalUrl } }, new System.Text.Json.JsonSerializerOptions{ WriteIndented = true });
        await File.WriteAllTextAsync(vidsPath, json);

        // Submit a video referencing the same stored subtitle
        using var submitReq = new HttpRequestMessage(HttpMethod.Post, "/api/submissions/videos");
        submitReq.Headers.Add("X-Api-Key", "TOKEN1");
        submitReq.Content = JsonContent.Create(new { youtube_id = "keep001", title = "Keep", subtitle_storage_key = storageKey });
        var submit = await client.SendAsync(submitReq);
        submit.EnsureSuccessStatusCode();
        var sid = (await submit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("submission_id").GetString();

        // Reject — since videos.json references the subtitle, the file should not be deleted
        var reject = await client.PatchAsync($"/api/admin/submissions/{sid}", JsonContent.Create(new { action = "reject" }));
        reject.EnsureSuccessStatusCode();

        var get = await client.GetAsync("/api/subtitles/keep001/1.srt");
        get.EnsureSuccessStatusCode();
        var text = await get.Content.ReadAsStringAsync();
        Assert.Contains("KEEP", text);
    }

    [Fact]
    public async Task Hiding_Multiple_Videos_Accumulates_In_Hidden_List()
    {
        // Arrange a temp store with two videos
        var tmpRoot = Directory.CreateTempSubdirectory();
        var vidsPath = Path.Combine(tmpRoot.FullName, "videos.json");
        var ratingsPath = Path.Combine(tmpRoot.FullName, "ratings.json");
        var subsRoot = Path.Combine(tmpRoot.FullName, "subtitles");
        var submissionsPath = Path.Combine(tmpRoot.FullName, "submissions.json");
        await File.WriteAllTextAsync(vidsPath, "[ {\"v\":\"a\",\"title\":\"Alpha\"}, {\"v\":\"b\",\"title\":\"Beta\"} ]");

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((ctx, cfg) =>
            {
                var dict = new Dictionary<string, string?>
                {
                    ["Data:JsonPath"] = vidsPath,
                    ["Data:VideosStorePath"] = vidsPath,
                    ["Data:RatingsPath"] = ratingsPath,
                    ["Data:SubtitlesRoot"] = subsRoot,
                    ["Data:SubmissionsPath"] = submissionsPath,
                    ["API_INGEST_TOKENS"] = "TOKHIDE"
                };
                cfg.AddInMemoryCollection(dict!);
            });
        });

        var client = factory.CreateClient();

        // Initially nothing hidden
        var initial = await client.GetFromJsonAsync<JsonElement>("/api/admin/videos/hidden");
        Assert.Equal(JsonValueKind.Array, initial.ValueKind);
        Assert.Equal(0, initial.GetArrayLength());

        // Hide first video
        using (var msg1 = new HttpRequestMessage(HttpMethod.Patch, "/api/admin/videos/a/hide")
        {
            Content = JsonContent.Create(new { reason = "t1" })
        })
        {
            var r1 = await client.SendAsync(msg1);
            r1.EnsureSuccessStatusCode();
        }

        var afterOne = await client.GetFromJsonAsync<JsonElement>("/api/admin/videos/hidden");
        Assert.Equal(1, afterOne.GetArrayLength());
        Assert.Contains(afterOne.EnumerateArray(), el => el.GetProperty("id").GetString() == "a");

        // Hide second video
        using (var msg2 = new HttpRequestMessage(HttpMethod.Patch, "/api/admin/videos/b/hide")
        {
            Content = JsonContent.Create(new { reason = "t2" })
        })
        {
            var r2 = await client.SendAsync(msg2);
            r2.EnsureSuccessStatusCode();
        }

        var afterTwo = await client.GetFromJsonAsync<JsonElement>("/api/admin/videos/hidden");
        Assert.Equal(2, afterTwo.GetArrayLength());
        var ids = afterTwo.EnumerateArray().Select(el => el.GetProperty("id").GetString() ?? "").ToArray();
        Assert.Contains("a", ids);
        Assert.Contains("b", ids);
    }

    [Fact]
    public async Task Admin_Tags_Set_Add_Remove_Update_Endpoints()
    {
        // Arrange a temp store with one video
        var tmpRoot = Directory.CreateTempSubdirectory();
        var vidsPath = Path.Combine(tmpRoot.FullName, "videos.json");
        var ratingsPath = Path.Combine(tmpRoot.FullName, "ratings.json");
        var subsRoot = Path.Combine(tmpRoot.FullName, "subtitles");
        var submissionsPath = Path.Combine(tmpRoot.FullName, "submissions.json");
        await File.WriteAllTextAsync(vidsPath, System.Text.Json.JsonSerializer.Serialize(new [] { new Dictionary<string, object?> { ["v"] = "tag001", ["title"] = "Tag Test", ["tags"] = new [] { "z" } } }));

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((ctx, cfg) =>
            {
                var dict = new Dictionary<string, string?>
                {
                    ["Data:JsonPath"] = vidsPath,
                    ["Data:VideosStorePath"] = vidsPath,
                    ["Data:RatingsPath"] = ratingsPath,
                    ["Data:SubtitlesRoot"] = subsRoot,
                    ["Data:SubmissionsPath"] = submissionsPath,
                    ["API_INGEST_TOKENS"] = "TOKEN1"
                };
                cfg.AddInMemoryCollection(dict!);
            });
        });
        var client = factory.CreateClient();

        // Verify initial tags via list
        var list1 = await client.GetFromJsonAsync<JsonElement>("/api/videos?q=Tag%20Test&pageSize=10");
        var first1 = list1.GetProperty("items").EnumerateArray().First();
        Assert.Contains("z", first1.GetProperty("tags").EnumerateArray().Select(e => e.GetString()));

        // Set tags to [p, pvz]
        var setPayload = JsonContent.Create(new { action = "set", tags = new [] { "p", "pvz" } });
        var setRes = await client.PatchAsync("/api/admin/videos/tag001/tags", setPayload);
        setRes.EnsureSuccessStatusCode();

        var list2 = await client.GetFromJsonAsync<JsonElement>("/api/videos?q=Tag%20Test&pageSize=10");
        var first2 = list2.GetProperty("items").EnumerateArray().First();
        var tags2 = first2.GetProperty("tags").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.DoesNotContain("z", tags2);
        Assert.Contains("p", tags2);
        Assert.Contains("pvz", tags2);

        // Add tag 'zvz'
        var addRes = await client.PatchAsync("/api/admin/videos/tag001/tags", JsonContent.Create(new { action = "add", tag = "zvz" }));
        addRes.EnsureSuccessStatusCode();
        var adminDetail = await client.GetFromJsonAsync<JsonElement>("/api/admin/videos/tag001");
        var tags3 = adminDetail.GetProperty("tags").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("zvz", tags3);

        // Remove tag 'p'
        var remRes = await client.PatchAsync("/api/admin/videos/tag001/tags", JsonContent.Create(new { action = "remove", tag = "p" }));
        remRes.EnsureSuccessStatusCode();
        var adminDetail2 = await client.GetFromJsonAsync<JsonElement>("/api/admin/videos/tag001");
        var tags4 = adminDetail2.GetProperty("tags").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.DoesNotContain("p", tags4);
        Assert.Contains("pvz", tags4);
        Assert.Contains("zvz", tags4);
    }

    [Fact]
    public async Task Legacy_Migration_DryRun_Previews_Without_Writing()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            var storePath = Path.Combine(tmp.FullName, "catalog-videos.json");
            var legacyPath = Path.Combine(tmp.FullName, "videos.json");
            var ratingsPath = Path.Combine(tmp.FullName, "ratings.json");
            var submissionsPath = Path.Combine(tmp.FullName, "submissions.json");
            var subtitlesRoot = Path.Combine(tmp.FullName, "subtitles");

            await File.WriteAllTextAsync(storePath,
                "[{\"v\":\"keep001\",\"title\":\"Keep Title\",\"creator\":\"Catalog Creator\",\"tags\":[\"z\"],\"subtitleUrl\":\"/api/subtitles/keep001/2.srt\"}]");
            await File.WriteAllTextAsync(legacyPath,
                "[" +
                "{\"v\":\"keep001\",\"title\":\"Legacy Title\",\"description\":\"Legacy desc\",\"tags\":[\"zvt\",\"story\"],\"subtitleUrl\":\"https://legacy.example/keep001.srt\"}," +
                "{\"v\":\"new001\",\"title\":\"Brand New\",\"creator\":\"Legacy Creator\",\"tags\":[\"p\",\"pvt\"]}," +
                "{\"v\":\"bad001\",\"title\":\"   \"}" +
                "]");

            var factory = _factory.WithWebHostBuilder(builder =>
            {
                var solutionDir = FindSolutionDirectory();
                var projectDir = Path.Combine(solutionDir, "catalog-api");
                builder.UseContentRoot(projectDir);
                builder.ConfigureAppConfiguration((ctx, cfg) =>
                {
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Data:VideosStorePath"] = storePath,
                        ["Data:JsonPath"] = legacyPath,
                        ["Data:RatingsPath"] = ratingsPath,
                        ["Data:SubmissionsPath"] = submissionsPath,
                        ["Data:SubtitlesRoot"] = subtitlesRoot
                    }!);
                });
            });
            var client = factory.CreateClient();

            var before = await File.ReadAllTextAsync(storePath);
            var resp = await client.PostAsJsonAsync("/api/admin/migrations/legacy-videos/import", new { dryRun = true });
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();

            Assert.True(body.GetProperty("ok").GetBoolean());
            Assert.True(body.GetProperty("dryRun").GetBoolean());
            var totals = body.GetProperty("totals");
            Assert.Equal(3, totals.GetProperty("legacyCount").GetInt32());
            Assert.Equal(1, totals.GetProperty("created").GetInt32());
            Assert.Equal(1, totals.GetProperty("updatedMissingFields").GetInt32());
            Assert.Equal(1, totals.GetProperty("tagsMerged").GetInt32());
            Assert.Equal(0, totals.GetProperty("unchanged").GetInt32());
            Assert.Equal(1, totals.GetProperty("skippedInvalid").GetInt32());
            Assert.Equal(0, totals.GetProperty("errors").GetInt32());

            var after = await File.ReadAllTextAsync(storePath);
            Assert.Equal(before, after);
        }
        finally
        {
            try { Directory.Delete(tmp.FullName, true); } catch { }
        }
    }

    [Fact]
    public async Task Legacy_Migration_Applies_UpdateMissing_And_TagUnion_And_Is_Idempotent()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            var storePath = Path.Combine(tmp.FullName, "catalog-videos.json");
            var legacyPath = Path.Combine(tmp.FullName, "videos.json");
            var ratingsPath = Path.Combine(tmp.FullName, "ratings.json");
            var submissionsPath = Path.Combine(tmp.FullName, "submissions.json");
            var subtitlesRoot = Path.Combine(tmp.FullName, "subtitles");

            await File.WriteAllTextAsync(storePath,
                "[" +
                "{\"v\":\"keep001\",\"title\":\"Keep Title\",\"creator\":\"Catalog Creator\",\"tags\":[\"z\"],\"subtitleUrl\":\"/api/subtitles/keep001/2.srt\"}," +
                "{\"v\":\"fill001\",\"title\":\"Fill Me\",\"creator\":\"   \",\"description\":\"\",\"tags\":[]}" +
                "]");
            await File.WriteAllTextAsync(legacyPath,
                "[" +
                "{\"v\":\"keep001\",\"title\":\"Legacy Title\",\"description\":\"Legacy desc\",\"tags\":[\"z\",\"zvt\",\"story\"],\"subtitleUrl\":\"https://legacy.example/keep001.srt\"}," +
                "{\"v\":\"fill001\",\"title\":\"Legacy Fill\",\"creator\":\"Legacy Creator\",\"description\":\"Legacy Fill Desc\",\"tags\":[\"t\"]}," +
                "{\"v\":\"new001\",\"title\":\"Brand New\",\"creator\":\"Legacy New Creator\",\"tags\":[\"p\",\"pvt\"],\"submitter\":\"alice\",\"submissionDate\":\"2025-01-01\"}" +
                "]");

            var factory = _factory.WithWebHostBuilder(builder =>
            {
                var solutionDir = FindSolutionDirectory();
                var projectDir = Path.Combine(solutionDir, "catalog-api");
                builder.UseContentRoot(projectDir);
                builder.ConfigureAppConfiguration((ctx, cfg) =>
                {
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Data:VideosStorePath"] = storePath,
                        ["Data:JsonPath"] = legacyPath,
                        ["Data:RatingsPath"] = ratingsPath,
                        ["Data:SubmissionsPath"] = submissionsPath,
                        ["Data:SubtitlesRoot"] = subtitlesRoot
                    }!);
                });
            });
            var client = factory.CreateClient();

            var apply = await client.PostAsJsonAsync("/api/admin/migrations/legacy-videos/import", new { dryRun = false });
            apply.EnsureSuccessStatusCode();
            var applyBody = await apply.Content.ReadFromJsonAsync<JsonElement>();
            var applyTotals = applyBody.GetProperty("totals");
            Assert.Equal(1, applyTotals.GetProperty("created").GetInt32());
            Assert.Equal(2, applyTotals.GetProperty("updatedMissingFields").GetInt32());
            Assert.Equal(2, applyTotals.GetProperty("tagsMerged").GetInt32());
            Assert.Equal(0, applyTotals.GetProperty("errors").GetInt32());

            using (var fs = File.OpenRead(storePath))
            using (var doc = JsonDocument.Parse(fs))
            {
                var arr = doc.RootElement.EnumerateArray().ToList();
                Assert.Equal(3, arr.Count);

                var keep = arr.First(x => x.GetProperty("v").GetString() == "keep001");
                Assert.Equal("Keep Title", keep.GetProperty("title").GetString());
                Assert.Equal("Legacy desc", keep.GetProperty("description").GetString());
                Assert.Equal("/api/subtitles/keep001/2.srt", keep.GetProperty("subtitleUrl").GetString());
                var keepTags = keep.GetProperty("tags").EnumerateArray().Select(x => x.GetString()).ToArray();
                Assert.Contains("z", keepTags);
                Assert.Contains("zvt", keepTags);
                Assert.Contains("story", keepTags);

                var fill = arr.First(x => x.GetProperty("v").GetString() == "fill001");
                Assert.Equal("Fill Me", fill.GetProperty("title").GetString());
                Assert.Equal("Legacy Creator", fill.GetProperty("creator").GetString());
                Assert.Equal("Legacy Fill Desc", fill.GetProperty("description").GetString());
                var fillTags = fill.GetProperty("tags").EnumerateArray().Select(x => x.GetString()).ToArray();
                Assert.Contains("t", fillTags);

                var created = arr.First(x => x.GetProperty("v").GetString() == "new001");
                Assert.Equal("Brand New", created.GetProperty("title").GetString());
                Assert.Equal("alice", created.GetProperty("submitter").GetString());
            }

            var rerun = await client.PostAsJsonAsync("/api/admin/migrations/legacy-videos/import", new { dryRun = false });
            rerun.EnsureSuccessStatusCode();
            var rerunBody = await rerun.Content.ReadFromJsonAsync<JsonElement>();
            var rerunTotals = rerunBody.GetProperty("totals");
            Assert.Equal(0, rerunTotals.GetProperty("created").GetInt32());
            Assert.Equal(0, rerunTotals.GetProperty("updatedMissingFields").GetInt32());
            Assert.Equal(0, rerunTotals.GetProperty("tagsMerged").GetInt32());
            Assert.Equal(3, rerunTotals.GetProperty("unchanged").GetInt32());
        }
        finally
        {
            try { Directory.Delete(tmp.FullName, true); } catch { }
        }
    }

    private sealed class SubtitleTestContext : IAsyncDisposable
    {
        public required WebApplicationFactory<Program> Factory { get; init; }
        public required string BaseDir { get; init; }
        public required string VideosPath { get; init; }
        public required string SubtitlesRoot { get; init; }

        public ValueTask DisposeAsync()
        {
            try { if (Directory.Exists(BaseDir)) Directory.Delete(BaseDir, recursive: true); }
            catch { }
            Factory.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private async Task<SubtitleTestContext> SeedVideoWithSubtitlesAsync(int currentVersion)
    {
        var tmp = Directory.CreateTempSubdirectory();
        var vidsPath = Path.Combine(tmp.FullName, "videos.json");
        var subsRoot = Path.Combine(tmp.FullName, "subtitles");
        Directory.CreateDirectory(Path.Combine(subsRoot, "vid1"));
        await File.WriteAllTextAsync(Path.Combine(subsRoot, "vid1", "v1.srt"),
@"1
00:00:00,000 --> 00:00:02,000
First line

2
00:00:03,000 --> 00:00:05,000
Second line original");
        await File.WriteAllTextAsync(Path.Combine(subsRoot, "vid1", "v2.srt"),
@"1
00:00:00,000 --> 00:00:02,000
First line

2
00:00:03,000 --> 00:00:05,000
Second line updated");
        var contributors = @"[
  {""version"":1,""userId"":""alice"",""displayName"":""Alice"",""submittedAt"":""2025-01-01T00:00:00Z""},
  {""version"":2,""userId"":""bob"",""displayName"":""Bob"",""submittedAt"":""2025-01-02T00:00:00Z""}
]";
        var subtitleUrl = $"/api/subtitles/vid1/{currentVersion}.srt";
        await File.WriteAllTextAsync(vidsPath, $"[{{\"v\":\"vid1\",\"title\":\"Test\",\"subtitleUrl\":\"{subtitleUrl}\",\"subtitleContributors\":{contributors}}}]");
        var ratingsPath = Path.Combine(tmp.FullName, "ratings.json");
        var submissionsPath = Path.Combine(tmp.FullName, "submissions.json");

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            var solutionDir = FindSolutionDirectory();
            var projectDir = Path.Combine(solutionDir, "catalog-api");
            builder.UseContentRoot(projectDir);
            builder.ConfigureAppConfiguration((ctx, cfg) =>
            {
                var dict = new Dictionary<string, string?>
                {
                    ["Data:VideosStorePath"] = vidsPath,
                    ["Data:SubtitlesRoot"] = subsRoot,
                    ["Data:RatingsPath"] = ratingsPath,
                    ["Data:SubmissionsPath"] = submissionsPath,
                    ["API_INGEST_TOKENS"] = "TOK_SUB"
                };
                cfg.AddInMemoryCollection(dict!);
            });
        });

        return new SubtitleTestContext
        {
            Factory = factory,
            BaseDir = tmp.FullName,
            VideosPath = vidsPath,
            SubtitlesRoot = subsRoot
        };
    }
}
