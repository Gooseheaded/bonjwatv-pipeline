using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using catalog_api.Services;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<VideoRepository>();
builder.Services.AddSingleton<RatingsRepository>();
builder.Services.AddSingleton<SubmissionsRepository>();
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

// Health endpoint
app.MapGet("/healthz", () => Results.Json(new { ok = true }))
   .WithOpenApi(o => { o.Summary = "Health check"; return o; });

// Readiness endpoint: verifies videos store path is accessible
app.MapGet("/readyz", () =>
{
    try
    {
        var path = VideosStorePath();
        var dir = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(dir)) return Results.StatusCode(500);
        Directory.CreateDirectory(dir);
        if (!File.Exists(path))
        {
            File.WriteAllText(path, "[]");
        }
        using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Results.Json(new { ok = true, store = path });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message }, statusCode: 500);
    }
}).WithOpenApi(o => { o.Summary = "Readiness check for store path"; return o; });

app.MapGet("/api/videos", (
    [Microsoft.AspNetCore.Mvc.FromServices] VideoRepository repo,
    [Microsoft.AspNetCore.Mvc.FromServices] RatingsRepository ratings,
    string? q,
    string? race,
    int? page,
    int? pageSize,
    string? sortBy
) =>
{
    var all = repo.All();
    IEnumerable<catalog_api.Services.VideoItem> query = all.Where(v => v.Hidden != true);

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

    // Optional sorting
    var sb = (sortBy ?? string.Empty).Trim().ToLowerInvariant();
    if (sb == "rating_desc")
    {
        double Score(string id)
        {
            var sum = ratings.GetSummary(id, 1, null);
            int red = sum.Red; int yellow = sum.Yellow; int green = sum.Green;
            int n = Math.Max(0, red + yellow + green);
            if (n <= 0) return 0;
            double pos = green + 0.5 * yellow;
            double p = pos / n;
            double z = 1.96; double z2 = z * z;
            double denom = 1 + z2 / n;
            double centre = p + z2 / (2 * n);
            double adj = z * Math.Sqrt((p * (1 - p) + z2 / (4 * n)) / n);
            return (centre - adj) / denom;
        }
        int Total(string id)
        {
            var sum = ratings.GetSummary(id, 1, null);
            return Math.Max(0, sum.Red + sum.Yellow + sum.Green);
        }
        query = query.OrderByDescending(v => Score(v.Id))
                     .ThenByDescending(v => Total(v.Id))
                     .ThenBy(v => v.Title);
    }

    var total = query.Count();
    int pg = Math.Max(1, page ?? 1);
    int ps = Math.Clamp(pageSize ?? 24, 1, 100);
    var items = query
        .Skip((pg - 1) * ps)
        .Take(ps)
        .Select(v => {
            var sum = ratings.GetSummary(v.Id, 1, null);
            return new VideoDto(
                v.Id,
                v.Title,
                v.Creator,
                v.Description,
                v.Tags,
                v.ReleaseDate,
                v.Id,
                v.SubtitleUrl,
                sum.Red,
                sum.Yellow,
                sum.Green
            );
        })
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
    if (o.Parameters.Count > 4)
    {
        o.Parameters[4].Description = "Sort: rating_desc for ratings-based sort (default none)";
    }
    return o;
});

// Single video by id (public)
app.MapGet("/api/videos/{id}", (
    string id,
    [Microsoft.AspNetCore.Mvc.FromServices] VideoRepository repo,
    [Microsoft.AspNetCore.Mvc.FromServices] RatingsRepository ratings
) =>
{
    var v = repo.All().FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase) && x.Hidden != true);
    if (v == null) return Results.NotFound();
    var s = ratings.GetSummary(v.Id, 1, null);
    var dto = new VideoDto(
        v.Id,
        v.Title,
        v.Creator,
        v.Description,
        v.Tags,
        v.ReleaseDate,
        v.Id,
        v.SubtitleUrl,
        s.Red,
        s.Yellow,
        s.Green
    );
    return Results.Json(dto);
}).WithOpenApi(o => { o.Summary = "Get a single video by id"; return o; });

