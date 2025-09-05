using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using catalog_api.Services;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<VideoRepository>();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseForwardedHeaders();
app.MapGet("/", () => Results.Redirect("/swagger"));

app.MapGet("/api/videos", (
    [Microsoft.AspNetCore.Mvc.FromServices] VideoRepository repo,
    string? q,
    string? race,
    int? page,
    int? pageSize
) =>
{
    var all = repo.All();
    IEnumerable<catalog_api.Services.VideoItem> query = all;

    // Filter by search terms
    if (!string.IsNullOrWhiteSpace(q))
    {
        var tokens = q.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);
        query = query.Where(v => tokens.All(t =>
            (v.Title?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (v.Tags != null && v.Tags.Any(tag => tag.Equals(t, StringComparison.OrdinalIgnoreCase)))
        ));
    }

    // Race filter: z|t|p
    var r = NormalizeRace(race);
    if (r != null)
    {
        query = query.Where(v => v.Tags != null && v.Tags.Any(tag => tag.Equals(r, StringComparison.OrdinalIgnoreCase)));
    }

    var total = query.Count();
    int pg = Math.Max(1, page ?? 1);
    int ps = Math.Clamp(pageSize ?? 24, 1, 100);
    var items = query
        .Skip((pg - 1) * ps)
        .Take(ps)
        .Select(v => new VideoDto(
            v.Id,
            v.Title,
            v.Creator,
            v.Description,
            v.Tags,
            v.ReleaseDate,
            v.Id
        ))
        .ToList();

    return Results.Json(new { items, totalCount = total, page = pg, pageSize = ps });
})
.WithName("GetVideos")
.WithOpenApi(o =>
{
    o.Summary = "List videos with optional search, race filter, and paging";
    o.Parameters[0].Description = "Search terms (space separated)";
    o.Parameters[1].Description = "Race code: z|t|p (omit for all)";
    o.Parameters[2].Description = "Page number (1-based)";
    o.Parameters[3].Description = "Page size (1-100, default 24)";
    return o;
});

// Ratings models (simple in-memory scaffold)
var ratingsStore = new Dictionary<(string videoId, int version), RatingAggregate>();

app.MapGet("/api/videos/{id}/ratings", (string id, int? version, HttpContext ctx) =>
{
    int v = Math.Max(1, version ?? 1);
    ratingsStore.TryGetValue((id, v), out var agg);
    agg ??= new RatingAggregate();
    var user = ctx.User?.Identity?.IsAuthenticated == true ? ctx.User.Identity!.Name : null;
    RatingValue? userRating = null;
    if (user != null && agg.UserRatings.TryGetValue(user, out var val))
    {
        userRating = val;
    }
    return Results.Json(new RatingSummary(agg.Red, agg.Yellow, agg.Green, v, userRating));
})
.WithName("GetRatings")
.WithOpenApi(o => { o.Summary = "Get rating summary (and user rating if authenticated)"; return o; });

app.MapPost("/api/videos/{id}/ratings", (string id, RatingRequest body, HttpContext ctx) =>
{
    var user = ctx.User?.Identity?.IsAuthenticated == true ? ctx.User.Identity!.Name : "anon";
    var key = (id, Math.Max(1, body.Version));
    if (!ratingsStore.TryGetValue(key, out var agg))
    {
        agg = new RatingAggregate();
        ratingsStore[key] = agg;
    }
    // If user previously rated, decrement that bucket
    if (agg.UserRatings.TryGetValue(user!, out var previous))
    {
        agg.Add(previous, -1);
    }
    agg.UserRatings[user!] = body.Value;
    agg.Add(body.Value, +1);
    return Results.Ok(new { ok = true });
})
.WithName("PostRating")
.WithOpenApi(o => { o.Summary = "Submit rating (red|yellow|green) for a version"; return o; });

app.Run();

static string? NormalizeRace(string? value)
{
    if (string.IsNullOrWhiteSpace(value)) return null;
    return value.Trim().ToLowerInvariant() switch
    {
        "z" or "zerg" => "z",
        "t" or "terran" => "t",
        "p" or "protoss" => "p",
        _ => null
    };
}

// Note: In a file that uses top-level statements, any
// type declarations must appear after all statements.
// Keeping these at the end avoids CS8803.
internal record VideoDto(
    string Id,
    string Title,
    string? Creator,
    string? Description,
    string[]? Tags,
    string? ReleaseDate,
    string YoutubeId
);

internal record RatingRequest([property: JsonConverter(typeof(JsonStringEnumConverter))] RatingValue Value, int Version);
internal record RatingSummary(int Red, int Yellow, int Green, int Version, RatingValue? UserRating);
internal enum RatingValue { red, yellow, green }
internal class RatingAggregate
{
    public Dictionary<string, RatingValue> UserRatings { get; } = new();
    public int Red { get; private set; }
    public int Yellow { get; private set; }
    public int Green { get; private set; }
    public void Add(RatingValue v, int delta)
    {
        switch (v)
        {
            case RatingValue.red: Red += delta; break;
            case RatingValue.yellow: Yellow += delta; break;
            case RatingValue.green: Green += delta; break;
        }
        Red = Math.Max(0, Red); Yellow = Math.Max(0, Yellow); Green = Math.Max(0, Green);
    }
}

public partial class Program { }
