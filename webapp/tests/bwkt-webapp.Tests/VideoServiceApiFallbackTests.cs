using System.Text.Json;
using bwkt_webapp.Models;
using bwkt_webapp.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace bwkt_webapp.Tests;

public class VideoServiceApiFallbackTests
{
    private class TestEnv : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = Path.GetFullPath(".");
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(Path.GetFullPath("."));
        public string WebRootPath { get; set; } = Path.GetFullPath(".");
        public IFileProvider WebRootFileProvider { get; set; } = new PhysicalFileProvider(Path.GetFullPath("."));
    }

    [Fact]
    public async Task UsesApi_WhenConfigured()
    {
        // Start a minimal API server serving two items
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.MapGet("/api/videos", () => new
        {
            items = new[]
            {
                new { id = "api1", title = "From API", tags = new [] { "z" } },
            },
            totalCount = 1, page = 1, pageSize = 100
        });
        await app.StartAsync();
        var baseUrl = app.Urls.First();

        try
        {
            Environment.SetEnvironmentVariable("DATA_CATALOG_URL", baseUrl + "/api/videos");
            var env = new TestEnv { ContentRootPath = Directory.GetCurrentDirectory() };
            var svc = new VideoService(env);
            var results = svc.Search("From", "z").ToList();
            Assert.Single(results);
            Assert.Equal("api1", results[0].VideoId);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
            Environment.SetEnvironmentVariable("DATA_CATALOG_URL", null);
        }
    }
}
