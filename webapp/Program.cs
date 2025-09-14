using System.Security.Claims;
using bwkt_webapp.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// Register application services
builder.Services.AddSingleton<IVideoService, VideoService>();
builder.Services.AddSingleton<IRatingsClient, HttpRatingsClient>();
// Generate all URLs in lowercase
builder.Services.AddRouting(options => options.LowercaseUrls = true);
builder.Services.AddRazorPages();
builder.Services.AddSingleton<DiscordOAuthService>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/account/login";
        options.LogoutPath = "/account/logout";
        options.Cookie.Name = "bwkt.auth";
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();

// Persist Data Protection keys to disk so auth cookies survive restarts/redeploys
try
{
    var keysDir = Path.Combine("/app/data", "keys");
    Directory.CreateDirectory(keysDir);
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(keysDir))
        .SetApplicationName("bwkt-webapp");
}
catch
{
    // If the directory isn't writable (e.g., dev read-only bind), fall back silently.
}

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

// OAuth callback: exchanges code with Discord (or mock), signs in user
app.MapGet("/account/callback", async (HttpContext ctx, DiscordOAuthService oauth) =>
{
    var code = ctx.Request.Query["code"].ToString();
    if (string.IsNullOrWhiteSpace(code)) return Results.BadRequest("Missing code");

    // Dev-friendly short-circuit: allow demo/mock code without Discord credentials
    if (string.Equals(code, "demo", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(code, "mock", StringComparison.OrdinalIgnoreCase))
    {
        var demoClaims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Name, "Demo User"),
            new Claim("provider", "discord")
        };
        var demoIdentity = new ClaimsIdentity(demoClaims, CookieAuthenticationDefaults.AuthenticationScheme);
        var demoPrincipal = new ClaimsPrincipal(demoIdentity);
        await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, demoPrincipal);
        return Results.Redirect("/");
    }

    // In Development, always use current host to simplify local login
    var envName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
    var isDevEnv = string.Equals(envName, "Development", StringComparison.OrdinalIgnoreCase);
    var callback = isDevEnv
        ? ($"{ctx.Request.Scheme}://{ctx.Request.Host}/account/callback")
        : (Environment.GetEnvironmentVariable("OAUTH_CALLBACK_URL") ?? $"{ctx.Request.Scheme}://{ctx.Request.Host}/account/callback");
    var (okToken, accessToken, err1) = await oauth.ExchangeCodeAsync(code, callback);
    if (!okToken || string.IsNullOrWhiteSpace(accessToken)) return Results.BadRequest(err1 ?? "OAuth token error");

    var (okUser, user, err2) = await oauth.GetUserAsync(accessToken!);
    if (!okUser || user == null || string.IsNullOrWhiteSpace(user.Id)) return Results.BadRequest(err2 ?? "OAuth user error");

    var display = string.IsNullOrWhiteSpace(user.GlobalName) ? user.Username : user.GlobalName;
    var claims = new List<Claim>
    {
        new Claim(ClaimTypes.NameIdentifier, user.Id!),
        new Claim(ClaimTypes.Name, display ?? user.Username ?? "User"),
        new Claim("provider", "discord"),
    };
    if (!string.IsNullOrWhiteSpace(user.Avatar))
    {
        claims.Add(new Claim("avatar", user.Avatar!));
    }
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    var principal = new ClaimsPrincipal(identity);
    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
    return Results.Redirect("/");
});

app.MapPost("/account/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/");
});

// Ratings proxy endpoints (server-side call to catalog-api)
var apiVideosUrl = Environment.GetEnvironmentVariable("DATA_CATALOG_URL");
var explicitApiBase = Environment.GetEnvironmentVariable("CATALOG_API_BASE_URL");
string? DeriveApiBase()
{
    if (!string.IsNullOrWhiteSpace(explicitApiBase)) return explicitApiBase!.TrimEnd('/');
    if (string.IsNullOrWhiteSpace(apiVideosUrl)) return null;
    try
    {
        var uri = new Uri(apiVideosUrl!);
        var path = uri.AbsolutePath.TrimEnd('/');
        // Trim trailing /videos if present → want the /api base
        if (path.EndsWith("/videos", StringComparison.OrdinalIgnoreCase))
        {
            path = path.Substring(0, path.Length - "/videos".Length);
        }
        var builderUri = new UriBuilder(uri.Scheme, uri.Host, uri.Port, path);
        return builderUri.Uri.ToString().TrimEnd('/');
    }
    catch { return null; }
}

var apiBase = DeriveApiBase();

