using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using catalog_api.Services;
using Microsoft.AspNetCore.HttpOverrides;
using System.Security.Cryptography;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddScoped<VideoRepository>();
builder.Services.AddScoped<RatingsRepository>();
builder.Services.AddScoped<SubmissionsRepository>();
builder.Services.AddScoped<CreatorMappingsRepository>();
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
    [Microsoft.AspNetCore.Mvc.FromServices] CreatorMappingsRepository mappings,
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
        {
            var inTitle = v.Title?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false;
            var inTags = v.Tags != null && v.Tags.Any(tag => tag.Equals(t, StringComparison.OrdinalIgnoreCase));
            var inCreator = v.Creator?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false;
            // Resolve token via mappings to support searching by original names (e.g., Korean)
            var resolved = mappings.Resolve(t);
            var inCreatorResolved = !string.IsNullOrWhiteSpace(resolved) && (v.Creator?.Contains(resolved, StringComparison.OrdinalIgnoreCase) ?? false);
            return inTitle || inTags || inCreator || inCreatorResolved;
        }));
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
            var contributors = MapContributors(v.SubtitleContributors);
            return new VideoDto(
                v.Id,
                v.Title,
                v.Creator,
                v.Description,
                v.Tags,
                v.ReleaseDate,
                v.Id,
                v.SubtitleUrl,
                contributors,
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
    var contributors = MapContributors(v.SubtitleContributors);
    var dto = new VideoDto(
        v.Id,
        v.Title,
        v.Creator,
        v.Description,
        v.Tags,
        v.ReleaseDate,
        v.Id,
        v.SubtitleUrl,
        contributors,
        s.Red,
        s.Yellow,
        s.Green
    );
    return Results.Json(dto);
}).WithOpenApi(o => { o.Summary = "Get a single video by id"; return o; });

// Batch subtitle hashes for given video IDs (hash of currently referenced first-party subtitle)
app.MapGet("/api/subtitles/hashes", async (HttpRequest req, VideoRepository repo) =>
{
    // Accept ids via repeated query params (?ids=a&ids=b) or comma-separated (?ids=a,b)
    var raw = req.Query["ids"]; // may be empty, repeated, or single comma-separated
    var ids = new List<string>();
    foreach (var val in raw)
    {
        if (string.IsNullOrWhiteSpace(val)) continue;
        ids.AddRange(val.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
    if (ids.Count == 0) return Results.BadRequest(new { error = "Provide ids via ?ids=abc&ids=def or ?ids=abc,def" });

    var result = new Dictionary<string, object?>();
    var videos = repo.All();
    foreach (var id in ids)
    {
        var vid = videos.FirstOrDefault(v => string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase));
        if (vid == null || string.IsNullOrWhiteSpace(vid.SubtitleUrl)) { result[id] = null; continue; }
        // Expect /api/subtitles/{id}/{version}.srt
        var su = vid.SubtitleUrl!.Trim();
        int ver = 0;
        try
        {
            var parts = su.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var last = parts.LastOrDefault() ?? string.Empty;
            if (last.EndsWith(".srt", StringComparison.OrdinalIgnoreCase)) last = last[..^4];
            if (int.TryParse(last, out var vnum)) ver = vnum;
        }
        catch { ver = 0; }
        if (ver <= 0) { result[id] = null; continue; }
        try
        {
            var path = Path.Combine(SubtitlesRoot(), SanitizeId(id), $"v{ver}.srt");
            if (!File.Exists(path)) { result[id] = null; continue; }
            var bytes = await File.ReadAllBytesAsync(path);
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
            result[id] = hash;
        }
        catch { result[id] = null; }
    }
    return Results.Json(result);
}).WithOpenApi(o => { o.Summary = "Get SHA256 hashes of current subtitles for provided video IDs"; return o; });


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

app.MapPost("/api/admin/migrations/legacy-videos/import", async (HttpRequest req, VideoRepository repo, ILoggerFactory loggerFactory) =>
{
    LegacyVideosMigrationRequest body = new(null);
    if ((req.ContentLength ?? 0) > 0)
    {
        try
        {
            body = await req.ReadFromJsonAsync<LegacyVideosMigrationRequest>() ?? new(null);
        }
        catch
        {
            return Results.BadRequest(new { error = "invalid_json" });
        }
    }

    var dryRun = body.DryRun ?? true;
    var logger = loggerFactory.CreateLogger("LegacyVideosMigration");
    var result = RunLegacyVideosMigration(VideosStorePath(), LegacyVideosPath(), dryRun, logger);

    if (!dryRun && result.Ok)
    {
        repo.ForceReload();
    }

    var status = result.Ok ? 200 : result.ErrorCode switch
    {
        "legacy_file_not_found" => 404,
        "invalid_legacy_payload" => 400,
        _ => 500
    };
    return Results.Json(result, statusCode: status);
}).WithOpenApi(o => { o.Summary = "Import legacy videos.json into catalog store (idempotent; dry-run supported)"; return o; });

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

string SubtitlesStagingRoot()
{
    // Prefer explicit staging root; default under app data folder (relative) to avoid permission issues in tests
    var root = app.Configuration["Data:SubtitlesStagingRoot"] ?? app.Configuration["DATA_SUBTITLES_STAGING_ROOT"] ?? "data/subtitles-staging";
    return Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, root));
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
        // Write to staging, not public, until admin approval promotes it
        var root = SubtitlesStagingRoot();
        var vid = SanitizeId(videoId!);
        var dir = Path.Combine(root, vid);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"v{ver}.srt");
        await System.IO.File.WriteAllBytesAsync(path, data);
        var storageKey = $"staging/{vid}/v{ver}.srt";
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
        var root = SubtitlesStagingRoot();
        var vid = SanitizeId(videoId!);
        var dir = Path.Combine(root, vid);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"v{ver}.srt");
        await System.IO.File.WriteAllBytesAsync(path, data);
        var storageKey = $"staging/{vid}/v{ver}.srt";
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