app.MapGet("/api/videos/{id}/ratings", (string id, int? version, HttpContext ctx, RatingsRepository repo) =>
{
    int v = Math.Max(1, version ?? 1);
    // Prefer forwarded identity header from webapp; fallback to authenticated identity
    var fwdUserId = ctx.Request.Headers["X-User-Id"].FirstOrDefault();
    string? user = !string.IsNullOrWhiteSpace(fwdUserId)
        ? fwdUserId
        : (ctx.User?.Identity?.IsAuthenticated == true ? ctx.User.Identity!.Name : null);
    var summary = repo.GetSummary(id, v, user);
    return Results.Json(summary);
})
.WithName("GetRatings")
.WithOpenApi(o => { o.Summary = "Get rating summary (and user rating if authenticated)"; return o; });

app.MapPost("/api/videos/{id}/ratings", (string id, RatingRequest body, HttpContext ctx, RatingsRepository repo) =>
{
    // Prefer forwarded identity headers from webapp; fallback to authenticated identity; then "anon"
    var fwdUserId = ctx.Request.Headers["X-User-Id"].FirstOrDefault();
    var fwdUserName = ctx.Request.Headers["X-User-Name"].FirstOrDefault();
    var user = !string.IsNullOrWhiteSpace(fwdUserId)
        ? fwdUserId
        : (ctx.User?.Identity?.IsAuthenticated == true ? (ctx.User.Identity!.Name ?? "anon") : "anon");
    var version = Math.Max(1, body.Version);
    repo.Submit(user!, id, version, body.Value, fwdUserName);
    return Results.Ok(new { ok = true });
})
.WithName("PostRating")
.WithOpenApi(o => { o.Summary = "Submit rating (red|yellow|green) for a version"; return o; });

app.MapDelete("/api/videos/{id}/ratings", (string id, int? version, HttpContext ctx, RatingsRepository repo) =>
{
    int v = Math.Max(1, version ?? 1);
    var fwdUserId = ctx.Request.Headers["X-User-Id"].FirstOrDefault();
    var user = !string.IsNullOrWhiteSpace(fwdUserId)
        ? fwdUserId
        : (ctx.User?.Identity?.IsAuthenticated == true ? (ctx.User.Identity!.Name ?? "") : "");
    if (string.IsNullOrWhiteSpace(user)) return Results.StatusCode(401);
    repo.Remove(user!, id, v);
    return Results.Ok(new { ok = true });
})
.WithName("DeleteRating")
.WithOpenApi(o => { o.Summary = "Remove current user rating for a version"; return o; });

// Admin endpoints (no auth yet; rely on private network; to be secured later)
app.MapGet("/api/admin/ratings/recent", (int? limit, RatingsRepository repo) =>
{
    var items = repo.GetRecent(Math.Max(1, limit ?? 50));
    return Results.Json(items);
})
.WithOpenApi(o => { o.Summary = "List recent rating events (descending)"; return o; });

string SubtitlesRoot()
{
    var root = app.Configuration["Data:SubtitlesRoot"] ?? app.Configuration["DATA_SUBTITLES_ROOT"] ?? "/app/data/subtitles";
    return Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, root));
}