app.MapGet("/ratings/{id}", async (string id, int? version, HttpContext ctx) =>
{
    if (string.IsNullOrWhiteSpace(apiBase)) return Results.StatusCode(503);
    var v = Math.Max(1, version ?? 1);
    var url = $"{apiBase}/videos/{id}/ratings?version={v}";
    using var http = new HttpClient();
    using var message = new HttpRequestMessage(HttpMethod.Get, url);
    if (ctx.User?.Identity?.IsAuthenticated ?? false)
    {
        var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var userName = ctx.User.Identity?.Name;
        if (!string.IsNullOrWhiteSpace(userId)) message.Headers.Add("X-User-Id", userId);
        if (!string.IsNullOrWhiteSpace(userName)) message.Headers.Add("X-User-Name", userName);
    }
    var resp = await http.SendAsync(message);
    var contentType = resp.Content.Headers.ContentType?.ToString() ?? "application/json";
    var stream = await resp.Content.ReadAsStreamAsync();
    return Results.Stream(stream, contentType);
});

app.MapPost("/ratings/{id}", async (string id, HttpRequest req, HttpContext ctx) =>
{
    if (string.IsNullOrWhiteSpace(apiBase)) return Results.StatusCode(503);
    using var sr = new StreamReader(req.Body);
    var body = await sr.ReadToEndAsync();
    var url = $"{apiBase}/videos/{id}/ratings";
    using var http = new HttpClient();
    using var message = new HttpRequestMessage(HttpMethod.Post, url)
    {
        Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
    };
    if (ctx.User?.Identity?.IsAuthenticated ?? false)
    {
        var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var userName = ctx.User.Identity?.Name;
        if (!string.IsNullOrWhiteSpace(userId)) message.Headers.Add("X-User-Id", userId);
        if (!string.IsNullOrWhiteSpace(userName)) message.Headers.Add("X-User-Name", userName);
    }
    var resp = await http.SendAsync(message);
    if (!resp.IsSuccessStatusCode)
    {
        return Results.StatusCode((int)resp.StatusCode);
    }
    return Results.Ok(new { ok = true });
}).RequireAuthorization();

// Subtitles proxy to avoid mixed-content/CORS issues
app.MapGet("/subtitles/{id}/{version}.srt", async (string id, int version, HttpContext ctx) =>
{
    if (string.IsNullOrWhiteSpace(apiBase)) return Results.StatusCode(503);
    using var http = new HttpClient();
    // Try first-party subtitle from Catalog API
    var url = $"{apiBase}/subtitles/{id}/{version}.srt";
    var resp = await http.GetAsync(url);
    if (resp.IsSuccessStatusCode)
    {
        var ct = resp.Content.Headers.ContentType?.ToString() ?? "text/plain; charset=utf-8";
        var stream = await resp.Content.ReadAsStreamAsync();
        return Results.Stream(stream, ct);
    }
    if ((int)resp.StatusCode != 404)
    {
        return Results.StatusCode((int)resp.StatusCode);
    }
    // Fallback: consult video detail for an external subtitleUrl and proxy it
    try
    {
        var vresp = await http.GetAsync($"{apiBase}/videos/{id}");
        if (vresp.IsSuccessStatusCode)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(await vresp.Content.ReadAsStringAsync());
            if (doc.RootElement.TryGetProperty("subtitleUrl", out var suEl))
            {
                var su = suEl.GetString() ?? string.Empty;
                if (su.StartsWith("http://") || su.StartsWith("https://"))
                {
                    var ext = await http.GetAsync(su);
                    if (ext.IsSuccessStatusCode)
                    {
                        var bytes = await ext.Content.ReadAsByteArrayAsync();
                        // Fire-and-forget upload to Catalog API to persist first-party copy
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                using var form = new MultipartFormDataContent();
                                form.Add(new StringContent(id), "videoId");
                                form.Add(new StringContent(version.ToString()), "version");
                                var fileContent = new ByteArrayContent(bytes);
                                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
                                form.Add(fileContent, "file", $"{id}-v{version}.srt");
                                using var hc = new HttpClient();
                                var ingest = Environment.GetEnvironmentVariable("LEGACY_SUBTITLE_API_TOKEN");
                                if (!string.IsNullOrWhiteSpace(ingest))
                                {
                                    hc.DefaultRequestHeaders.Add("X-Api-Key", ingest);
                                }
                                await hc.PostAsync($"{apiBase}/uploads/subtitles", form);
                            }
                            catch { /* swallow */ }
                        });
                        var ct2 = ext.Content.Headers.ContentType?.ToString() ?? "text/plain; charset=utf-8";
                        return Results.File(bytes, ct2);
                    }
                    return Results.StatusCode((int)ext.StatusCode);
                }
            }
        }
    }
    catch { }
    return Results.StatusCode(404);
});