bool IsCorrectionsAuthorized(HttpContext ctx)
{
    var header = ctx.Request.Headers["X-Api-Key"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(header)) return false;
    var allow = app.Configuration["API_CORRECTION_TOKENS"];
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

app.MapPost("/api/submissions/videos", (HttpContext ctx, SubmissionsRepository repo, CreatorMappingsRepository mappings, VideoSubmissionPayload body) =>
{
    if (!IsIngestAuthorized(ctx)) return Results.StatusCode(403);
    if (string.IsNullOrWhiteSpace(body.YoutubeId) || string.IsNullOrWhiteSpace(body.Title)) return Results.BadRequest("Missing required fields");
    var token = ctx.Request.Headers["X-Api-Key"].FirstOrDefault();
    var who = ParseSubmitterFromToken(token) ?? (token ?? "unknown");
    // Apply creator canonicalization at ingest
    var original = body.Creator;
    body.CreatorOriginal = original;
    var resolved = mappings.Resolve(original);
    body.CreatorCanonical = !string.IsNullOrWhiteSpace(resolved) ? resolved : original;
    var s = repo.CreateVideo(who, body);
    return Results.Json(new { submission_id = s.Id, status = s.Status });
}).WithOpenApi(o => { o.Summary = "Submit a new video for review"; return o; });

app.MapPost("/api/submissions/subtitle-corrections", async (HttpContext ctx, SubmissionsRepository repo, VideoRepository videos) =>
{
    if (!IsCorrectionsAuthorized(ctx)) return Results.StatusCode(403);
    SubtitleCorrectionPayload? body;
    try
    {
        body = await System.Text.Json.JsonSerializer.DeserializeAsync<SubtitleCorrectionPayload>(ctx.Request.Body, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
    catch
    {
        return Results.BadRequest(new { error = "invalid_json" });
    }
    if (body == null) return Results.BadRequest(new { error = "missing_body" });
    var validationError = ValidateCorrectionPayload(body);
    if (validationError != null) return Results.BadRequest(new { error = validationError });
    var video = videos.All().FirstOrDefault(x => string.Equals(x.Id, body.VideoId, StringComparison.OrdinalIgnoreCase));
    if (video == null) return Results.NotFound(new { error = "video_not_found" });
    var basePath = Path.Combine(SubtitlesRoot(), SanitizeId(body.VideoId), $"v{body.SubtitleVersion}.srt");
    if (!System.IO.File.Exists(basePath)) return Results.BadRequest(new { error = "subtitle_version_missing" });
    NormalizeCorrectionPayload(body);
    var submitter = BuildCorrectionSubmitter(body);
    var s = repo.CreateSubtitleCorrection(submitter, body);
    return Results.Json(new { submission_id = s.Id, status = s.Status }, statusCode: 201);
}).WithOpenApi(o => { o.Summary = "Submit a subtitle correction patch for review"; return o; });

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

// Admin: subtitle preview for a submission (supports staged or external)
app.MapGet("/api/admin/submissions/{id}/subtitle", async (string id, SubmissionsRepository repo, HttpResponse res) =>
{
    var s = repo.Get(id);
    if (s?.Payload == null) return Results.NotFound();

    // 1) If there's a staged storage key, read from staging
    if (!string.IsNullOrWhiteSpace(s.Payload.SubtitleStorageKey))
    {
        var key = s.Payload.SubtitleStorageKey!;
        var stagedPath = MapStagingStorageKeyToPath(SubtitlesStagingRoot(), key);
        if (!string.IsNullOrWhiteSpace(stagedPath) && System.IO.File.Exists(stagedPath))
        {
            res.Headers["Cache-Control"] = "no-store";
            return Results.File(stagedPath!, "text/plain; charset=utf-8");
        }
    }

    // 2) If external URL present, fetch and return
    if (!string.IsNullOrWhiteSpace(s.Payload.SubtitleUrl))
    {
        try
        {
            string text;
            var url = s.Payload.SubtitleUrl!;
            if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                var fp = url.Substring("file://".Length);
                text = await System.IO.File.ReadAllTextAsync(fp);
            }
            else
            {
                using var http = new HttpClient();
                text = await http.GetStringAsync(url);
            }
            res.Headers["Cache-Control"] = "no-store";
            return Results.Text(text, "text/plain; charset=utf-8");
        }
        catch { }
    }

    // 3) Fallback to first-party approved path by youtube id (if present)
    var vid = s.Payload.YoutubeId;
    if (!string.IsNullOrWhiteSpace(vid))
    {
        var path = Path.Combine(SubtitlesRoot(), SanitizeId(vid!), "v1.srt");
        if (System.IO.File.Exists(path))
        {
            res.Headers["Cache-Control"] = "no-store";
            return Results.File(path, "text/plain; charset=utf-8");
        }
    }

    return Results.NotFound();
}).WithOpenApi(o => { o.Summary = "Get subtitle text for a submission (admin preview; staged or external)"; return o; });

app.MapPatch("/api/admin/submissions/{id}", async (string id, HttpRequest req, SubmissionsRepository repo, VideoRepository videos) =>
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
        if (s?.Type == "video" && s.Payload != null)
        {
            var vid = s.Payload.YoutubeId;
            if (!string.IsNullOrWhiteSpace(vid))
            {
                // Ensure we have a local subtitle file; mirror external if necessary
                var version = 1;
                var subsRoot = SubtitlesRoot();
                // If a staged storage_key exists, promote it to public; else mirror external if provided
                string? promotedKey = null;
                if (!string.IsNullOrWhiteSpace(s.Payload.SubtitleStorageKey))
                {
                    promotedKey = PromoteStagedSubtitleToPublic(s.Payload.SubtitleStorageKey!, vid, version);
                }
                var storageKey = promotedKey ?? await EnsureSubtitleStoredAsync(subsRoot, vid, version, null, s.Payload.SubtitleUrl);
                // Compute first‑party serving URL
                var internalUrl = $"/api/subtitles/{SanitizeId(vid)}/{version}.srt";
                // Upsert in primary videos store (decoupled from legacy videos.json)
                // Choose canonical creator if available
                var creatorForStore = s.Payload.CreatorCanonical ?? s.Payload.Creator ?? string.Empty;
                var contributorEntry = new SubtitleContributor
                {
                    Version = version,
                    UserId = s.SubmittedBy,
                    DisplayName = s.SubmittedBy,
                    SubmittedAt = s.SubmittedAt.ToString("u")
                };
                UpsertVideoIntoCatalogJson(
                    VideosStorePath(),
                    vid,
                    s.Payload.Title,
                    creatorForStore,
                    s.Payload.Description,
                    s.Payload.Tags,
                    s.Payload.ReleaseDate,
                    internalUrl,
                    s.SubmittedBy,
                    s.SubmittedAt,
                    s.Payload.CreatorOriginal ?? s.Payload.Creator,
                    new[] { contributorEntry }
                );
                videos.ForceReload();
            }
        }
        else if (s?.Type == "subtitle_correction" && s.SubtitleCorrection != null)
        {
            var payload = s.SubtitleCorrection;
            var video = videos.All().FirstOrDefault(x => string.Equals(x.Id, payload.VideoId, StringComparison.OrdinalIgnoreCase));
            if (video == null) return Results.BadRequest(new { error = "video_not_found" });
            var sanitized = SanitizeId(payload.VideoId);
            var subsRoot = SubtitlesRoot();
            var latest = GetCurrentSubtitleVersion(subsRoot, sanitized);
            if (latest > 0 && payload.SubtitleVersion != latest)
            {
                // TODO: support rebasing stale corrections submitted against prior versions.
                return Results.Conflict(new { error = "stale_version" });
            }
            var basePath = Path.Combine(subsRoot, sanitized, $"v{payload.SubtitleVersion}.srt");
            if (!System.IO.File.Exists(basePath)) return Results.BadRequest(new { error = "base_subtitle_missing" });
            var apply = TryApplyCueUpdates(basePath, payload.Cues);
            if (!apply.Ok || string.IsNullOrWhiteSpace(apply.UpdatedContent)) return Results.BadRequest(new { error = apply.Error ?? "apply_failed" });
            Directory.CreateDirectory(Path.Combine(subsRoot, sanitized));
            var nextVersion = DetermineNextSubtitleVersion(subsRoot, sanitized);
            var newPath = Path.Combine(subsRoot, sanitized, $"v{nextVersion}.srt");
            await System.IO.File.WriteAllTextAsync(newPath, apply.UpdatedContent);
            var contributors = video.SubtitleContributors != null
                ? video.SubtitleContributors.Select(c => new SubtitleContributor
                {
                    Version = c.Version,
                    DisplayName = c.DisplayName,
                    UserId = c.UserId,
                    SubmittedAt = c.SubmittedAt
                }).ToList()
                : new List<SubtitleContributor>();
            contributors.Add(new SubtitleContributor
            {
                Version = nextVersion,
                UserId = payload.SubmittedByUserId,
                DisplayName = string.IsNullOrWhiteSpace(payload.SubmittedByDisplayName) ? payload.SubmittedByUserId : payload.SubmittedByDisplayName,
                SubmittedAt = DateTimeOffset.UtcNow.ToString("u")
            });
            var newUrl = $"/api/subtitles/{sanitized}/{nextVersion}.srt";
            UpdateSubtitleMetadata(VideosStorePath(), payload.VideoId, newUrl, contributors);
            videos.ForceReload();
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

app.MapPatch("/api/admin/videos/{id}/hide", async (string id, HttpRequest req, VideoRepository repo) =>
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
    repo.ForceReload();
    return Results.Ok(new { ok = true });
}).WithOpenApi(o => { o.Summary = "Hide a video with reason"; return o; });

app.MapPatch("/api/admin/videos/{id}/duration", async (string id, HttpRequest req, VideoRepository repo) =>
{
    using var sr = new StreamReader(req.Body);
    var body = await sr.ReadToEndAsync();
    if (string.IsNullOrWhiteSpace(body)) return Results.BadRequest("Missing body");
    try
    {
        using var doc = System.Text.Json.JsonDocument.Parse(body);
        var root = doc.RootElement;
        var val = root.TryGetProperty("durationSeconds", out var d) ? d.GetInt32() : 0;
        if (val <= 0) return Results.BadRequest("Invalid durationSeconds");
        SetDuration(VideosStorePath(), id, val);
        repo.ForceReload();
        return Results.Ok(new { ok = true });
    }
    catch { return Results.BadRequest("Invalid JSON"); }
}).WithOpenApi(o => { o.Summary = "Set or update video runtime duration (seconds)"; return o; });

app.MapPatch("/api/admin/videos/{id}/show", (string id, VideoRepository repo) =>
{
    HideOrShowVideo(VideosStorePath(), id, hide: false, reason: null);
    repo.ForceReload();
    return Results.Ok(new { ok = true });
}).WithOpenApi(o => { o.Summary = "Unhide a video"; return o; });

app.MapDelete("/api/admin/videos/{id}", (string id, VideoRepository repo) =>
{
    DeleteVideo(VideosStorePath(), id);
    repo.ForceReload();
    return Results.Ok(new { ok = true });
}).WithOpenApi(o => { o.Summary = "Delete a video from catalog"; return o; });

// Admin: manage tags (add/remove/set)
app.MapPatch("/api/admin/videos/{id}/tags", async (string id, HttpRequest req, VideoRepository repo) =>
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
            repo.ForceReload();
            return Results.Ok(new { ok = true, count = tags.Count });
        }
        else if (string.Equals(action, "add", StringComparison.OrdinalIgnoreCase))
        {
            var tag = root.TryGetProperty("tag", out var t) ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(tag)) return Results.BadRequest("Missing tag");
            AddTag(VideosStorePath(), id, tag!);
            repo.ForceReload();
            return Results.Ok(new { ok = true });
        }
        else if (string.Equals(action, "remove", StringComparison.OrdinalIgnoreCase))
        {
            var tag = root.TryGetProperty("tag", out var t) ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(tag)) return Results.BadRequest("Missing tag");
            RemoveTag(VideosStorePath(), id, tag!);
            repo.ForceReload();
            return Results.Ok(new { ok = true });
        }
        return Results.BadRequest("Unknown action");
    }
    catch
    {
        return Results.BadRequest("Invalid JSON");
    }
}).WithOpenApi(o => { o.Summary = "Add/remove/set tags for a video (admin)"; return o; });