static string SanitizeId(string id)
{
    var safe = new string(id.Where(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_').ToArray());
    return string.IsNullOrWhiteSpace(safe) ? "unknown" : safe;
}

int MaxUploadBytes()
{
    var v = app.Configuration["UPLOADS_MAX_SUBTITLE_BYTES"];
    return int.TryParse(v, out var n) && n > 0 ? n : 1024 * 1024;
}

app.MapGet("/api/subtitles/{videoId}/{version}.srt", (string videoId, int version, HttpResponse res) =>
{
    var root = SubtitlesRoot();
    var vid = SanitizeId(videoId);
    var path = Path.Combine(root, vid, $"v{version}.srt");
    if (!System.IO.File.Exists(path)) return Results.NotFound();
    res.Headers["Cache-Control"] = "public, max-age=3600";
    res.Headers["Content-Disposition"] = $"inline; filename=\"{vid}-v{version}.srt\"";
    return Results.File(path, "text/plain; charset=utf-8");
}).WithOpenApi(o => { o.Summary = "Serve first-party subtitle SRT"; return o; });

app.MapPost("/api/uploads/subtitles", async (HttpRequest req, HttpContext ctx) =>
{
    if (!IsIngestAuthorized(ctx)) return Results.StatusCode(403);
    var videoId = req.Query["videoId"].FirstOrDefault();
    var versionStr = req.Query["version"].FirstOrDefault();
    if (req.HasFormContentType)
    {
        var form = await req.ReadFormAsync();
        videoId ??= form["videoId"].FirstOrDefault();
        versionStr ??= form["version"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(videoId) || string.IsNullOrWhiteSpace(versionStr)) return Results.BadRequest("Missing videoId or version");
        if (!int.TryParse(versionStr, out var ver) || ver < 1) return Results.BadRequest("Invalid version");
        var content = form["content"].FirstOrDefault();
        byte[] data;
        if (form.Files.Count > 0)
        {
            using var ms = new MemoryStream();
            await form.Files[0].CopyToAsync(ms);
            data = ms.ToArray();
        }
        else if (!string.IsNullOrEmpty(content))
        {
            data = System.Text.Encoding.UTF8.GetBytes(content);
        }
        else return Results.BadRequest("Missing content");
        if (data.Length > MaxUploadBytes()) return Results.BadRequest("File too large");
        var root = SubtitlesRoot();
        var vid = SanitizeId(videoId!);
        var dir = Path.Combine(root, vid);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"v{ver}.srt");
        await System.IO.File.WriteAllBytesAsync(path, data);
        var storageKey = $"subtitles/{vid}/v{ver}.srt";
        return Results.Json(new { storage_key = storageKey });
    }
    else
    {
        if (string.IsNullOrWhiteSpace(videoId) || string.IsNullOrWhiteSpace(versionStr)) return Results.BadRequest("Missing videoId or version");
        if (!int.TryParse(versionStr, out var ver) || ver < 1) return Results.BadRequest("Invalid version");
        using var ms = new MemoryStream();
        await req.Body.CopyToAsync(ms);
        var data = ms.ToArray();
        if (data.Length == 0) return Results.BadRequest("Empty body");
        if (data.Length > MaxUploadBytes()) return Results.BadRequest("File too large");
        var root = SubtitlesRoot();
        var vid = SanitizeId(videoId!);
        var dir = Path.Combine(root, vid);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"v{ver}.srt");
        await System.IO.File.WriteAllBytesAsync(path, data);
        var storageKey = $"subtitles/{vid}/v{ver}.srt";
        return Results.Json(new { storage_key = storageKey });
    }
}).WithOpenApi(o => { o.Summary = "Upload SRT (multipart or raw text)"; return o; });

bool IsIngestAuthorized(HttpContext ctx)
{
    var header = ctx.Request.Headers["X-Api-Key"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(header)) return false;
    var allow = app.Configuration["API_INGEST_TOKENS"];
    if (string.IsNullOrWhiteSpace(allow)) return false;
    return allow.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(header);
}

string? ParseSubmitterFromToken(string? token)
{
    if (string.IsNullOrWhiteSpace(token)) return null;
    // Expected format: bonjwatv_token_<USERNAME>_<MD5(USERNAME)>
    try
    {
        var parts = token.Split('_');
        if (parts.Length >= 4 && parts[0] == "bonjwatv" && parts[1] == "token")
        {
            var username = parts[2];
            var md5 = parts[3];
            using var md5prov = System.Security.Cryptography.MD5.Create();
            var hash = BitConverter.ToString(md5prov.ComputeHash(System.Text.Encoding.UTF8.GetBytes(username))).Replace("-", "").ToLowerInvariant();
            if (string.Equals(hash, md5, StringComparison.OrdinalIgnoreCase))
            {
                return username;
            }
            // If hash doesn't match, still fall back to raw username to avoid blocking
            return username;
        }
    }
    catch { }
    return null;
}

