# BWKT Webapp Plan

A simple Razor‑Pages app serving as a YouTube wrapper with custom subtitles.

## A. Domain & Hostname
- Purchase a custom domain (e.g. `bonjwa.tv`) so users can replace `youtube.com` with `bonjwa.tv` for translated videos.

## B. Master Video List
- Store the list of supported videos in a local JSON file (e.g. `data/videos.json`).
- Load or reload the JSON file server-side on startup or on file change.
- Each entry in `videos.json` should include:
  - `videoId` (YouTube ID)
  - `title`
  - `description` (optional)
  - `creator` (author or channel name)
  - URL or path to the SRT subtitles (e.g. a pastebin raw URL)

## C. Search
- **SQLite FTS5** (primary): full-text search across videoId, creator, title, tags, and subtitles with BM25 ranking (implemented via SubtitlesCache and VideoService).
- **In-memory fallback**: whitespace-based AND match on title, creator, and tags when the FTS5 index is unavailable.

## D. Subtitle Integration
- The webapp will load subtitle references directly from the local JSON data file and will not depend on any external subtitle‑service (your personal Google Sheets remains for your own bookkeeping).
- Use a minimal client‑side JS approach (inspired by the current userscript) to:
  1. Fetch the raw SRT file from its known URL.
  2. Parse SRT timestamps and text cues.
  3. Synchronize cue display with the YouTube iframe player's `currentTime`.
- Avoid heavy front‑end dependencies; write a small SRT parser and rendering overlay.
- Use the YouTube IFrame embed API for the video player integration.
- **Server-side subtitle downloads**:
  - Enhance `SubtitlesCache` to download each video's SRT (`subtitleUrl`) into `data/subtitles/{videoId}.srt`.
  - Rate-limit downloads to one fetch per **initialDelay** interval (default 5 s).
  - On failures, apply exponential backoff (doubling the delay) up to a configurable max retry count.
  - After fetching (or using existing files), rebuild/update the SQLite FTS5 `video_index` so subtitles are included in full-text search.

## E. UI Pages
- **Homepage**: Paginated grid of supported videos (thumbnail + title + creator) with a search bar at the top.
- **Search Results**: Filtered grid view showing videos matching the query, including creator names.
- **Watch Page**: Embedded YouTube player with creator name, custom subtitle overlay, and controls.
- **Login & Signup**: Simple stub pages for future authentication.
- Keep all pages SSR‑rendered and styling minimal (no Web Components or large frameworks).

## J. Pagination

- **Server-side**:
  1. Accept optional `pageNum` (int, default 1) and `pageSize` (int, default 24) parameters in Index and Search page models.
  2. Compute `TotalCount` and `TotalPages = ceil(TotalCount / pageSize)`.
  3. Query `VideoService.GetAll()` or `VideoService.Search(q)` then apply `.Skip((pageNum - 1) * pageSize).Take(pageSize)`.
  4. Pass pagination metadata (`CurrentPage`, `PageSize`, `TotalPages`, `TotalCount`) to the Razor view.

- **UI**:
  - Use Bootstrap's Pagination component below the video grid.
  - Render "Previous" and "Next" links; disable at first/last pages.
  - Render numeric page links (or a sliding window) preserving search query (`q`) if present.

- **Service evolution**:
  - Optionally add a paged lookup API on `IVideoService` (e.g. `GetPaged(pageNum, pageSize)`) returning `(Items, TotalCount)` for cleaner PageModel logic.

## Next Steps
1. Create `data/videos.json` and load it in the startup of the Razor‑Pages app.
2. Scaffold Razor Pages for Home, Search, Watch, Login, and Signup.
3. Implement server‑side search and filtering using the JSON data.
4. Write minimal client‑side JS for SRT parsing and subtitle rendering.
5. **(Done)** SubtitlesCache + SQLite FTS5 index for full‑text search and BM25‑based ranking.
6. Implement rate-limited SRT downloader in SubtitlesCache (one fetch per 5 s, exponential backoff on retry).
7. Apply basic styling (CSS or lightweight framework) to match a YouTube‑like grid.
8. Plan for future enhancements: fuzzy search, authentication flow, production subtitle-service, etc.
8. Add a `creator` field to `data/videos.json`, update model, and display creators in the UI pages.
9. Implement pagination support on Home (and Search) pages: server-side page parameters, view pagination controls.

## MVP Decisions

Based on the current MVP scope and constraints:

- Implement simple SSR pages for Home, Search, Watch, Login, and Signup without detailed wireframes.
- Use Bootstrap (v5) for lightweight styling and grid layout.
- Create a minimal English-only SRT parser in `wwwroot/js/subtitles.js`.
- Enhance `VideoService` to watch `data/videos.json` and reload automatically on file changes.
- Defer custom domain setup; assume `localhost` in development.
- Follow a **test-first** workflow: write xUnit tests for new features before implementation.

## Architecture Overview

The following describes the key building blocks and folder structure of the application.

### Folder Layout