app.MapGet("/api/admin/videos/{id}/subtitles", (string id, [Microsoft.AspNetCore.Mvc.FromServices] VideoRepository repo) =>
{
    var video = repo.All().FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
    if (video == null) return Results.NotFound();
    var sanitized = SanitizeId(id);
    var root = SubtitlesRoot();
    var dir = Path.Combine(root, sanitized);
    var currentVersion = TryParseSubtitleVersionFromUrl(video.SubtitleUrl);
    if (!Directory.Exists(dir))
    {
        return Results.Json(new { items = Array.Empty<object>(), currentVersion = currentVersion ?? 0 });
    }
    var files = Directory.GetFiles(dir, "v*.srt");
    var list = new List<SubtitleVersionSummary>();
    foreach (var file in files)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        if (!name.StartsWith("v", StringComparison.OrdinalIgnoreCase)) continue;
        if (!int.TryParse(name.Substring(1), out var ver) || ver <= 0) continue;
        var contributor = video.SubtitleContributors?.FirstOrDefault(c => c.Version == ver);
        var (added, removed) = ComputeSubtitleDiffStats(root, sanitized, ver);
        list.Add(new SubtitleVersionSummary(
            Version: ver,
            DisplayName: contributor?.DisplayName,
            UserId: contributor?.UserId,
            SubmittedAt: contributor?.SubmittedAt,
            SizeBytes: new FileInfo(file).Length,
            AddedLines: added,
            RemovedLines: removed,
            IsCurrent: (currentVersion ?? 0) == ver
        ));
    }
    var ordered = list.OrderBy(x => x.Version).ToList();
    return Results.Json(new { items = ordered, currentVersion = currentVersion ?? 0 });
}).WithOpenApi(o => { o.Summary = "List subtitle versions and metadata for a video (admin)"; return o; });

app.MapGet("/api/admin/videos/{id}/subtitles/{version}/diff", (string id, int version) =>
{
    if (version <= 0) return Results.BadRequest("Invalid version");
    var sanitized = SanitizeId(id);
    var root = SubtitlesRoot();
    var currentPath = ResolveSubtitlePath(root, sanitized, version);
    if (currentPath == null) return Results.NotFound();
    var prevPath = ResolvePreviousSubtitlePath(root, sanitized, version);
    var currentLines = File.ReadAllLines(currentPath);
    var prevLines = prevPath != null ? File.ReadAllLines(prevPath) : Array.Empty<string>();
    var prevLabel = prevPath == null
        ? "(none)"
        : (FindVersionFromPath(prevPath) is int prevVer ? $"v{prevVer}" : Path.GetFileName(prevPath));
    var diff = BuildUnifiedDiff(prevLines, currentLines, prevLabel, $"v{version}");
    return Results.Text(diff, "text/plain");
}).WithOpenApi(o => { o.Summary = "Show a plain-text diff between a subtitle version and the previous version (admin)"; return o; });

app.MapPost("/api/admin/videos/{id}/subtitles/{version}/promote", (string id, int version, [Microsoft.AspNetCore.Mvc.FromServices] VideoRepository repo) =>
{
    if (version <= 0) return Results.BadRequest("Invalid version");
    var video = repo.All().FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
    if (video == null) return Results.NotFound();
    var sanitized = SanitizeId(id);
    var root = SubtitlesRoot();
    var path = ResolveSubtitlePath(root, sanitized, version);
    if (path == null) return Results.NotFound();
    var newUrl = $"/api/subtitles/{sanitized}/{version}.srt";
    var contributors = video.SubtitleContributors ?? new List<SubtitleContributor>();
    UpdateSubtitleMetadata(VideosStorePath(), id, newUrl, contributors);
    return Results.Ok(new { ok = true, version });
}).WithOpenApi(o => { o.Summary = "Set the active subtitle version for a video (admin)"; return o; });

app.MapDelete("/api/admin/videos/{id}/subtitles/{version}", (string id, int version, [Microsoft.AspNetCore.Mvc.FromServices] VideoRepository repo) =>
{
    if (version <= 0) return Results.BadRequest("Invalid version");
    var video = repo.All().FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
    if (video == null) return Results.NotFound();
    var currentVersion = TryParseSubtitleVersionFromUrl(video.SubtitleUrl) ?? 0;
    if (version == currentVersion) return Results.BadRequest(new { error = "cannot_delete_current_version" });
    var sanitized = SanitizeId(id);
    var root = SubtitlesRoot();
    var path = ResolveSubtitlePath(root, sanitized, version);
    if (path == null) return Results.NotFound();
    try { System.IO.File.Delete(path); } catch { }
    var contributors = video.SubtitleContributors?.Where(c => c.Version != version).ToList() ?? new List<SubtitleContributor>();
    var existingUrl = string.IsNullOrWhiteSpace(video.SubtitleUrl) ? $"/api/subtitles/{sanitized}/{currentVersion}.srt" : video.SubtitleUrl!;
    UpdateSubtitleMetadata(VideosStorePath(), id, existingUrl, contributors);
    return Results.Ok(new { ok = true });
}).WithOpenApi(o => { o.Summary = "Delete a specific subtitle version (admin)"; return o; });

// ----- Creator Mappings admin endpoints -----

app.MapGet("/api/admin/creators/mappings", (CreatorMappingsRepository repo, string? q, int? page, int? pageSize) =>
{
    var pg = Math.Max(1, page ?? 1);
    var ps = Math.Clamp(pageSize ?? 50, 1, 200);
    var (items, total) = repo.List(q, pg, ps);
    return Results.Json(new { items, totalCount = total, page = pg, pageSize = ps });
}).WithOpenApi(o => { o.Summary = "List creator mappings"; return o; });

app.MapPost("/api/admin/creators/mappings", (CreatorMappingsRepository repo, NewMappingDto body) =>
{
    var (created, err) = repo.Create(body.Source ?? string.Empty, body.Canonical ?? string.Empty, "admin");
    if (err == "conflict") return Results.Conflict(new { error = err });
    if (created == null) return Results.BadRequest(new { error = err ?? "invalid" });
    return Results.Json(created, statusCode: 201);
}).WithOpenApi(o => { o.Summary = "Create a new creator mapping"; return o; });

app.MapPut("/api/admin/creators/mappings/{id}", (string id, CreatorMappingsRepository repo, NewMappingDto body) =>
{
    var (updated, err) = repo.Update(id, body.Source ?? string.Empty, body.Canonical ?? string.Empty, "admin");
    if (err == "not_found") return Results.NotFound();
    if (err == "conflict") return Results.Conflict(new { error = err });
    if (updated == null) return Results.BadRequest(new { error = err ?? "invalid" });
    return Results.Json(updated);
}).WithOpenApi(o => { o.Summary = "Update a creator mapping"; return o; });