app.MapPost("/api/submissions/videos", (HttpContext ctx, SubmissionsRepository repo, VideoSubmissionPayload body) =>
{
    if (!IsIngestAuthorized(ctx)) return Results.StatusCode(403);
    if (string.IsNullOrWhiteSpace(body.YoutubeId) || string.IsNullOrWhiteSpace(body.Title)) return Results.BadRequest("Missing required fields");
    var token = ctx.Request.Headers["X-Api-Key"].FirstOrDefault();
    var who = ParseSubmitterFromToken(token) ?? (token ?? "unknown");
    var s = repo.CreateVideo(who, body);
    return Results.Json(new { submission_id = s.Id, status = s.Status });
}).WithOpenApi(o => { o.Summary = "Submit a new video for review"; return o; });

app.MapGet("/api/admin/submissions", (SubmissionsRepository repo, string? status, string? type, int? page, int? pageSize) =>
{
    var pg = Math.Max(1, page ?? 1);
    var ps = Math.Clamp(pageSize ?? 24, 1, 100);
    var items = repo.List(type, status, pg, ps);
    return Results.Json(new { items, page = pg, pageSize = ps });
}).WithOpenApi(o => { o.Summary = "List submissions (admin)"; return o; });

app.MapGet("/api/admin/submissions/{id}", (string id, SubmissionsRepository repo) =>
{
    var s = repo.Get(id);
    return s == null ? Results.NotFound() : Results.Json(s);
}).WithOpenApi(o => { o.Summary = "Get submission detail (admin)"; return o; });

app.MapPatch("/api/admin/submissions/{id}", async (string id, HttpRequest req, SubmissionsRepository repo) =>
{
    using var sr = new StreamReader(req.Body);
    var json = await sr.ReadToEndAsync();
    if (string.IsNullOrWhiteSpace(json)) return Results.BadRequest("Missing body");
    using var doc = System.Text.Json.JsonDocument.Parse(json);
    var bodyRoot = doc.RootElement;
    var action = bodyRoot.TryGetProperty("action", out var a) ? a.GetString() : null;
    var reason = bodyRoot.TryGetProperty("reason", out var r) ? r.GetString() : null;
    if (string.IsNullOrWhiteSpace(action)) return Results.BadRequest("Missing action");
    var ok = repo.Review(id, "admin", action!, reason);
    if (!ok) return Results.NotFound();

    // On approval, upsert into videos store and ensure first‑party subtitle is available
    if (string.Equals(action, "approve", StringComparison.OrdinalIgnoreCase))
    {
        var s = repo.Get(id);
        if (s?.Payload != null)
        {
            var vid = s.Payload.YoutubeId;
            if (!string.IsNullOrWhiteSpace(vid))
            {
                // Ensure we have a local subtitle file; mirror external if necessary
                var version = 1;
                var subsRoot = SubtitlesRoot();
                var storageKey = await EnsureSubtitleStoredAsync(subsRoot, vid, version, s.Payload.SubtitleStorageKey, s.Payload.SubtitleUrl);
                // Compute first‑party serving URL
                var internalUrl = $"/api/subtitles/{SanitizeId(vid)}/{version}.srt";
                // Upsert in primary videos store (decoupled from legacy videos.json)
                UpsertVideoIntoCatalogJson(
                    VideosStorePath(),
                    vid,
                    s.Payload.Title,
                    s.Payload.Creator,
                    s.Payload.Description,
                    s.Payload.Tags,
                    s.Payload.ReleaseDate,
                    internalUrl,
                    s.SubmittedBy,
                    s.SubmittedAt
                );
            }
        }
    }
    else if (string.Equals(action, "reject", StringComparison.OrdinalIgnoreCase))
    {
        // On rejection, delete any uploaded first‑party subtitle file referenced by the submission
        var s = repo.Get(id);
        var storageKey = s?.Payload?.SubtitleStorageKey;
        if (!string.IsNullOrWhiteSpace(storageKey))
        {
            // Only delete if not referenced by catalog videos store
            if (TryParseStorageKey(storageKey!, out var vid, out var ver))
            {
                var inUse = IsSubtitleReferencedInCatalog(VideosStorePath(), vid!, ver);
                if (!inUse)
                {
                    var full = MapStorageKeyToPath(SubtitlesRoot(), storageKey!);
                    if (full != null)
                    {
                        try { if (System.IO.File.Exists(full)) System.IO.File.Delete(full); }
                        catch { /* swallow IO errors */ }
                    }
                }
            }
        }
    }
    return Results.Ok(new { ok = true });
}).WithOpenApi(o => { o.Summary = "Approve or reject a submission (admin)"; return o; });

