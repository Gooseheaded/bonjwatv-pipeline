using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace bwkt_webapp.Tests;

public class AdminSubmissionDetailsTests : IClassFixture<TestWebAppFactory>, IDisposable
{
    private readonly TestWebAppFactory _factory;
    private readonly FakeCatalogApi _api;

    public AdminSubmissionDetailsTests(TestWebAppFactory factory)
    {
        _factory = factory;
        _api = new FakeCatalogApi();
    }

    public void Dispose()
    {
        _api.Dispose();
    }

    private WebApplicationFactory<Program> CreateAuthedFactory()
    {
        Environment.SetEnvironmentVariable("CATALOG_API_BASE_URL", _api.BaseUrl + "/api");
        Environment.SetEnvironmentVariable("ADMIN_USER_IDS", "TEST_ADMIN");
        var authed = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "Test";
                    options.DefaultChallengeScheme = "Test";
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
                services.PostConfigureAll<Microsoft.AspNetCore.Authentication.AuthenticationOptions>(opts =>
                {
                    opts.DefaultAuthenticateScheme = "Test";
                    opts.DefaultChallengeScheme = "Test";
                    opts.DefaultScheme = "Test";
                });
            });
        });
        return authed;
    }

    private static async Task<string> GetHtmlOrFail(System.Net.Http.HttpClient client, string path)
    {
        var res = await client.GetAsync(path);
        var html = await res.Content.ReadAsStringAsync();
        if (res.StatusCode != HttpStatusCode.OK)
        {
            var snippet = html.Length > 600 ? html.Substring(0, 600) : html;
            throw new Xunit.Sdk.XunitException($@"GET {path} -> {(int)res.StatusCode} {res.StatusCode}
Body head:
{snippet}");
        }
        return html;
    }

    [Fact]
    public async Task Submission_Detail_Shows_Player_Overlay_And_Diff_With_Coverage()
    {
        var client = CreateAuthedFactory().CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        var html = await GetHtmlOrFail(client, "/Admin/Submission?id=sub-up-2");

        // Player + overlay elements (similar to Preview page)
        Assert.Contains("admin-preview-player", html); // iframe id
        Assert.Contains("admin-subtitle-container", html); // overlay container
        // Font size controls present
        Assert.Contains("A-", html);
        Assert.Contains("A+", html);

        // Diff textareas
        Assert.Contains("id=\"diff-left\"", html);
        Assert.Contains("id=\"diff-right\"", html);
        // Coverage badges
        Assert.Contains("id=\"cov-old\"", html);
        Assert.Contains("id=\"cov-new\"", html);

        // Scripts included (YouTube API + subtitles parser)
        Assert.Contains("iframe_api", html);
        Assert.Contains("/js/subtitles.js", html);
    }

    private class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, ISystemClock clock)
            : base(options, logger, encoder, clock) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "TEST_ADMIN"),
                new Claim(ClaimTypes.Name, "Test Admin")
            };
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private class FakeCatalogApi : IDisposable
    {
        private readonly IHost _host;
        public string BaseUrl { get; }

        public FakeCatalogApi()
        {
            var port = GetFreeTcpPort();
            BaseUrl = $"http://127.0.0.1:{port}";
            _host = new HostBuilder()
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder.UseKestrel().UseUrls(BaseUrl);
                    webBuilder.ConfigureServices(services => { services.AddRouting(); });
                    webBuilder.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            // Minimal endpoints to drive the page
                            endpoints.MapGet("/api/admin/submissions/{id}", async (HttpContext ctx, string id) =>
                            {
                                if (id == "sub-up-2")
                                {
                                    var obj = new { id = id, status = "pending", submitted_at = DateTimeOffset.UtcNow, submitted_by = "u",
                                        payload = new { youtube_id = "existsABC", title = "Update Title" } };
                                    ctx.Response.ContentType = "application/json";
                                    await ctx.Response.WriteAsync(JsonSerializer.Serialize(obj));
                                    return;
                                }
                                ctx.Response.StatusCode = 404;
                            });
                            // Submission preview text (new subtitles)
                            endpoints.MapGet("/api/admin/submissions/{id}/subtitle", async (HttpContext ctx, string id) =>
                            {
                                if (id == "sub-up-2")
                                {
                                    ctx.Response.ContentType = "text/plain; charset=utf-8";
                                    await ctx.Response.WriteAsync("1\n00:00:01,000 --> 00:00:02,000\nNEW\n");
                                    return;
                                }
                                ctx.Response.StatusCode = 404;
                            });
                            // Existing video detail with subtitleUrl
                            endpoints.MapGet("/api/videos/{id}", async (HttpContext ctx, string id) =>
                            {
                                if (id == "existsABC")
                                {
                                    var o = new { id = id, subtitleUrl = "/api/subtitles/existsABC/1.srt" };
                                    ctx.Response.ContentType = "application/json";
                                    await ctx.Response.WriteAsync(JsonSerializer.Serialize(o));
                                    return;
                                }
                                ctx.Response.StatusCode = 404;
                            });
                            // Serve existing first-party subtitle text
                            endpoints.MapGet("/api/subtitles/{videoId}/{version}.srt", async (HttpContext ctx, string videoId, int version) =>
                            {
                                if (videoId == "existsABC" && version == 1)
                                {
                                    ctx.Response.ContentType = "text/plain; charset=utf-8";
                                    await ctx.Response.WriteAsync("1\n00:00:01,000 --> 00:00:02,000\nOLD\n");
                                    return;
                                }
                                ctx.Response.StatusCode = 404;
                            });
                        });
                    });
                })
                .Build();
            _host.Start();
        }

        public void Dispose()
        {
            try { _host.StopAsync().GetAwaiter().GetResult(); } catch { }
            _host.Dispose();
        }

        private static int GetFreeTcpPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}

