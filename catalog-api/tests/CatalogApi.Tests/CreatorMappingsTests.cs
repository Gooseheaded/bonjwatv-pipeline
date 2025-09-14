using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CatalogApi.Tests;

public class CreatorMappingsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CreatorMappingsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((ctx, cfg) =>
            {
                var dir = Directory.CreateTempSubdirectory();
                var vidsPath = Path.Combine(dir.FullName, "videos.json");
                File.WriteAllText(vidsPath, "[]");
                var ratingsPath = Path.Combine(dir.FullName, "ratings.json");
                var subsRoot = Path.Combine(dir.FullName, "subtitles");
                var submissionsPath = Path.Combine(dir.FullName, "submissions.json");
                var mappingsPath = Path.Combine(dir.FullName, "creator-mappings.json");
                var dict = new System.Collections.Generic.Dictionary<string, string?>
                {
                    ["Data:VideosStorePath"] = vidsPath,
                    ["Data:RatingsPath"] = ratingsPath,
                    ["Data:SubtitlesRoot"] = subsRoot,
                    ["Data:SubmissionsPath"] = submissionsPath,
                    ["Data:CreatorMappingsPath"] = mappingsPath,
                    ["API_INGEST_TOKENS"] = "TOK"
                };
                cfg.AddInMemoryCollection(dict!);
            });
        });
    }

    [Fact]
    public async Task Crud_And_Resolve_Works()
    {
        var client = _factory.CreateClient();

        // Create mapping
        var create = await client.PostAsJsonAsync("/api/admin/creators/mappings", new { source = "파도튜브[PADOTUBE]", canonical = "Pado" });
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString();
        Assert.Equal("Pado", created.GetProperty("canonical").GetString());

        // List should contain the mapping
        var list = await client.GetFromJsonAsync<JsonElement>("/api/admin/creators/mappings");
        Assert.True(list.GetProperty("items").EnumerateArray().Any());

        // Update mapping
        var update = await client.PutAsJsonAsync($"/api/admin/creators/mappings/{id}", new { source = "파도튜브[PADOTUBE]", canonical = "PADO" });
        update.EnsureSuccessStatusCode();
        var updated = await update.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("PADO", updated.GetProperty("canonical").GetString());

        // Delete mapping
        var del = await client.DeleteAsync($"/api/admin/creators/mappings/{id}");
        Assert.Equal(System.Net.HttpStatusCode.NoContent, del.StatusCode);
    }

    [Fact]
    public async Task Ingest_Applies_Canonical_And_Approval_Uses_It()
    {
        var client = _factory.CreateClient();
        // Create mapping
        var create = await client.PostAsJsonAsync("/api/admin/creators/mappings", new { source = "파도튜브[PADOTUBE]", canonical = "Pado" });
        create.EnsureSuccessStatusCode();

        // Submit a video with original creator
        using var submitReq = new HttpRequestMessage(HttpMethod.Post, "/api/submissions/videos");
        submitReq.Headers.Add("X-Api-Key", "TOK");
        submitReq.Content = JsonContent.Create(new { youtube_id = "pado001", title = "Some Title", creator = "파도튜브[PADOTUBE]" });
        var submit = await client.SendAsync(submitReq);
        submit.EnsureSuccessStatusCode();
        var sid = (await submit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("submission_id").GetString();

        // Approve
        var approve = await client.PatchAsync($"/api/admin/submissions/{sid}", JsonContent.Create(new { action = "approve" }));
        approve.EnsureSuccessStatusCode();

        // Public videos endpoint should show canonical creator
        var video = await client.GetFromJsonAsync<JsonElement>("/api/videos/pado001");
        Assert.Equal("Pado", video.GetProperty("creator").GetString());
    }

    [Fact]
    public async Task Reapply_Updates_Existing()
    {
        var client = _factory.CreateClient();
        // Seed a catalog entry with non-canonical creator only
        var vidsPath = client.GetType(); // placeholder to get hosting; we will add via admin approval path

        // Create mapping
        await client.PostAsJsonAsync("/api/admin/creators/mappings", new { source = "ABC KR", canonical = "ABC" });

        // Submit/approve a video with original form
        using var submitReq = new HttpRequestMessage(HttpMethod.Post, "/api/submissions/videos");
        submitReq.Headers.Add("X-Api-Key", "TOK");
        submitReq.Content = JsonContent.Create(new { youtube_id = "abc001", title = "ABC Vid", creator = "ABC KR" });
        var submit = await client.SendAsync(submitReq);
        submit.EnsureSuccessStatusCode();
        var sid = (await submit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("submission_id").GetString();
        var approve = await client.PatchAsync($"/api/admin/submissions/{sid}", JsonContent.Create(new { action = "approve" }));
        approve.EnsureSuccessStatusCode();

        // Change mapping
        var list = await client.GetFromJsonAsync<JsonElement>("/api/admin/creators/mappings");
        var first = list.GetProperty("items").EnumerateArray().First();
        var id = first.GetProperty("id").GetString();
        await client.PutAsJsonAsync($"/api/admin/creators/mappings/{id}", new { source = "ABC KR", canonical = "A.B.C" });

        // Reapply
        var re = await client.PostAsync("/api/admin/creators/mappings/reapply", null);
        re.EnsureSuccessStatusCode();
        var rr = await re.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(rr.GetProperty("updated_videos").GetInt32() >= 1);

        var video = await client.GetFromJsonAsync<JsonElement>("/api/videos/abc001");
        Assert.Equal("A.B.C", video.GetProperty("creator").GetString());
    }
}