// Admin videos management: list hidden, detail, hide/show/delete
app.MapGet("/api/admin/videos/hidden", ([Microsoft.AspNetCore.Mvc.FromServices] VideoRepository repo) =>
{
    var items = repo.All().Where(v => v.Hidden == true)
        .Select(v => new { id = v.Id, title = v.Title, hiddenReason = v.HiddenReason, hiddenAt = v.HiddenAt })
        .ToList();
    return Results.Json(items);
}).WithOpenApi(o => { o.Summary = "List hidden videos"; return o; });

app.MapGet("/api/admin/videos/{id}", (string id, [Microsoft.AspNetCore.Mvc.FromServices] VideoRepository repo) =>
{
    var v = repo.All().FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
    return v == null ? Results.NotFound() : Results.Json(v);
}).WithOpenApi(o => { o.Summary = "Get video detail (admin)"; return o; });

// Legacy helper removed; use VideosStorePath() instead

app.MapPatch("/api/admin/videos/{id}/hide", async (string id, HttpRequest req) =>
{
    using var sr = new StreamReader(req.Body);
    var body = await sr.ReadToEndAsync();
    string? reason = null;
    if (!string.IsNullOrWhiteSpace(body))
    {
        try { using var doc = System.Text.Json.JsonDocument.Parse(body); reason = doc.RootElement.TryGetProperty("reason", out var r) ? r.GetString() : null; }
        catch { }
    }
    HideOrShowVideo(VideosStorePath(), id, hide: true, reason: reason);
    return Results.Ok(new { ok = true });
}).WithOpenApi(o => { o.Summary = "Hide a video with reason"; return o; });

app.MapPatch("/api/admin/videos/{id}/show", (string id) =>
{
    HideOrShowVideo(VideosStorePath(), id, hide: false, reason: null);
    return Results.Ok(new { ok = true });
}).WithOpenApi(o => { o.Summary = "Unhide a video"; return o; });

app.MapDelete("/api/admin/videos/{id}", (string id) =>
{
    DeleteVideo(VideosStorePath(), id);
    return Results.Ok(new { ok = true });
}).WithOpenApi(o => { o.Summary = "Delete a video from catalog"; return o; });

// Admin: manage tags (add/remove/set)
app.MapPatch("/api/admin/videos/{id}/tags", async (string id, HttpRequest req) =>
{
    using var sr = new StreamReader(req.Body);
    var body = await sr.ReadToEndAsync();
    if (string.IsNullOrWhiteSpace(body)) return Results.BadRequest("Missing body");
    try
    {
        using var doc = System.Text.Json.JsonDocument.Parse(body);
        var root = doc.RootElement;
        var action = root.TryGetProperty("action", out var a) ? (a.GetString() ?? string.Empty).ToLowerInvariant() : string.Empty;
        if (string.IsNullOrWhiteSpace(action)) return Results.BadRequest("Missing action");
        List<string> tags = new();
        if (string.Equals(action, "set", StringComparison.OrdinalIgnoreCase))
        {
            if (root.TryGetProperty("tags", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in arr.EnumerateArray())
                {
                    var s = el.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) tags.Add(s!);
                }
            }
            SetTags(VideosStorePath(), id, tags);
            return Results.Ok(new { ok = true, count = tags.Count });
        }
        else if (string.Equals(action, "add", StringComparison.OrdinalIgnoreCase))
        {
            var tag = root.TryGetProperty("tag", out var t) ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(tag)) return Results.BadRequest("Missing tag");
            AddTag(VideosStorePath(), id, tag!);
            return Results.Ok(new { ok = true });
        }
        else if (string.Equals(action, "remove", StringComparison.OrdinalIgnoreCase))
        {
            var tag = root.TryGetProperty("tag", out var t) ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(tag)) return Results.BadRequest("Missing tag");
            RemoveTag(VideosStorePath(), id, tag!);
            return Results.Ok(new { ok = true });
        }
        return Results.BadRequest("Unknown action");
    }
    catch
    {
        return Results.BadRequest("Invalid JSON");
    }
}).WithOpenApi(o => { o.Summary = "Add/remove/set tags for a video (admin)"; return o; });