<project root>/
├── data/videos.json           # Master list of videos and subtitle URLs
├── Models/
│   └── VideoInfo.cs           # C# model representing a video entry
├── Services/
│   ├── IVideoService.cs       # Interface for video lookup and search
│   └── VideoService.cs        # Implementation loading data/videos.json and providing search
├── Pages/
│   ├── Index.cshtml           # Homepage (grid + search)
│   ├── Index.cshtml.cs        # Index page model
│   ├── Search.cshtml          # Search results
│   ├── Search.cshtml.cs       # Search page model
│   ├── Watch.cshtml           # Watch page (video player + subtitles overlay)
│   ├── Watch.cshtml.cs        # Watch page model
│   └── Account/
│       ├── Login.cshtml       # Login page stub
│       ├── Login.cshtml.cs
│       ├── Signup.cshtml      # Signup page stub
│       └── Signup.cshtml.cs
├── wwwroot/
│   ├── css/site.css           # Global styles
│   └── js/subtitles.js        # Client-side SRT parser and subtitle overlay logic
├── Program.cs                 # App startup and service registration
└── PLAN.md                    # This design document

### Data Layer

- **videos.json**: JSON file in `data/` containing an array of video entries with `creator` and optional `tags`:
  ```json
  [
    { "v": "abc123", "title": "My Video", "description": "...", "creator": "SomeName", "subtitleUrl": "...", "tags": ["z", "p"] },
    …
  ]
  ```
- **VideoInfo.cs**: Plain C# record or class matching a JSON entry; include nullable `Creator` and `Tags` properties bound to JSON `creator` and `tags`.

### Service Layer

- **IVideoService**: Defines methods to list all videos and search by title.
- **VideoService**: Singleton service that reads and caches `videos.json` at startup (and watches for file changes). Exposes search and lookup APIs to page models.

### Razor Pages & Page Models

- **Index**: Renders homepage grid and search bar. Uses `VideoService.GetAll()` for initial load.
- **Search**: Accepts a `q` query parameter, calls `VideoService.Search(q)`, and renders filtered grid.
- **Watch**: Takes a `videoId` parameter, retrieves that entry via `VideoService.GetById()`, and renders the YouTube embed + subtitle overlay container.
- **Account/Login & Signup**: Stubs for future authentication flows.
- `_Layout.cshtml`: Shared site layout, navigation, and search form.

### Client-side Script

- **subtitles.js**: Minimal JavaScript loaded on the Watch page to:
  1. Fetch the SRT file from `subtitleUrl`.
  2. Parse SRT into cues (start/end/text).
  3. Hook into the YouTube IFrame API or HTML5 `video` element to synchronize and render subtitles in-dome overlay.

### Configuration & Dependency Injection

- Register `VideoService` as a singleton in `Program.cs`.
- Configure Razor Pages endpoints for `/`, `/search`, `/watch`, `/account/login`, and `/account/signup`.
- Enable global lowercase URLs by adding routing configuration:
  ```csharp
  builder.Services.AddRouting(options => options.LowercaseUrls = true);
  ```
- Serve `data/videos.json` as a physical file (read-only) via `FileProvider`, not as a static asset.

## G. Tags

- Extend each video entry in `data/videos.json` with an optional `tags` array of string codes (e.g. `["z", "p", "t"]`).
- Update `VideoInfo.cs` to include a nullable `Tags` property bound to JSON `tags`.
- In the Index, Search, and Watch Razor Pages, render tags as Bootstrap badges using the following mapping:

| Code | Label   | Badge CSS class   |
|:----:|:--------|:------------------|
| z    | Zerg    | bg-danger         |
| p    | Protoss | bg-warning        |
| t    | Terran  | bg-primary        |
| *    | (code)  | bg-secondary      |

- No server-side tag management UI; tags are manually managed in the JSON file.

## H. Centralized Tag‑Badge Helper

- Create a static helper class in `Helpers/TagBadge.cs` with a single `Get(string code)` method that returns `(string Text, string CssClass)`.
- Move the tag-to-label-and-class mapping (z, p, t and fallback) into this helper so updating or adding a new code only requires one file change.
- Refactor the Index, Search, and Watch Razor Pages to call `TagBadge.Get(tag)` instead of duplicating the switch logic.

## I. Light/Dark Mode Toggle

- Add a theme toggle button to the shared navbar (`_Layout.cshtml`) for light/dark mode.
- Use Bootstrap 5.3's color modes via the `data-bs-theme` attribute on `<html>` to switch themes.
- Implement JavaScript in `site.js` to:
  1. Load the saved theme (from `localStorage`) or detect `prefers-color-scheme`.
  2. Set `document.documentElement.setAttribute("data-bs-theme", theme)`.
  3. Toggle theme on button click, update the attribute, icon, and persist in `localStorage`.

- **TODO**: Add end-to-end or browser-level tests for the theme toggle (e.g. via Playwright) later.

## F. Testing Strategy