app.MapDelete("/ratings/{id}", async (string id, int? version, HttpContext ctx) =>
{
    if (string.IsNullOrWhiteSpace(apiBase)) return Results.StatusCode(503);
    var v = Math.Max(1, version ?? 1);
    var url = $"{apiBase}/videos/{id}/ratings?version={v}";
    using var http = new HttpClient();
    using var message = new HttpRequestMessage(HttpMethod.Delete, url);
    if (ctx.User?.Identity?.IsAuthenticated ?? false)
    {
        var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var userName = ctx.User.Identity?.Name;
        if (!string.IsNullOrWhiteSpace(userId)) message.Headers.Add("X-User-Id", userId);
        if (!string.IsNullOrWhiteSpace(userName)) message.Headers.Add("X-User-Name", userName);
    }
    var resp = await http.SendAsync(message);
    return Results.StatusCode((int)resp.StatusCode);
}).RequireAuthorization();

// Admin proxy: recent ratings (authorized by simple allowlist of admin user IDs)
static bool IsAdmin(HttpContext ctx)
{
    var ids = Environment.GetEnvironmentVariable("ADMIN_USER_IDS");
    if (string.IsNullOrWhiteSpace(ids)) return false;
    var current = ctx.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    return !string.IsNullOrWhiteSpace(current) && ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(current);
}

app.MapGet("/admin/ratings/recent", async (int? limit, HttpContext ctx) =>
{
    if (!(ctx.User?.Identity?.IsAuthenticated ?? false) || !IsAdmin(ctx)) return Results.StatusCode(403);
    if (string.IsNullOrWhiteSpace(apiBase)) return Results.StatusCode(503);
    var url = $"{apiBase}/admin/ratings/recent?limit={Math.Max(1, limit ?? 50)}";
    using var http = new HttpClient();
    var resp = await http.GetAsync(url);
    if (!resp.IsSuccessStatusCode) return Results.StatusCode((int)resp.StatusCode);
    var contentType = resp.Content.Headers.ContentType?.ToString() ?? "application/json";
    var stream = await resp.Content.ReadAsStreamAsync();
    return Results.Stream(stream, contentType);
});

// (diagnostics endpoint removed)

// Admin proxy: submissions list and approve/reject
app.MapGet("/admin/submissions", async (string? status, int? page, int? pageSize, HttpContext ctx) =>
{
    if (!(ctx.User?.Identity?.IsAuthenticated ?? false) || !IsAdmin(ctx)) return Results.StatusCode(403);
    if (string.IsNullOrWhiteSpace(apiBase)) return Results.StatusCode(503);
    var url = $"{apiBase}/admin/submissions?status={Uri.EscapeDataString(status ?? "pending")}&page={Math.Max(1, page ?? 1)}&pageSize={Math.Clamp(pageSize ?? 50, 1, 100)}";
    using var http = new HttpClient();
    var resp = await http.GetAsync(url);
    if (!resp.IsSuccessStatusCode) return Results.StatusCode((int)resp.StatusCode);
    var contentType = resp.Content.Headers.ContentType?.ToString() ?? "application/json";
    var stream = await resp.Content.ReadAsStreamAsync();
    return Results.Stream(stream, contentType);
});

// Admin proxy: videos hide/show/delete and list hidden
app.MapGet("/admin/videos/hidden", async (HttpContext ctx) =>
{
    if (!(ctx.User?.Identity?.IsAuthenticated ?? false) || !IsAdmin(ctx)) return Results.StatusCode(403);
    if (string.IsNullOrWhiteSpace(apiBase)) return Results.StatusCode(503);
    using var http = new HttpClient();
    var resp = await http.GetAsync($"{apiBase}/admin/videos/hidden");
    if (!resp.IsSuccessStatusCode) return Results.StatusCode((int)resp.StatusCode);
    var ct = resp.Content.Headers.ContentType?.ToString() ?? "application/json";
    var stream = await resp.Content.ReadAsStreamAsync();
    return Results.Stream(stream, ct);
});