app.Run();

// ----- Helpers (must remain before type declarations) -----

string VideosStorePath()
{
    // Primary store path (new): prefers DATA_VIDEOS_STORE_PATH / Data:VideosStorePath; fallback to data/catalog-videos.json
    return Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath,
        app.Configuration["DATA_VIDEOS_STORE_PATH"] ?? app.Configuration["Data:VideosStorePath"] ?? "data/catalog-videos.json"));
}

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

static void UpsertVideoIntoCatalogJson(string jsonPath, string youtubeId, string title, string? creator, string? description, string[]? tags, string? releaseDate, string subtitleUrl, string? submitter, DateTimeOffset submittedAt)
{
    try
    {
        var full = Path.GetFullPath(jsonPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        System.Text.Json.JsonElement[] empty = Array.Empty<System.Text.Json.JsonElement>();
        var list = new List<Dictionary<string, object?>>();
        if (File.Exists(full))
        {
            using var fs = File.OpenRead(full);
            using var doc = System.Text.Json.JsonDocument.Parse(fs);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var obj = new Dictionary<string, object?>();
                    foreach (var prop in el.EnumerateObject())
                    {
                        obj[prop.Name] = prop.Value.ValueKind switch
                        {
                            System.Text.Json.JsonValueKind.String => prop.Value.GetString(),
                            System.Text.Json.JsonValueKind.Number => (object?)prop.Value.GetRawText(),
                            System.Text.Json.JsonValueKind.True => true,
                            System.Text.Json.JsonValueKind.False => false,
                            System.Text.Json.JsonValueKind.Array => prop.Value.EnumerateArray().Select(x => x.GetString()).ToArray(),
                            _ => null
                        };
                    }
                    list.Add(obj);
                }
            }
        }
        var existing = list.FirstOrDefault(x => (x.TryGetValue("v", out var vv) ? Convert.ToString(vv) : null) == youtubeId);
        if (existing == null)
        {
            existing = new Dictionary<string, object?>();
            list.Add(existing);
        }
        existing["v"] = youtubeId;
        if (!string.IsNullOrWhiteSpace(title)) existing["title"] = title;
        if (!string.IsNullOrWhiteSpace(creator)) existing["creator"] = creator;
        if (!string.IsNullOrWhiteSpace(description)) existing["description"] = description;
        if (tags != null && tags.Length > 0) existing["tags"] = tags;
        if (!string.IsNullOrWhiteSpace(releaseDate)) existing["releaseDate"] = releaseDate;
        existing["subtitleUrl"] = subtitleUrl;
        if (!string.IsNullOrWhiteSpace(submitter)) existing["submitter"] = submitter;
        existing["submissionDate"] = submittedAt.ToString("yyyy-MM-dd");
        var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
        var normalized = list.Select(x =>
        {
            var o = new Dictionary<string, object?>();
            foreach (var k in new[] { "v", "title", "creator", "description", "tags", "releaseDate", "subtitleUrl", "submitter", "submissionDate" })
            {
                if (x.ContainsKey(k)) o[k] = x[k];
            }
            return o;
        }).ToList();
        var json = System.Text.Json.JsonSerializer.Serialize(normalized, opts);
        File.WriteAllText(full, json);
    }
    catch
    {
        // swallow in this minimal implementation; could log
    }
}
static async Task<string?> EnsureSubtitleStoredAsync(string subtitlesRoot, string videoId, int version, string? storageKey, string? externalUrl)
{
    var root = subtitlesRoot;
    Directory.CreateDirectory(root);
    var vid = SanitizeId(videoId);
    var dir = Path.Combine(root, vid);
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, $"v{version}.srt");

    if (!string.IsNullOrWhiteSpace(storageKey))
    {
        // If storageKey already points under our root, assume present
        // Otherwise, we still write using standard path
        if (File.Exists(path)) return storageKey;
    }

    if (!string.IsNullOrWhiteSpace(externalUrl))
    {
        try
        {
            byte[] data;
            if (externalUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                var fp = externalUrl.Substring("file://".Length);
                data = await File.ReadAllBytesAsync(fp);
            }
            else
            {
                using var http = new HttpClient();
                data = await http.GetByteArrayAsync(externalUrl);
            }
            await File.WriteAllBytesAsync(path, data);
            return $"subtitles/{vid}/v{version}.srt";
        }
        catch { }
    }

    // If nothing else, ensure file exists (create empty) to avoid broken link
    if (!File.Exists(path))
    {
        await File.WriteAllTextAsync(path, "");
    }
    return $"subtitles/{vid}/v{version}.srt";
}