app.MapDelete("/api/admin/creators/mappings/{id}", (string id, CreatorMappingsRepository repo) =>
{
    var ok = repo.Delete(id);
    return ok ? Results.NoContent() : Results.NotFound();
}).WithOpenApi(o => { o.Summary = "Delete a creator mapping"; return o; });

app.MapPost("/api/admin/creators/mappings/reapply", (CreatorMappingsRepository repo) =>
{
    var videosPath = VideosStorePath();
    var submissionsPath = app.Configuration["Data:SubmissionsPath"] ?? app.Configuration["DATA_SUBMISSIONS_PATH"] ?? "data/submissions.json";
    submissionsPath = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, submissionsPath));
    var updatedVideos = ReapplyCreatorMappingsToVideos(videosPath, repo);
    var updatedSubs = ReapplyCreatorMappingsToSubmissions(submissionsPath, repo);
    return Results.Json(new { updated_videos = updatedVideos, updated_submissions = updatedSubs });
}).WithOpenApi(o => { o.Summary = "Reapply creator mappings to existing videos and pending submissions"; return o; });

app.Run();

// ----- Helpers (must remain before type declarations) -----

string VideosStorePath()
{
    // Primary store path (new): prefers DATA_VIDEOS_STORE_PATH / Data:VideosStorePath; fallback to data/catalog-videos.json
    return Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath,
        app.Configuration["DATA_VIDEOS_STORE_PATH"] ?? app.Configuration["Data:VideosStorePath"] ?? "data/catalog-videos.json"));
}

string LegacyVideosPath()
{
    return Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath,
        app.Configuration["DATA_JSON_PATH"] ?? app.Configuration["Data:JsonPath"] ?? "data/videos.json"));
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

static LegacyVideosMigrationResult RunLegacyVideosMigration(string storePath, string legacyPath, bool dryRun, ILogger logger)
{
    var startedAt = DateTimeOffset.UtcNow;
    var totals = new LegacyVideosMigrationTotals();
    var details = new List<LegacyVideosMigrationDetail>();
    const int detailSampleLimit = 100;

    try
    {
        var fullLegacy = Path.GetFullPath(legacyPath);
        var fullStore = Path.GetFullPath(storePath);
        if (!File.Exists(fullLegacy))
        {
            return new LegacyVideosMigrationResult(
                Ok: false,
                DryRun: dryRun,
                Mode: "update_missing_only_with_tag_merge",
                LegacyPath: fullLegacy,
                Totals: totals,
                DetailsSample: details,
                RanAtUtc: startedAt,
                ErrorCode: "legacy_file_not_found",
                Error: "Legacy videos.json file not found");
        }

        lock (StoreSync.Lock)
        {
            var legacy = ReadObjectArray(fullLegacy);
            if (legacy == null)
            {
                return new LegacyVideosMigrationResult(
                    Ok: false,
                    DryRun: dryRun,
                    Mode: "update_missing_only_with_tag_merge",
                    LegacyPath: fullLegacy,
                    Totals: totals,
                    DetailsSample: details,
                    RanAtUtc: startedAt,
                    ErrorCode: "invalid_legacy_payload",
                    Error: "Legacy videos.json must contain a JSON array");
            }

            totals.LegacyCount = legacy.Count;

            var store = ReadObjectArray(fullStore) ?? new List<Dictionary<string, object?>>();
            var byId = new Dictionary<string, Dictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in store)
            {
                var id = ReadVideoId(item);
                if (string.IsNullOrWhiteSpace(id)) continue;
                if (!byId.ContainsKey(id))
                {
                    byId[id] = item;
                }
            }

            foreach (var legacyItem in legacy)
            {
                try
                {
                    var legacyId = ReadVideoId(legacyItem);
                    if (!IsValidVideoId(legacyId))
                    {
                        totals.SkippedInvalid++;
                        AddDetail(details, detailSampleLimit, new LegacyVideosMigrationDetail(legacyId ?? "(missing)", "skipped_invalid", null, null, "invalid_video_id"));
                        continue;
                    }

                    if (!byId.TryGetValue(legacyId!, out var existing))
                    {
                        var created = BuildNewCatalogItemFromLegacy(legacyItem, out var invalidReason);
                        if (created == null)
                        {
                            totals.SkippedInvalid++;
                            AddDetail(details, detailSampleLimit, new LegacyVideosMigrationDetail(legacyId!, "skipped_invalid", null, null, invalidReason ?? "invalid_legacy_item"));
                            continue;
                        }
                        store.Add(created);
                        byId[legacyId!] = created;
                        totals.Created++;
                        AddDetail(details, detailSampleLimit, new LegacyVideosMigrationDetail(legacyId!, "created", null, null, null));
                        continue;
                    }

                    var fieldsFilled = new List<string>();
                    var tagsAdded = new List<string>();

                    MergeMissingString(existing, legacyItem, "title", fieldsFilled);
                    MergeMissingString(existing, legacyItem, "creator", fieldsFilled);
                    MergeMissingString(existing, legacyItem, "description", fieldsFilled);
                    MergeMissingString(existing, legacyItem, "subtitleUrl", fieldsFilled);
                    MergeMissingString(existing, legacyItem, "submitter", fieldsFilled);
                    MergeMissingString(existing, legacyItem, "submissionDate", fieldsFilled);
                    MergeMissingString(existing, legacyItem, "releaseDate", fieldsFilled);

                    if (MergeTags(existing, legacyItem, out var addedTags) && addedTags.Count > 0)
                    {
                        tagsAdded.AddRange(addedTags);
                    }

                    var missingUpdated = fieldsFilled.Count > 0;
                    var mergedTags = tagsAdded.Count > 0;
                    if (missingUpdated) totals.UpdatedMissingFields++;
                    if (mergedTags) totals.TagsMerged++;

                    if (!missingUpdated && !mergedTags)
                    {
                        totals.Unchanged++;
                        continue;
                    }

                    var action = missingUpdated && mergedTags
                        ? "updated_missing_and_tags_merged"
                        : missingUpdated ? "updated_missing" : "tags_merged";
                    AddDetail(details, detailSampleLimit, new LegacyVideosMigrationDetail(
                        legacyId!,
                        action,
                        fieldsFilled.Count > 0 ? fieldsFilled : null,
                        tagsAdded.Count > 0 ? tagsAdded : null,
                        null));
                }
                catch (Exception ex)
                {
                    totals.Errors++;
                    var id = ReadVideoId(legacyItem) ?? "(unknown)";
                    AddDetail(details, detailSampleLimit, new LegacyVideosMigrationDetail(id, "error", null, null, ex.Message));
                }
            }

            if (!dryRun)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(fullStore)!);
                var opts = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(fullStore, JsonSerializer.Serialize(store, opts));
                logger.LogInformation(
                    "Legacy migration applied from {LegacyPath} to {StorePath}: created={Created} updatedMissing={UpdatedMissing} tagsMerged={TagsMerged} unchanged={Unchanged} skippedInvalid={SkippedInvalid} errors={Errors}",
                    fullLegacy, fullStore, totals.Created, totals.UpdatedMissingFields, totals.TagsMerged, totals.Unchanged, totals.SkippedInvalid, totals.Errors);
            }
            else
            {
                logger.LogInformation(
                    "Legacy migration dry-run from {LegacyPath} to {StorePath}: created={Created} updatedMissing={UpdatedMissing} tagsMerged={TagsMerged} unchanged={Unchanged} skippedInvalid={SkippedInvalid} errors={Errors}",
                    fullLegacy, fullStore, totals.Created, totals.UpdatedMissingFields, totals.TagsMerged, totals.Unchanged, totals.SkippedInvalid, totals.Errors);
            }
        }

        return new LegacyVideosMigrationResult(
            Ok: true,
            DryRun: dryRun,
            Mode: "update_missing_only_with_tag_merge",
            LegacyPath: Path.GetFullPath(legacyPath),
            Totals: totals,
            DetailsSample: details,
            RanAtUtc: DateTimeOffset.UtcNow,
            ErrorCode: null,
            Error: null);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Legacy migration failed for {LegacyPath} -> {StorePath}", legacyPath, storePath);
        return new LegacyVideosMigrationResult(
            Ok: false,
            DryRun: dryRun,
            Mode: "update_missing_only_with_tag_merge",
            LegacyPath: Path.GetFullPath(legacyPath),
            Totals: totals,
            DetailsSample: details,
            RanAtUtc: DateTimeOffset.UtcNow,
            ErrorCode: "migration_failed",
            Error: ex.Message);
    }
}