app.MapGet("/admin/videos/{id}", async (string id, HttpContext ctx) =>
{
    if (!(ctx.User?.Identity?.IsAuthenticated ?? false) || !IsAdmin(ctx)) return Results.StatusCode(403);
    if (string.IsNullOrWhiteSpace(apiBase)) return Results.StatusCode(503);
    using var http = new HttpClient();
    var resp = await http.GetAsync($"{apiBase}/admin/videos/{id}");
    if (!resp.IsSuccessStatusCode) return Results.StatusCode((int)resp.StatusCode);
    var ct = resp.Content.Headers.ContentType?.ToString() ?? "application/json";
    var stream = await resp.Content.ReadAsStreamAsync();
    return Results.Stream(stream, ct);
});

// Admin proxy: set video durationSeconds
app.MapPatch("/admin/videos/{id}/duration", async (string id, HttpRequest req, HttpContext ctx) =>
{
    if (!(ctx.User?.Identity?.IsAuthenticated ?? false) || !IsAdmin(ctx)) return Results.StatusCode(403);
    if (string.IsNullOrWhiteSpace(apiBase)) return Results.StatusCode(503);
    using var sr = new StreamReader(req.Body);
    var body = await sr.ReadToEndAsync();
    using var http = new HttpClient();
    using var msg = new HttpRequestMessage(HttpMethod.Patch, $"{apiBase}/admin/videos/{id}/duration")
    {
        Content = new StringContent(string.IsNullOrWhiteSpace(body) ? "{}" : body, System.Text.Encoding.UTF8, "application/json")
    };
    var resp = await http.SendAsync(msg);
    if (!resp.IsSuccessStatusCode) return Results.StatusCode((int)resp.StatusCode);
    return Results.Ok(new { ok = true });
});

app.MapPost("/admin/videos/{id}/hide", async (string id, HttpRequest req, HttpContext ctx) =>
{
    if (!(ctx.User?.Identity?.IsAuthenticated ?? false) || !IsAdmin(ctx)) return Results.StatusCode(403);
    if (string.IsNullOrWhiteSpace(apiBase)) return Results.StatusCode(503);
    var form = await req.ReadFormAsync();
    var reason = form["reason"].FirstOrDefault() ?? "Hidden via UI";
    using var http = new HttpClient();
    using var msg = new HttpRequestMessage(HttpMethod.Patch, $"{apiBase}/admin/videos/{id}/hide")
    {
        Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new { reason }), System.Text.Encoding.UTF8, "application/json")
    };
    var resp = await http.SendAsync(msg);
    if (!resp.IsSuccessStatusCode) return Results.StatusCode((int)resp.StatusCode);
    return Results.Redirect("/Admin?ok=1&status=hidden");
});