static string? MapStorageKeyToPath(string subtitlesRoot, string storageKey)
{
    try
    {
        if (string.IsNullOrWhiteSpace(storageKey)) return null;
        var key = storageKey.Replace('\\', '/');
        if (!key.StartsWith("subtitles/", StringComparison.OrdinalIgnoreCase)) return null;
        var rel = key.Substring("subtitles/".Length);
        var parts = rel.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return null;
        var vid = SanitizeId(parts[0]);
        var verPart = parts[1];
        // Accept both "v1" and "v1.srt"
        var verNoExt = verPart.EndsWith(".srt", StringComparison.OrdinalIgnoreCase)
            ? verPart.Substring(0, verPart.Length - 4)
            : verPart;
        if (!verNoExt.StartsWith("v") || verNoExt.Length < 2 || !int.TryParse(verNoExt.Substring(1), out var _)) return null;
        var path = Path.Combine(subtitlesRoot, vid, verNoExt + ".srt");
        return Path.GetFullPath(path);
    }
    catch { return null; }
}

static bool TryParseStorageKey(string storageKey, out string videoId, out int version)
{
    videoId = string.Empty; version = 0;
    try
    {
        if (string.IsNullOrWhiteSpace(storageKey)) return false;
        var key = storageKey.Replace('\\', '/');
        if (!key.StartsWith("subtitles/", StringComparison.OrdinalIgnoreCase)) return false;
        var rel = key.Substring("subtitles/".Length);
        var parts = rel.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return false;
        var vid = SanitizeId(parts[0]);
        var verPart = parts[1];
        var verNoExt = verPart.EndsWith(".srt", StringComparison.OrdinalIgnoreCase)
            ? verPart.Substring(0, verPart.Length - 4)
            : verPart;
        if (!verNoExt.StartsWith("v") || verNoExt.Length < 2 || !int.TryParse(verNoExt.Substring(1), out var vnum)) return false;
        videoId = vid; version = vnum; return true;
    }
    catch { return false; }
}

