# BWKT Webapp

A simple ASP.NET Core Razor Pages app that wraps YouTube videos with custom subtitles and tag-based filtering.

## Features

- Browse a grid of supported videos with thumbnails and titles
- Search videos by title or tags (Zerg, Protoss, Terran) with case-insensitive, multi-token queries
- Watch page with embedded YouTube player and synchronized subtitle overlay
- Adjustable subtitle font size (saved in browser storage)
- Automatic reload of `data/videos.json` when modified
- Minimal front-end dependencies; custom SRT parser in `wwwroot/js/subtitles.js`
- Test-first workflow with xUnit unit and integration tests

## Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download)
- A modern web browser (Chrome, Firefox, Edge, etc.)

## Getting Started

1. **Clone the repository**
   ```bash
   git clone <repository-url>
   cd bwkt-webapp
   ```

2. **Restore and build**
   ```bash
   dotnet restore
   dotnet build
   ```

3. **Run the application**
   ```bash
   dotnet run
   ```

   The app will be available at `https://localhost:5001` (or the URL shown in the console).

## Configuration & Data

- Video metadata is stored in `data/videos.json`. Edit this file to add or update videos, subtitles, and tags.
- Tags are simple codes (`z`, `p`, `t`) for Zerg, Protoss, and Terran, displayed as Bootstrap badges.
- Changes to `data/videos.json` are hot-reloaded by the app.

## Testing

Run the xUnit test suite (unit + integration) with:
```bash
dotnet test tests/bwkt-webapp.Tests/bwkt-webapp.Tests.csproj
```

## Project Roadmap

See [Monorepo Plan](./PLAN.md) for the full project plan, feature overview, and development roadmap.

## Contributing

Contributions are welcome! Feel free to open issues or submit pull requests.
