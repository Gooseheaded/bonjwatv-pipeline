using System.Security.Claims;
using bwkt_webapp.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// Register application services
builder.Services.AddSingleton<IVideoService, VideoService>();
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

    var callback = Environment.GetEnvironmentVariable("OAUTH_CALLBACK_URL")
                   ?? ($"{ctx.Request.Scheme}://{ctx.Request.Host}/account/callback");
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

app.MapGet("/ratings/{id}", async (string id, int? version) =>
{
    if (string.IsNullOrWhiteSpace(apiBase)) return Results.StatusCode(503);
    var v = Math.Max(1, version ?? 1);
    var url = $"{apiBase}/videos/{id}/ratings?version={v}";
    using var http = new HttpClient();
    var resp = await http.GetAsync(url);
    var contentType = resp.Content.Headers.ContentType?.ToString() ?? "application/json";
    var stream = await resp.Content.ReadAsStreamAsync();
    return Results.Stream(stream, contentType);
});

app.MapPost("/ratings/{id}", async (string id, HttpRequest req) =>
{
    if (string.IsNullOrWhiteSpace(apiBase)) return Results.StatusCode(503);
    using var sr = new StreamReader(req.Body);
    var body = await sr.ReadToEndAsync();
    var url = $"{apiBase}/videos/{id}/ratings";
    using var http = new HttpClient();
    using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
    var resp = await http.PostAsync(url, content);
    if (!resp.IsSuccessStatusCode)
    {
        return Results.StatusCode((int)resp.StatusCode);
    }
    return Results.Ok(new { ok = true });
}).RequireAuthorization();

app.Run();

public partial class Program { }