static void AddDetail(List<LegacyVideosMigrationDetail> details, int limit, LegacyVideosMigrationDetail detail)
{
    if (details.Count < limit) details.Add(detail);
}

static List<Dictionary<string, object?>>? ReadObjectArray(string path)
{
    if (!File.Exists(path)) return new List<Dictionary<string, object?>>();
    var json = File.ReadAllText(path);
    if (string.IsNullOrWhiteSpace(json)) return new List<Dictionary<string, object?>>();
    try
    {
        return JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(json);
    }
    catch
    {
        return null;
    }
}

static bool IsValidVideoId(string? value)
{
    if (string.IsNullOrWhiteSpace(value)) return false;
    var s = value.Trim();
    if (s.Length < 6 || s.Length > 64) return false;
    return s.All(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_');
}

static string? ReadVideoId(Dictionary<string, object?> item)
{
    var v = ReadNormalizedString(item, "v");
    if (!string.IsNullOrWhiteSpace(v)) return v;
    return ReadNormalizedString(item, "id");
}

static Dictionary<string, object?>? BuildNewCatalogItemFromLegacy(Dictionary<string, object?> legacyItem, out string? invalidReason)
{
    invalidReason = null;
    var id = ReadVideoId(legacyItem);
    if (!IsValidVideoId(id))
    {
        invalidReason = "invalid_video_id";
        return null;
    }
    var title = ReadNormalizedString(legacyItem, "title");
    if (string.IsNullOrWhiteSpace(title))
    {
        invalidReason = "missing_title";
        return null;
    }

    var created = new Dictionary<string, object?>(StringComparer.Ordinal);
    foreach (var kvp in legacyItem)
    {
        if (string.Equals(kvp.Key, "id", StringComparison.OrdinalIgnoreCase)) continue;
        created[kvp.Key] = CloneJsonBackedValue(kvp.Value);
    }
    created["v"] = id;
    created["title"] = title;

    SetNormalizedStringOrRemove(created, "creator", ReadNormalizedString(legacyItem, "creator"));
    SetNormalizedStringOrRemove(created, "description", ReadNormalizedString(legacyItem, "description"));
    SetNormalizedStringOrRemove(created, "subtitleUrl", ReadNormalizedString(legacyItem, "subtitleUrl"));
    SetNormalizedStringOrRemove(created, "submitter", ReadNormalizedString(legacyItem, "submitter"));
    SetNormalizedStringOrRemove(created, "submissionDate", ReadNormalizedString(legacyItem, "submissionDate"));
    SetNormalizedStringOrRemove(created, "releaseDate", ReadNormalizedString(legacyItem, "releaseDate"));

    var tags = NormalizeTags(ReadTags(legacyItem, "tags"));
    if (tags.Count > 0) created["tags"] = tags.ToArray();
    else created.Remove("tags");

    return created;
}

static void SetNormalizedStringOrRemove(Dictionary<string, object?> target, string key, string? value)
{
    if (string.IsNullOrWhiteSpace(value)) target.Remove(key);
    else target[key] = value;
}

static bool MergeMissingString(Dictionary<string, object?> target, Dictionary<string, object?> legacy, string field, List<string> fieldsFilled)
{
    if (!IsMissingStringField(target, field)) return false;
    var val = ReadNormalizedString(legacy, field);
    if (string.IsNullOrWhiteSpace(val)) return false;
    target[field] = val;
    fieldsFilled.Add(field);
    return true;
}

static bool IsMissingStringField(Dictionary<string, object?> item, string field)
{
    var current = ReadNormalizedString(item, field);
    return string.IsNullOrWhiteSpace(current);
}

static string? ReadNormalizedString(Dictionary<string, object?> item, string field)
{
    if (!item.TryGetValue(field, out var value)) return null;
    return NormalizeOptionalString(ConvertJsonBackedToString(value));
}

static string? NormalizeOptionalString(string? value)
{
    if (string.IsNullOrWhiteSpace(value)) return null;
    return value.Trim();
}

static string? ConvertJsonBackedToString(object? value)
{
    if (value == null) return null;
    if (value is string s) return s;
    if (value is JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.Number => el.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => el.ToString()
        };
    }
    return Convert.ToString(value);
}

static bool MergeTags(Dictionary<string, object?> target, Dictionary<string, object?> legacy, out List<string> tagsAdded)
{
    tagsAdded = new List<string>();
    var currentTags = NormalizeTags(ReadTags(target, "tags"));
    var legacyTags = NormalizeTags(ReadTags(legacy, "tags"));
    if (legacyTags.Count == 0) return false;

    var set = new HashSet<string>(currentTags, StringComparer.OrdinalIgnoreCase);
    foreach (var tag in legacyTags)
    {
        if (set.Add(tag))
        {
            currentTags.Add(tag);
            tagsAdded.Add(tag);
        }
    }
    if (tagsAdded.Count == 0) return false;
    target["tags"] = currentTags.ToArray();
    return true;
}

static List<string> ReadTags(Dictionary<string, object?> item, string field)
{
    var tags = new List<string>();
    if (!item.TryGetValue(field, out var value) || value == null) return tags;

    if (value is JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in el.EnumerateArray())
            {
                if (child.ValueKind == JsonValueKind.String)
                {
                    var s = child.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) tags.Add(s!);
                }
                else
                {
                    var s = child.ToString();
                    if (!string.IsNullOrWhiteSpace(s)) tags.Add(s);
                }
            }
        }
        return tags;
    }

    if (value is IEnumerable<object> enumerable)
    {
        foreach (var part in enumerable)
        {
            var s = Convert.ToString(part);
            if (!string.IsNullOrWhiteSpace(s)) tags.Add(s!);
        }
        return tags;
    }

    var single = Convert.ToString(value);
    if (!string.IsNullOrWhiteSpace(single)) tags.Add(single!);
    return tags;
}

static List<string> NormalizeTags(IEnumerable<string> tags)
{
    var result = new List<string>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var raw in tags)
    {
        var normalized = NormalizeTag(raw);
        if (normalized == null) continue;
        if (seen.Add(normalized))
        {
            result.Add(normalized);
        }
    }
    return result;
}

static string? NormalizeTag(string? value)
{
    if (string.IsNullOrWhiteSpace(value)) return null;
    return value.Trim().ToLowerInvariant();
}

static object? CloneJsonBackedValue(object? value)
{
    if (value is JsonElement el) return el.Clone();
    return value;
}

static SubtitleContributorDto[]? MapContributors(List<SubtitleContributor>? contributors)
{
    if (contributors == null || contributors.Count == 0) return null;
    var list = new List<SubtitleContributorDto>();
    foreach (var c in contributors.OrderBy(x => x.Version))
    {
        DateTimeOffset.TryParse(c.SubmittedAt, out var ts);
        list.Add(new SubtitleContributorDto(c.Version, c.UserId, c.DisplayName, ts == default ? DateTimeOffset.MinValue : ts));
    }
    return list.ToArray();
}

static List<Dictionary<string, object?>> SerializeContributors(IEnumerable<SubtitleContributor> contributors)
{
    var list = new List<Dictionary<string, object?>>();
    foreach (var c in contributors)
    {
        list.Add(new Dictionary<string, object?>
        {
            ["version"] = c.Version,
            ["userId"] = string.IsNullOrWhiteSpace(c.UserId) ? null : c.UserId,
            ["displayName"] = string.IsNullOrWhiteSpace(c.DisplayName) ? null : c.DisplayName,
            ["submittedAt"] = string.IsNullOrWhiteSpace(c.SubmittedAt) ? DateTimeOffset.UtcNow.ToString("u") : c.SubmittedAt
        });
    }
    return list;
}

static int? TryParseSubtitleVersionFromUrl(string? subtitleUrl)
{
    if (string.IsNullOrWhiteSpace(subtitleUrl)) return null;
    try
    {
        var last = Path.GetFileName(subtitleUrl);
        if (string.IsNullOrWhiteSpace(last)) return null;
        if (last.EndsWith(".srt", StringComparison.OrdinalIgnoreCase))
        {
            last = last[..^4];
        }
        if (last.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            last = last.Substring(1);
        }
        if (int.TryParse(last, out var version) && version > 0)
        {
            return version;
        }
    }
    catch { }
    return null;
}

