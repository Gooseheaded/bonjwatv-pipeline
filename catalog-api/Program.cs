using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/", () => Results.Redirect("/swagger"));

app.MapGet("/api/videos", () =>
{
    // Phase 0: Read from webapp local JSON (dev) as a stopgap until DB is added.
    // Monorepo layout assumption: ../webapp/data/videos.json relative to API project.
    var root = Directory.GetCurrentDirectory();
    var jsonPath = Path.GetFullPath(Path.Combine(root, "..", "webapp", "data", "videos.json"));
    if (!File.Exists(jsonPath))
    {
        return Results.Json(new { items = Array.Empty<VideoDto>(), totalCount = 0 });
    }

    using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
    var items = new List<VideoDto>();
    foreach (var el in doc.RootElement.EnumerateArray())
    {
        string youtubeId = el.TryGetProperty("v", out var vProp) ? (vProp.GetString() ?? "") : "";
        string title = el.TryGetProperty("title", out var t) ? (t.GetString() ?? "") : "";
        string? creator = el.TryGetProperty("creator", out var c) ? c.GetString() : null;
        string? description = el.TryGetProperty("description", out var d) ? d.GetString() : null;
        string? releaseDate = el.TryGetProperty("releaseDate", out var rd) ? rd.GetString() : null;
        string[]? tags = null;
        if (el.TryGetProperty("tags", out var tg) && tg.ValueKind == JsonValueKind.Array)
        {
            tags = tg.EnumerateArray()
                     .Select(e => e.GetString() ?? "")
                     .Where(s => !string.IsNullOrWhiteSpace(s))
                     .ToArray();
        }

        items.Add(new VideoDto(youtubeId, title, creator, description, tags, releaseDate, youtubeId));
    }

    return Results.Json(new { items, totalCount = items.Count });
})
.WithName("GetVideos");

app.Run();

// Note: In a file that uses top-level statements, any
// type declarations must appear after all statements.
// Keeping this record at the end avoids CS8803.
internal record VideoDto(
    string Id,
    string Title,
    string? Creator,
    string? Description,
    string[]? Tags,
    string? ReleaseDate,
    string YoutubeId
);