- **Test project layout**: add an xUnit test project (e.g. `tests/bwkt-webapp.Tests`) alongside the main app solution.
- **Unit tests**:
  - **VideoService tests**: validate JSON deserialization, `GetAll()`, `GetById()`, and `Search()` using sample JSON fixtures.
  - **PageModel tests**: instantiate page models (IndexModel, SearchModel, WatchModel) and verify `OnGet()` behaviors and data bindings.
- **Integration tests**:
  - Use `WebApplicationFactory<Program>` (from `Microsoft.AspNetCore.Mvc.Testing`) to host an in-memory app.
  - Perform HTTP GETs against `/`, `/search?q=`, and `/watch?videoId=` to assert status codes and basic HTML content.
- **Client-side script tests**: skip for now (small, self-contained `subtitles.js`); add minimal JS tests later if desired.

This provides a solid baseline to build with confidence without targeting full coverage.

## Ephemeral YouTube + SRT Preview Wizard

### Overview
- A client-side, two-step wizard allowing any user to preview a YouTube video with a local `.srt` subtitle file without persisting data server-side.
- **Step 1:** User enters a YouTube URL and selects an `.srt` file.
- **Step 2:** The app displays the embedded YouTube player and renders subtitles from the provided file.

### Implementation Plan
1. **Razor Page `/Preview`**
   - Two containers: a Step 1 form (`id="uploadForm"`) for URL + file input, and a hidden Step 2 view for the player (`iframe#player`) and subtitle overlay (`div#subtitle-container`).
2. **Client-side JavaScript** in `Preview.cshtml`:
   - Intercept the form submit to:
     1. Extract `videoId` from the URL.
     2. Read the `.srt` file via `FileReader.readAsText()` → UTF-8 text.
     3. Base64-encode and build a `data:text/plain;base64,…` URI.
   - Swap Step 1 for Step 2:
     1. Set `iframe.src="https://www.youtube.com/embed/${videoId}?enablejsapi=1"`.
     2. Call `initSubtitles(dataUri, 'player', 'subtitle-container')` (JS SRT parser).
   - **Back button reloads the page** (`location.reload()`) to fully clear video/subtitle state (
     avoids lingering subtitle cues from previous runs).
3. **Layout Update**: Add a "Preview" link to `_Layout.cshtml` navigation to `/Preview`.

### Testing Plan (Server-side)
- **Integration test `PreviewPageTests`**: GET `/Preview`, assert 200 OK and presence of form elements (`uploadForm`, `videoUrl`, `subtitleFile`).
- **PageModel smoke test**: Add `PreviewModel_OnGet_DoesNotThrow()` in existing `PageModelTests`.

### Next Steps
1. Write the integration and model tests first.
2. Implement the `/Preview` page, inlined JS wizard, and nav link.

## K. Google Analytics Instrumentation

You’re all set for basic page‑view tracking – the GA4 snippet in your layout (`_Layout.cshtml`) automatically records each server‑rendered page load.

**Client‑side custom events** (implemented):
| Interaction               | GA event call                                                | Location                    |
|:--------------------------|:-------------------------------------------------------------|:----------------------------|
| Search queries            | `gtag('event','search',{query:q})`                           | `wwwroot/js/site.js`        |
| Video play                | `gtag('event','video_play',{video_id:…})`                     | `wwwroot/js/subtitles.js`   |
| Video complete            | `gtag('event','video_complete',{video_id:…})`                 | `wwwroot/js/subtitles.js`   |
| Subtitle font resize      | `gtag('event','subtitle_font_resize',{size:…})`               | `Pages/Watch.cshtml`        |
| Preview wizard start      | `gtag('event','preview_start',{video_id:…})`                  | `Pages/Preview.cshtml`      |
| Preview wizard cancel     | `gtag('event','preview_cancel')`                              | `Pages/Preview.cshtml`      |

**Next step**:
1. Enable GA4 → BigQuery export for long‑term event data retention.
3. Manually verify the preview flow in browser.

## L. Release Date Display and Sorting

- **Goal**: Display the `releaseDate` from `videos.json` and allow sorting by it.
- **Backend**:
    1.  Update `Models/VideoInfo.cs` to include a nullable `releaseDate` string property.
    2.  Modify `Services/VideoService.cs` to handle the new field.
    3.  Update `PageModels` (Index and Search) to accept a `sortBy` query parameter (e.g., `?sortBy=date_asc`, `?sortBy=date_desc`).
    4.  In the PageModels, apply sorting to the video list before pagination.
- **Frontend**:
    1.  On the Index and Search pages, add a dropdown or links to trigger sorting.
    2.  Display the `releaseDate` (formatted for readability, e.g., "Oct 26, 2023") on video cards.
    3.  Handle cases where `releaseDate` is null or missing.
- **Testing**:
    1.  Add a unit test to `VideoServiceTests.cs` to verify that `releaseDate` is correctly deserialized.
    2.  Add a unit test to `PageModelTests.cs` to verify that the sorting logic works as expected.
    3.  Add an integration test to `IntegrationTests.cs` to verify that the sorting UI works and the correct data is displayed.