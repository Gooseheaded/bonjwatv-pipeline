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
  - URL or path to the SRT subtitles (e.g. a pastebin raw URL)

## C. Search
- Implement simple title matching (case-insensitive substring search) initially.
- Extend search to include tags as well:
  - Split the query string on whitespace into tokens.
  - For each token, a video matches if the token (case-insensitive) is found in the video title _or_ exactly matches any tag code or tag label.
  - Use AND semantics: include a video only if _all_ tokens match in title or tags.
- Keep the code flexible for future fuzzy search or advanced filtering.

## D. Subtitle Integration
- The webapp will load subtitle references directly from the local JSON data file and will not depend on any external subtitle‑service (your personal Google Sheets remains for your own bookkeeping).
- Use a minimal client‑side JS approach (inspired by the current userscript) to:
  1. Fetch the raw SRT file from its known URL.
  2. Parse SRT timestamps and text cues.
  3. Synchronize cue display with the YouTube iframe player's `currentTime`.
- Avoid heavy front‑end dependencies; write a small SRT parser and rendering overlay.
- Use the YouTube IFrame embed API for the video player integration.

## E. UI Pages
- **Homepage**: Grid of all supported videos (thumbnail + title) with a search bar at the top.
- **Search Results**: Filtered grid view showing videos matching the query.
- **Watch Page**: Embedded YouTube player with a custom subtitle overlay below the video.
- **Login & Signup**: Simple stub pages for future authentication.
- Keep all pages SSR‑rendered and styling minimal (no Web Components or large frameworks).

## Next Steps
1. Create `data/videos.json` and load it in the startup of the Razor‑Pages app.
2. Scaffold Razor Pages for Home, Search, Watch, Login, and Signup.
3. Implement server‑side search and filtering using the JSON data.
4. Write minimal client‑side JS for SRT parsing and subtitle rendering.
5. Apply basic styling (CSS or lightweight framework) to match a YouTube‑like grid.
6. Plan for future enhancements: fuzzy search, authentication flow, production subtitle-service, etc.

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

- **videos.json**: JSON file in `data/` containing an array of video entries (optional `tags` array):
  ```json
  [
    { "v": "abc123", "title": "My Video", "description": "...", "subtitleUrl": "...", "tags": ["z", "p"] },
    …
  ]
  ```
- **VideoInfo.cs**: Plain C# record or class matching a JSON entry; add a nullable `Tags` property bound to JSON `tags`.

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