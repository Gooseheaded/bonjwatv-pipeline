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
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Text.Encodings.Web;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace bwkt_webapp.Tests;

public class AdminSubmissionTypeTests : IClassFixture<TestWebAppFactory>, IDisposable
{
    private readonly TestWebAppFactory _factory;
    private readonly FakeCatalogApi _api;

    public AdminSubmissionTypeTests(TestWebAppFactory factory)
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


    private static async System.Threading.Tasks.Task<string> GetHtmlOrFail(System.Net.Http.HttpClient client, string path)
    {
        var res = await client.GetAsync(path);
        var html = await res.Content.ReadAsStringAsync();
        if (res.StatusCode != System.Net.HttpStatusCode.OK)
        {
            var snippet = html.Length > 600 ? html.Substring(0, 600) : html;
            throw new Xunit.Sdk.XunitException($"GET {path} -> {(int)res.StatusCode} {res.StatusCode}
Body head:
{snippet}");
        }
        return html;
    }

    [Fact]
    public async Task AdminIndex_Shows_Type_Badges()
    {
        var client = CreateAuthedFactory().CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        var html = await GetHtmlOrFail(client, "/Admin");
        var head = html.Length > 600 ? html.Substring(0, 600) : html;
        Assert.Contains("Type", html);
        Assert.Contains("🆕 New", html);
        Assert.Contains("✏️ Update", html);
    }

    [Fact]
    public async Task AdminSubmission_Shows_Type_Meta()
    {
        var client = CreateAuthedFactory().CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        // Existing (update)
        var html1 = await GetHtmlOrFail(client, "/Admin/Submission?id=sub-update-1");
        Assert.True(html1.Contains("Type") || html1.Contains("badge bg-secondary"),
            $"Expected update indicator on detail page. Head:\n{(html1.Length>600?html1.Substring(0,600):html1)}");
        Assert.True(html1.Contains("Update (existing video)") || html1.Contains("badge bg-secondary"),
            $"Expected update indicator text or badge. Head:\n{(html1.Length>600?html1.Substring(0,600):html1)}");
        // New
        var html2 = await GetHtmlOrFail(client, "/Admin/Submission?id=sub-new-1");
        Assert.True(html2.Contains("Type") || html2.Contains("badge bg-success"),
            $"Expected new indicator on detail page. Head:\n{(html2.Length>600?html2.Substring(0,600):html2)}");
        Assert.True(html2.Contains("New (not in catalog)") || html2.Contains("badge bg-success"),
            $"Expected new indicator text or badge. Head:\n{(html2.Length>600?html2.Substring(0,600):html2)}");
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
                            endpoints.MapGet("/api/admin/submissions", async ctx =>
                        {
                            // Return two items: one new, one update
                            var items = new[]
                            {
                                new { id = "sub-new-1", status = "pending", submitted_at = DateTimeOffset.UtcNow, submitted_by = "user1",
                                    payload = new { youtube_id = "new123", title = "New Title" } },
                                new { id = "sub-update-1", status = "pending", submitted_at = DateTimeOffset.UtcNow, submitted_by = "user2",
                                    payload = new { youtube_id = "exists123", title = "Update Title" } },
                            };
                            var json = JsonSerializer.Serialize(new { items });
                            ctx.Response.ContentType = "application/json";
                            await ctx.Response.WriteAsync(json);
                        });
                            endpoints.MapGet("/api/admin/submissions/{id}", async (HttpContext ctx, string id) =>
                        {
                            if (id == "sub-new-1")
                            {
                                var obj = new { id = id, status = "pending", submitted_at = DateTimeOffset.UtcNow, submitted_by = "user1",
                                    payload = new { youtube_id = "new123", title = "New Title" } };
                                ctx.Response.ContentType = "application/json";
                                await ctx.Response.WriteAsync(JsonSerializer.Serialize(obj));
                                return;
                            }
                            if (id == "sub-update-1")
                            {
                                var obj = new { id = id, status = "pending", submitted_at = DateTimeOffset.UtcNow, submitted_by = "user2",
                                    payload = new { youtube_id = "exists123", title = "Update Title" } };
                                ctx.Response.ContentType = "application/json";
                                await ctx.Response.WriteAsync(JsonSerializer.Serialize(obj));
                                return;
                            }
                            ctx.Response.StatusCode = 404;
                        });
                            endpoints.MapGet("/api/videos/{id}", async (HttpContext ctx, string id) =>
                        {
                            if (id == "exists123")
                            {
                                ctx.Response.ContentType = "application/json";
                                await ctx.Response.WriteAsync("{\"id\":\"exists123\"}");
                            }
                            else
                            {
                                ctx.Response.StatusCode = 404;
                            }
                        });
                            endpoints.MapGet("/api/admin/submissions/{id}/subtitle", ctx => { ctx.Response.StatusCode = 404; return Task.CompletedTask; });
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