static string? ResolveSubtitlePath(string root, string sanitizedId, int version)
{
    var path = Path.Combine(root, sanitizedId, $"v{version}.srt");
    return File.Exists(path) ? path : null;
}

static string? ResolvePreviousSubtitlePath(string root, string sanitizedId, int version)
{
    for (var prev = version - 1; prev >= 1; prev--)
    {
        var candidate = ResolveSubtitlePath(root, sanitizedId, prev);
        if (candidate != null) return candidate;
    }
    return null;
}

static int? FindVersionFromPath(string? path)
{
    if (string.IsNullOrWhiteSpace(path)) return null;
    var name = Path.GetFileNameWithoutExtension(path);
    if (string.IsNullOrWhiteSpace(name)) return null;
    if (name.StartsWith("v", StringComparison.OrdinalIgnoreCase))
    {
        name = name.Substring(1);
    }
    return int.TryParse(name, out var ver) ? ver : null;
}

static (int Added, int Removed) ComputeSubtitleDiffStats(string root, string sanitizedId, int version)
{
    var currentPath = ResolveSubtitlePath(root, sanitizedId, version);
    if (currentPath == null) return (0, 0);
    var prevPath = ResolvePreviousSubtitlePath(root, sanitizedId, version);
    var currLines = File.ReadAllLines(currentPath);
    var prevLines = prevPath != null ? File.ReadAllLines(prevPath) : Array.Empty<string>();
    var (_, lcs) = BuildLcsTable(prevLines, currLines);
    var added = currLines.Length - lcs;
    var removed = prevLines.Length - lcs;
    return (Math.Max(0, added), Math.Max(0, removed));
}

static (int[,] Table, int Length) BuildLcsTable(IReadOnlyList<string> oldLines, IReadOnlyList<string> newLines)
{
    var table = new int[oldLines.Count + 1, newLines.Count + 1];
    for (int i = oldLines.Count - 1; i >= 0; i--)
    {
        for (int j = newLines.Count - 1; j >= 0; j--)
        {
            if (string.Equals(oldLines[i], newLines[j], StringComparison.Ordinal))
            {
                table[i, j] = table[i + 1, j + 1] + 1;
            }
            else
            {
                table[i, j] = Math.Max(table[i + 1, j], table[i, j + 1]);
            }
        }
    }
    return (table, table[0, 0]);
}

static string BuildUnifiedDiff(string[] previous, string[] current, string prevPathLabel, string currentLabel)
{
    var (table, _) = BuildLcsTable(previous, current);
    var sb = new StringBuilder();
    sb.AppendLine($"--- {prevPathLabel}");
    sb.AppendLine($"+++ {currentLabel}");
    int i = 0, j = 0;
    while (i < previous.Length || j < current.Length)
    {
        if (i < previous.Length && j < current.Length && string.Equals(previous[i], current[j], StringComparison.Ordinal))
        {
            sb.AppendLine($" {previous[i]}");
            i++; j++;
            continue;
        }
        var down = i < previous.Length ? table[i + 1, j] : int.MinValue;
        var right = j < current.Length ? table[i, j + 1] : int.MinValue;
        if (j < current.Length && (down == int.MinValue || right >= down))
        {
            sb.AppendLine($"+{current[j]}");
            j++;
        }
        else if (i < previous.Length)
        {
            sb.AppendLine($"-{previous[i]}");
            i++;
        }
        else
        {
            break;
        }
    }
    return sb.ToString();
}

static string? ValidateCorrectionPayload(SubtitleCorrectionPayload body)
{
    if (string.IsNullOrWhiteSpace(body.VideoId)) return "missing_video_id";
    if (body.SubtitleVersion <= 0) return "invalid_subtitle_version";
    if (string.IsNullOrWhiteSpace(body.SubmittedByUserId)) return "missing_user";
    if (body.Cues == null || body.Cues.Count == 0) return "missing_cues";
    foreach (var cue in body.Cues)
    {
        if (cue.Sequence <= 0) return "invalid_sequence";
        if (cue.UpdatedText.Length > 1000 || cue.OriginalText.Length > 1000) return "cue_too_long";
        if (string.IsNullOrWhiteSpace(cue.UpdatedText)) return "missing_updated_text";
    }
    if (!string.IsNullOrWhiteSpace(body.Notes) && body.Notes.Length > 500) return "notes_too_long";
    if (body.WindowEndSeconds < body.WindowStartSeconds) return "invalid_window";
    return null;
}

static void NormalizeCorrectionPayload(SubtitleCorrectionPayload body)
{
    body.VideoId = body.VideoId.Trim();
    body.Notes = string.IsNullOrWhiteSpace(body.Notes) ? null : Truncate(body.Notes.Trim(), 500);
    body.SubmittedByUserId = body.SubmittedByUserId.Trim();
    if (!string.IsNullOrWhiteSpace(body.SubmittedByDisplayName))
    {
        body.SubmittedByDisplayName = Truncate(body.SubmittedByDisplayName.Trim(), 200);
    }
    body.Cues ??= new List<SubtitleCorrectionCue>();
    foreach (var cue in body.Cues)
    {
        cue.OriginalText = NormalizeCorrectionText(cue.OriginalText);
        cue.UpdatedText = NormalizeCorrectionText(cue.UpdatedText);
    }
}

static string BuildCorrectionSubmitter(SubtitleCorrectionPayload body)
{
    if (!string.IsNullOrWhiteSpace(body.SubmittedByDisplayName))
    {
        return $"{body.SubmittedByDisplayName} ({body.SubmittedByUserId})";
    }
    return body.SubmittedByUserId;
}

static string NormalizeCorrectionText(string value)
{
    var normalized = value.Replace("\r\n", "\n").Replace("\r", "\n");
    return Truncate(normalized, 1000);
}

static string Truncate(string value, int max)
{
    if (string.IsNullOrEmpty(value)) return value;
    return value.Length <= max ? value : value[..max];
}

static (bool Ok, string? Error, string? UpdatedContent) TryApplyCueUpdates(string basePath, List<SubtitleCorrectionCue> patches)
{
    var cues = ParseSrtFile(basePath);
    if (cues.Count == 0) return (false, "empty_base_subtitles", null);
    var map = cues.ToDictionary(c => c.Sequence);
    foreach (var patch in patches)
    {
        if (!map.TryGetValue(patch.Sequence, out var cue))
        {
            return (false, "cue_not_found", null);
        }
        var current = NormalizeCorrectionText(string.Join("\n", cue.Lines));
        if (!string.Equals(current, NormalizeCorrectionText(patch.OriginalText), StringComparison.Ordinal))
        {
            return (false, "cue_conflict", null);
        }
        cue.Lines = NormalizeCorrectionText(patch.UpdatedText).Split('\n', StringSplitOptions.None).ToList();
    }
    var ordered = cues.OrderBy(c => c.Sequence).ToList();
    var builder = new StringBuilder();
    for (var i = 0; i < ordered.Count; i++)
    {
        var cue = ordered[i];
        builder.Append(cue.Sequence).AppendLine();
        builder.Append(FormatTimestamp(cue.Start))
            .Append(" --> ")
            .Append(FormatTimestamp(cue.End))
            .AppendLine();
        foreach (var line in cue.Lines)
        {
            builder.AppendLine(line);
        }
        if (i < ordered.Count - 1)
        {
            builder.AppendLine();
        }
    }
    return (true, null, builder.ToString());
}

static List<ParsedSrtCue> ParseSrtFile(string path)
{
    var text = System.IO.File.ReadAllText(path);
    var normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
    var blocks = normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
    var cues = new List<ParsedSrtCue>();
    foreach (var block in blocks)
    {
        var lines = block.Split('\n');
        if (lines.Length < 2) continue;
        if (!int.TryParse(lines[0].Trim(), out var seq)) continue;
        var arrowIdx = lines[1].IndexOf("-->", StringComparison.Ordinal);
        if (arrowIdx < 0) continue;
        var startText = lines[1].Substring(0, arrowIdx).Trim();
        var endText = lines[1].Substring(arrowIdx + 3).Trim();
        var start = ParseTimestamp(startText);
        var end = ParseTimestamp(endText);
        var cueLines = lines.Skip(2).ToList();
        cues.Add(new ParsedSrtCue(seq, start, end, cueLines));
    }
    return cues;
}