static bool IsSubtitleReferencedInCatalog(string catalogJsonPath, string videoId, int version)
{
    try
    {
        var full = Path.GetFullPath(catalogJsonPath);
        if (!System.IO.File.Exists(full)) return false;
        using var fs = System.IO.File.OpenRead(full);
        using var doc = System.Text.Json.JsonDocument.Parse(fs);
        if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) return false;
        var expected = $"/api/subtitles/{SanitizeId(videoId)}/{version}.srt";
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            if (el.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
            if (el.TryGetProperty("subtitleUrl", out var su) && su.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var val = su.GetString();
                if (!string.IsNullOrWhiteSpace(val) && string.Equals(val, expected, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        return false;
    }
    catch { return false; }
}

// (type declarations moved to end — must come after all statements)

// --- Admin video file mutators ---
static void HideOrShowVideo(string jsonPath, string id, bool hide, string? reason)
{
    try
    {
        var full = Path.GetFullPath(jsonPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var list = new List<Dictionary<string, object?>>();
        if (File.Exists(full))
        {
            var json = File.ReadAllText(full);
            try { list = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(json) ?? new(); }
            catch { list = new(); }
        }
        var item = list.FirstOrDefault(x => (x.TryGetValue("v", out var vv) ? Convert.ToString(vv) : null) == id);
        if (item == null) return;
        if (hide)
        {
            item["hidden"] = true;
            if (!string.IsNullOrWhiteSpace(reason)) item["hiddenReason"] = reason;
            item["hiddenAt"] = DateTimeOffset.UtcNow.ToString("u");
        }
        else
        {
            item.Remove("hidden");
            item.Remove("hiddenReason");
            item.Remove("hiddenAt");
        }
        var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(full, System.Text.Json.JsonSerializer.Serialize(list, opts));
    }
    catch { }
}

static void DeleteVideo(string jsonPath, string id)
{
    try
    {
        var full = Path.GetFullPath(jsonPath);
        if (!File.Exists(full)) return;
        var json = File.ReadAllText(full);
        var list = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(json) ?? new();
        var next = list.Where(x => (x.TryGetValue("v", out var vv) ? Convert.ToString(vv) : null) != id).ToList();
        var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(full, System.Text.Json.JsonSerializer.Serialize(next, opts));
    }
    catch { }
}

static void SetTags(string jsonPath, string id, IEnumerable<string> tags)
{
    try
    {
        var full = Path.GetFullPath(jsonPath);
        if (!File.Exists(full)) return;
        var json = File.ReadAllText(full);
        var list = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(json) ?? new();
        var item = list.FirstOrDefault(x => (x.TryGetValue("v", out var vv) ? Convert.ToString(vv) : null) == id);
        if (item == null) return;
        var unique = tags.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        item["tags"] = unique;
        var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(full, System.Text.Json.JsonSerializer.Serialize(list, opts));
    }
    catch { }
}

static void AddTag(string jsonPath, string id, string tag)
{
    try
    {
        var full = Path.GetFullPath(jsonPath);
        if (!File.Exists(full)) return;
        var json = File.ReadAllText(full);
        var list = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(json) ?? new();
        var item = list.FirstOrDefault(x => (x.TryGetValue("v", out var vv) ? Convert.ToString(vv) : null) == id);
        if (item == null) return;
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (item.TryGetValue("tags", out var tv) && tv is System.Text.Json.JsonElement jel && jel.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in jel.EnumerateArray())
            {
                var s = el.GetString(); if (!string.IsNullOrWhiteSpace(s)) existing.Add(s!);
            }
        }
        else if (item.TryGetValue("tags", out var tv2) && tv2 is IEnumerable<object?> arr)
        {
            foreach (var o in arr) { var s = Convert.ToString(o); if (!string.IsNullOrWhiteSpace(s)) existing.Add(s!); }
        }
        existing.Add(tag);
        item["tags"] = existing.ToArray();
        var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(full, System.Text.Json.JsonSerializer.Serialize(list, opts));
    }
    catch { }
}

static void RemoveTag(string jsonPath, string id, string tag)
{
    try
    {
        var full = Path.GetFullPath(jsonPath);
        if (!File.Exists(full)) return;
        var json = File.ReadAllText(full);
        var list = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(json) ?? new();
        var item = list.FirstOrDefault(x => (x.TryGetValue("v", out var vv) ? Convert.ToString(vv) : null) == id);
        if (item == null) return;
        var existing = new List<string>();
        if (item.TryGetValue("tags", out var tv) && tv is System.Text.Json.JsonElement jel && jel.ValueKind == JsonValueKind.Array)
        {
            existing = jel.EnumerateArray().Select(e => e.GetString() ?? string.Empty).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        }
        else if (item.TryGetValue("tags", out var tv2) && tv2 is IEnumerable<object?> arr)
        {
            existing = arr.Select(o => Convert.ToString(o) ?? string.Empty).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        }
        existing = existing.Where(s => !string.Equals(s, tag, StringComparison.OrdinalIgnoreCase)).ToList();
        item["tags"] = existing.ToArray();
        var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(full, System.Text.Json.JsonSerializer.Serialize(list, opts));
    }
    catch { }
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
    string YoutubeId,
    string? SubtitleUrl,
    int Red,
    int Yellow,
    int Green
);

internal record RatingRequest([property: JsonConverter(typeof(JsonStringEnumConverter))] RatingValue Value, int Version);

public partial class Program { }