app.MapPost("/admin/videos/{id}/show", async (string id, HttpContext ctx) =>
{
    if (!(ctx.User?.Identity?.IsAuthenticated ?? false) || !IsAdmin(ctx)) return Results.StatusCode(403);
    if (string.IsNullOrWhiteSpace(apiBase)) return Results.StatusCode(503);
    using var http = new HttpClient();
    var resp = await http.PatchAsync($"{apiBase}/admin/videos/{id}/show", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
    if (!resp.IsSuccessStatusCode) return Results.StatusCode((int)resp.StatusCode);
    return Results.Redirect("/Admin?ok=1&status=shown");
});

app.MapPost("/admin/videos/{id}/delete", async (string id, HttpContext ctx) =>
{
    if (!(ctx.User?.Identity?.IsAuthenticated ?? false) || !IsAdmin(ctx)) return Results.StatusCode(403);
    if (string.IsNullOrWhiteSpace(apiBase)) return Results.StatusCode(503);
    using var http = new HttpClient();
    var resp = await http.DeleteAsync($"{apiBase}/admin/videos/{id}");
    if (!resp.IsSuccessStatusCode) return Results.StatusCode((int)resp.StatusCode);
    return Results.Redirect("/Admin?ok=1&status=deleted");
});

// Admin proxy: batch/individual tag management
app.MapPatch("/admin/videos/{id}/tags", async (string id, HttpRequest req, HttpContext ctx) =>
{
    if (!(ctx.User?.Identity?.IsAuthenticated ?? false) || !IsAdmin(ctx)) return Results.StatusCode(403);
    if (string.IsNullOrWhiteSpace(apiBase)) return Results.StatusCode(503);
    using var sr = new StreamReader(req.Body);
    var body = await sr.ReadToEndAsync();
    using var http = new HttpClient();
    using var msg = new HttpRequestMessage(HttpMethod.Patch, $"{apiBase}/admin/videos/{id}/tags")
    {
        Content = new StringContent(body ?? "{}", System.Text.Encoding.UTF8, "application/json")
    };
    var resp = await http.SendAsync(msg);
    if (!resp.IsSuccessStatusCode) return Results.StatusCode((int)resp.StatusCode);
    return Results.Ok(new { ok = true });
});

app.MapPost("/admin/videos/{id}/tags/set", async (string id, HttpRequest req, HttpContext ctx) =>
{
    if (!(ctx.User?.Identity?.IsAuthenticated ?? false) || !IsAdmin(ctx)) return Results.StatusCode(403);
    if (string.IsNullOrWhiteSpace(apiBase)) return Results.StatusCode(503);
    var form = await req.ReadFormAsync();
    var tags = form["tags"].ToArray();
    using var http = new HttpClient();
    var payload = System.Text.Json.JsonSerializer.Serialize(new { action = "set", tags });
    using var msg = new HttpRequestMessage(HttpMethod.Patch, $"{apiBase}/admin/videos/{id}/tags")
    {
        Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
    };
    var resp = await http.SendAsync(msg);
    if (!resp.IsSuccessStatusCode) return Results.StatusCode((int)resp.StatusCode);
    return Results.Redirect($"/Admin/Video?id={Uri.EscapeDataString(id)}&ok=1");
});

app.MapGet("/admin/submissions/{id}", async (string id, HttpContext ctx) =>
{
    if (!(ctx.User?.Identity?.IsAuthenticated ?? false) || !IsAdmin(ctx)) return Results.StatusCode(403);
    if (string.IsNullOrWhiteSpace(apiBase)) return Results.StatusCode(503);
    using var http = new HttpClient();
    var url = $"{apiBase}/admin/submissions/{id}";
    var resp = await http.GetAsync(url);
    if (!resp.IsSuccessStatusCode) return Results.StatusCode((int)resp.StatusCode);
    var contentType = resp.Content.Headers.ContentType?.ToString() ?? "application/json";
    var stream = await resp.Content.ReadAsStreamAsync();
    return Results.Stream(stream, contentType);
});

app.MapPost("/admin/submissions/{id}/approve", async (string id, HttpContext ctx) =>
{
    if (!(ctx.User?.Identity?.IsAuthenticated ?? false) || !IsAdmin(ctx)) return Results.StatusCode(403);
    if (string.IsNullOrWhiteSpace(apiBase)) return Results.StatusCode(503);
    using var http = new HttpClient();
    var url = $"{apiBase}/admin/submissions/{id}";
    // Fetch youtube_id before approving, so we can link to Watch after success
    string? youtubeId = null;
    try
    {
        var pre = await http.GetAsync(url);
        if (pre.IsSuccessStatusCode)
        {
            var json = await pre.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("payload", out var payload) && payload.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                if (payload.TryGetProperty("youtube_id", out var y)) youtubeId = y.GetString();
            }
        }
    }
    catch { /* non-fatal */ }
    using var msg = new HttpRequestMessage(HttpMethod.Patch, url)
    {
        Content = new StringContent("{\"action\":\"approve\"}", System.Text.Encoding.UTF8, "application/json")
    };
    var resp = await http.SendAsync(msg);
    if (!resp.IsSuccessStatusCode) return Results.StatusCode((int)resp.StatusCode);
    // Always redirect back to Admin page (broad UI) with banner params
    if (!string.IsNullOrWhiteSpace(youtubeId))
    {
        return Results.Redirect($"/Admin?ok=1&status=approved&v={Uri.EscapeDataString(youtubeId!)}");
    }
    return Results.Redirect("/Admin?ok=1&status=approved");
});

app.MapPost("/admin/submissions/{id}/reject", async (string id, HttpRequest req, HttpContext ctx) =>
{
    if (!(ctx.User?.Identity?.IsAuthenticated ?? false) || !IsAdmin(ctx)) return Results.StatusCode(403);
    if (string.IsNullOrWhiteSpace(apiBase)) return Results.StatusCode(503);
    var form = await req.ReadFormAsync();
    var reason = form["reason"].FirstOrDefault() ?? "Rejected";
    using var http = new HttpClient();
    var url = $"{apiBase}/admin/submissions/{id}";
    var body = System.Text.Json.JsonSerializer.Serialize(new { action = "reject", reason });
    using var msg = new HttpRequestMessage(HttpMethod.Patch, url)
    {
        Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
    };
    var resp = await http.SendAsync(msg);
    if (!resp.IsSuccessStatusCode) return Results.StatusCode((int)resp.StatusCode);
    return Results.Redirect("/Admin?ok=1&status=rejected");
});

app.Run();

public partial class Program { }