static TimeSpan ParseTimestamp(string value)
{
    if (TimeSpan.TryParse(value.Replace(',', '.'), out var direct)) return direct;
    var parts = value.Split(new[] { ':', ',', '.' }, StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length < 3) return TimeSpan.Zero;
    int h = int.Parse(parts[0]);
    int m = int.Parse(parts[1]);
    int s = int.Parse(parts[2]);
    int ms = parts.Length >= 4 ? int.Parse(parts[3]) : 0;
    return new TimeSpan(0, h, m, s, ms);
}

static string FormatTimestamp(TimeSpan ts)
{
    return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2},{ts.Milliseconds:D3}";
}

static int GetCurrentSubtitleVersion(string root, string sanitizedId)
{
    var dir = Path.Combine(root, sanitizedId);
    if (!Directory.Exists(dir)) return 0;
    var files = Directory.GetFiles(dir, "v*.srt");
    var max = 0;
    foreach (var file in files)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        if (name.StartsWith("v", StringComparison.OrdinalIgnoreCase) && int.TryParse(name.Substring(1), out var ver))
        {
            if (ver > max) max = ver;
        }
    }
    return max;
}

static int DetermineNextSubtitleVersion(string root, string sanitizedId)
{
    var current = GetCurrentSubtitleVersion(root, sanitizedId);
    var next = Math.Max(0, current) + 1;
    return next <= 0 ? 1 : next;
}

static void UpsertVideoIntoCatalogJson(string jsonPath, string youtubeId, string title, string? creatorCanonical, string? description, string[]? tags, string? releaseDate, string subtitleUrl, string? submitter, DateTimeOffset submittedAt, string? creatorOriginal = null, IEnumerable<SubtitleContributor>? subtitleContributors = null)
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
        if (!string.IsNullOrWhiteSpace(creatorCanonical)) existing["creator"] = creatorCanonical;
        if (!string.IsNullOrWhiteSpace(creatorOriginal)) existing["creatorOriginal"] = creatorOriginal;
        if (!string.IsNullOrWhiteSpace(description)) existing["description"] = description;
        if (tags != null && tags.Length > 0) existing["tags"] = tags;
        if (!string.IsNullOrWhiteSpace(releaseDate)) existing["releaseDate"] = releaseDate;
        existing["subtitleUrl"] = subtitleUrl;
        if (subtitleContributors != null)
        {
            existing["subtitleContributors"] = SerializeContributors(subtitleContributors);
        }
        if (!string.IsNullOrWhiteSpace(submitter)) existing["submitter"] = submitter;
        existing["submissionDate"] = submittedAt.ToString("yyyy-MM-dd");
        var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
        var normalized = list.Select(x =>
        {
            var o = new Dictionary<string, object?>();
            foreach (var k in new[] { "v", "title", "creator", "creatorOriginal", "description", "tags", "releaseDate", "subtitleUrl", "subtitleContributors", "submitter", "submissionDate" })
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

static int ReapplyCreatorMappingsToVideos(string jsonPath, CreatorMappingsRepository repo)
{
    try
    {
        if (!File.Exists(jsonPath)) return 0;
        var json = File.ReadAllText(jsonPath);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return 0;
        var list = new List<Dictionary<string, object?>>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) continue;
            var obj = new Dictionary<string, object?>();
            foreach (var prop in el.EnumerateObject())
            {
                obj[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Array => prop.Value.EnumerateArray().Select(x => x.GetString()).ToArray(),
                    _ => null
                };
            }
            list.Add(obj);
        }
        int changes = 0;
        foreach (var v in list)
        {
            var original = Convert.ToString(v.TryGetValue("creatorOriginal", out var co) ? co : v.TryGetValue("creator", out var c) ? c : null);
            var current = Convert.ToString(v.TryGetValue("creator", out var cur) ? cur : null);
            var canonical = repo.Resolve(original) ?? repo.Resolve(current) ?? current;
            if (!string.IsNullOrWhiteSpace(canonical) && !string.Equals(current, canonical, StringComparison.Ordinal))
            {
                v["creator"] = canonical;
                if (!string.IsNullOrWhiteSpace(original))
                {
                    v["creatorOriginal"] = original;
                }
                changes++;
            }
            else if (string.IsNullOrWhiteSpace(original) && !string.IsNullOrWhiteSpace(current))
            {
                // Preserve original if missing to support future reapply passes
                v["creatorOriginal"] = current;
            }
        }
        var opts = new JsonSerializerOptions { WriteIndented = true };
        var normalized = list.Select(x =>
        {
            var o = new Dictionary<string, object?>();
            foreach (var k in new[] { "v", "title", "creator", "creatorOriginal", "description", "tags", "releaseDate", "subtitleUrl", "submitter", "submissionDate" })
            {
                if (x.ContainsKey(k)) o[k] = x[k];
            }
            return o;
        }).ToList();
        File.WriteAllText(jsonPath, System.Text.Json.JsonSerializer.Serialize(normalized, opts));
        return changes;
    }
    catch { return 0; }
}

static int ReapplyCreatorMappingsToSubmissions(string jsonPath, CreatorMappingsRepository repo)
{
    try
    {
        if (!File.Exists(jsonPath)) return 0;
        var json = File.ReadAllText(jsonPath);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("Items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            int changes = 0;
            var arr = new List<JsonElement>();
            // We'll rebuild with mutable dictionaries
            var list = new List<Dictionary<string, object?>>();
            foreach (var el in items.EnumerateArray())
            {
                var obj = new Dictionary<string, object?>();
                foreach (var prop in el.EnumerateObject())
                {
                    if (prop.Name == "payload" && prop.Value.ValueKind == JsonValueKind.Object)
                    {
                        var payload = new Dictionary<string, object?>();
                        foreach (var p in prop.Value.EnumerateObject())
                        {
                            payload[p.Name] = p.Value.ValueKind switch
                            {
                                JsonValueKind.String => p.Value.GetString(),
                                JsonValueKind.True => true,
                                JsonValueKind.False => false,
                                JsonValueKind.Array => p.Value.EnumerateArray().Select(x => x.GetString()).ToArray(),
                                _ => null
                            };
                        }
                        var original = Convert.ToString(payload.TryGetValue("creator_original", out var co) ? co : payload.TryGetValue("creator", out var c) ? c : null);
                        if (!string.IsNullOrWhiteSpace(original))
                        {
                            var canonical = repo.Resolve(original) ?? original;
                            var current = Convert.ToString(payload.TryGetValue("creator_canonical", out var cc) ? cc : null);
                            if (!string.Equals(current, canonical, StringComparison.Ordinal))
                            {
                                payload["creator_original"] = original;
                                payload["creator_canonical"] = canonical;
                                changes++;
                            }
                        }
                        obj["payload"] = payload;
                    }
                    else
                    {
                        obj[prop.Name] = prop.Value.ValueKind switch
                        {
                            JsonValueKind.String => prop.Value.GetString(),
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            JsonValueKind.Array => prop.Value.EnumerateArray().Select(x => x.GetString()).ToArray(),
                            _ => null
                        };
                    }
                }
                list.Add(obj);
            }
            var root = new Dictionary<string, object?> { ["Items"] = list };
            var opts = new JsonSerializerOptions { WriteIndented = false };
            File.WriteAllText(jsonPath, System.Text.Json.JsonSerializer.Serialize(root, opts));
            return changes;
        }
        return 0;
    }
    catch { return 0; }
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

static string? MapStagingStorageKeyToPath(string stagingRoot, string storageKey)
{
    try
    {
        if (string.IsNullOrWhiteSpace(storageKey)) return null;
        var key = storageKey.Replace('\\', '/');
        if (!key.StartsWith("staging/", StringComparison.OrdinalIgnoreCase)) return null;
        var rel = key.Substring("staging/".Length);
        var parts = rel.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return null;
        var vid = SanitizeId(parts[0]);
        var verPart = parts[1];
        // Accept both "v1" and "v1.srt"
        var verNoExt = verPart.EndsWith(".srt", StringComparison.OrdinalIgnoreCase)
            ? verPart.Substring(0, verPart.Length - 4)
            : verPart;
        if (!verNoExt.StartsWith("v", StringComparison.OrdinalIgnoreCase)) return null;
        var path = Path.Combine(stagingRoot, vid, verNoExt + ".srt");
        return path;
    }
    catch { return null; }
}

string? PromoteStagedSubtitleToPublic(string storageKey, string videoId, int version)
{
    try
    {
        // Expected storageKey like: staging/{vid}/v{ver}.srt
        var key = storageKey.Replace('\\', '/');
        if (!key.StartsWith("staging/", StringComparison.OrdinalIgnoreCase)) return null;
        var rel = key.Substring("staging/".Length);
        var parts = rel.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return null;
        var vid = SanitizeId(parts[0]);
        // Source (staging) path
        var src = Path.Combine(SubtitlesStagingRoot(), vid, $"v{version}.srt");
        if (!File.Exists(src)) return null;
        // Destination (public) path
        var dstDir = Path.Combine(SubtitlesRoot(), vid);
        Directory.CreateDirectory(dstDir);
        var dst = Path.Combine(dstDir, $"v{version}.srt");
        File.Copy(src, dst, overwrite: true);
        return $"subtitles/{vid}/v{version}.srt";
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
        lock (StoreSync.Lock)
        {
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
    }
    catch { }
}

static void DeleteVideo(string jsonPath, string id)
{
    try
    {
        var full = Path.GetFullPath(jsonPath);
        if (!File.Exists(full)) return;
        lock (StoreSync.Lock)
        {
            var json = File.ReadAllText(full);
            var list = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(json) ?? new();
            var next = list.Where(x => (x.TryGetValue("v", out var vv) ? Convert.ToString(vv) : null) != id).ToList();
            var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(full, System.Text.Json.JsonSerializer.Serialize(next, opts));
        }
    }
    catch { }
}

static void SetTags(string jsonPath, string id, IEnumerable<string> tags)
{
    try
    {
        var full = Path.GetFullPath(jsonPath);
        if (!File.Exists(full)) return;
        lock (StoreSync.Lock)
        {
            var json = File.ReadAllText(full);
            var list = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(json) ?? new();
            var item = list.FirstOrDefault(x => (x.TryGetValue("v", out var vv) ? Convert.ToString(vv) : null) == id);
            if (item == null) return;
            var unique = tags.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            item["tags"] = unique;
            var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(full, System.Text.Json.JsonSerializer.Serialize(list, opts));
        }
    }
    catch { }
}

static void SetDuration(string jsonPath, string id, int durationSeconds)
{
    try
    {
        var full = Path.GetFullPath(jsonPath);
        if (!File.Exists(full)) return;
        lock (StoreSync.Lock)
        {
            var json = File.ReadAllText(full);
            var list = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(json) ?? new();
            var item = list.FirstOrDefault(x => (x.TryGetValue("v", out var vv) ? Convert.ToString(vv) : null) == id);
            if (item == null) return;
            item["durationSeconds"] = durationSeconds;
            var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(full, System.Text.Json.JsonSerializer.Serialize(list, opts));
        }
    }
    catch { }
}

static void AddTag(string jsonPath, string id, string tag)
{
    try
    {
        var full = Path.GetFullPath(jsonPath);
        if (!File.Exists(full)) return;
        lock (StoreSync.Lock)
        {
            var json = File.ReadAllText(full);
            var list = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(json) ?? new();
            var item = list.FirstOrDefault(x => (x.TryGetValue("v", out var vv) ? Convert.ToString(vv) : null) == id);
            if (item == null) return;
            
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (item.TryGetValue("tags", out var tagsValue) && tagsValue != null)
            {
                if (tagsValue is JsonElement tagsElement && tagsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in tagsElement.EnumerateArray())
                    {
                        var s = el.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) existing.Add(s);
                    }
                }
                else if (tagsValue is IEnumerable<object> objEnumerable)
                {
                     foreach (var o in objEnumerable)
                     {
                        var s = Convert.ToString(o);
                        if (!string.IsNullOrWhiteSpace(s)) existing.Add(s);
                     }
                }
            }
            
            existing.Add(tag);
            item["tags"] = existing.ToArray();
            
            var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(full, System.Text.Json.JsonSerializer.Serialize(list, opts));
        }
    }
    catch { }
}

static void RemoveTag(string jsonPath, string id, string tag)
{
    try
    {
        var full = Path.GetFullPath(jsonPath);
        if (!File.Exists(full)) return;
        lock (StoreSync.Lock)
        {
            var json = File.ReadAllText(full);
            var list = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(json) ?? new();
            var item = list.FirstOrDefault(x => (x.TryGetValue("v", out var vv) ? Convert.ToString(vv) : null) == id);
            if (item == null) return;

            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (item.TryGetValue("tags", out var tagsValue) && tagsValue != null)
            {
                if (tagsValue is JsonElement tagsElement && tagsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in tagsElement.EnumerateArray())
                    {
                        var s = el.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) existing.Add(s);
                    }
                }
                else if (tagsValue is IEnumerable<object> objEnumerable)
                {
                     foreach (var o in objEnumerable)
                     {
                        var s = Convert.ToString(o);
                        if (!string.IsNullOrWhiteSpace(s)) existing.Add(s);
                     }
                }
            }
            
            existing.RemoveWhere(s => string.Equals(s, tag, StringComparison.OrdinalIgnoreCase));
            item["tags"] = existing.ToArray();
            
            var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(full, System.Text.Json.JsonSerializer.Serialize(list, opts));
        }
    }
    catch { }
}

static void UpdateSubtitleMetadata(string jsonPath, string id, string subtitleUrl, IEnumerable<SubtitleContributor> contributors)
{
    try
    {
        var full = Path.GetFullPath(jsonPath);
        if (!File.Exists(full)) return;
        lock (StoreSync.Lock)
        {
            var json = File.ReadAllText(full);
            var list = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(json) ?? new();
            var item = list.FirstOrDefault(x => (x.TryGetValue("v", out var vv) ? Convert.ToString(vv) : null) == id);
            if (item == null) return;
            item["subtitleUrl"] = subtitleUrl;
            item["subtitleContributors"] = SerializeContributors(contributors);
            var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(full, System.Text.Json.JsonSerializer.Serialize(list, opts));
        }
    }
    catch { }
}

// Note: In a file that uses top-level statements, any
// type declarations must appear after all statements.
// Keeping these at the end avoids CS8803.
record ParsedSrtCue(int Sequence, TimeSpan Start, TimeSpan End, List<string> Lines)
{
    public List<string> Lines { get; set; } = Lines;
}
internal record VideoDto(
    string Id,
    string Title,
    string? Creator,
    string? Description,
    string[]? Tags,
    string? ReleaseDate,
    string YoutubeId,
    string? SubtitleUrl,
    SubtitleContributorDto[]? SubtitleContributors,
    int Red,
    int Yellow,
    int Green
);

internal record SubtitleVersionSummary(
    int Version,
    string? DisplayName,
    string? UserId,
    string? SubmittedAt,
    long SizeBytes,
    int AddedLines,
    int RemovedLines,
    bool IsCurrent
);

internal record SubtitleContributorDto(int Version, string? UserId, string? DisplayName, DateTimeOffset SubmittedAt);

// Centralized lock for serializing read-modify-write operations on the videos store file.
internal static class StoreSync
{
    public static readonly object Lock = new object();
}

internal record RatingRequest([property: JsonConverter(typeof(JsonStringEnumConverter))] RatingValue Value, int Version);

public partial class Program { }
public record NewMappingDto([property: JsonPropertyName("source")] string? Source, [property: JsonPropertyName("canonical")] string? Canonical, [property: JsonPropertyName("notes")] string? Notes);
public record LegacyVideosMigrationRequest([property: JsonPropertyName("dryRun")] bool? DryRun);
public record LegacyVideosMigrationResult(
    bool Ok,
    bool DryRun,
    string Mode,
    string LegacyPath,
    LegacyVideosMigrationTotals Totals,
    IReadOnlyList<LegacyVideosMigrationDetail> DetailsSample,
    DateTimeOffset RanAtUtc,
    string? ErrorCode,
    string? Error);
public class LegacyVideosMigrationTotals
{
    public int LegacyCount { get; set; }
    public int Created { get; set; }
    public int UpdatedMissingFields { get; set; }
    public int TagsMerged { get; set; }
    public int Unchanged { get; set; }
    public int SkippedInvalid { get; set; }
    public int Errors { get; set; }
}
public record LegacyVideosMigrationDetail(
    string VideoId,
    string Action,
    IReadOnlyList<string>? FieldsFilled,
    IReadOnlyList<string>? TagsAdded,
    string? Error);
