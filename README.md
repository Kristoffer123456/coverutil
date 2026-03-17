# coverutil

A lightweight Windows system tray utility that watches a `now_playing.txt` file and automatically fetches the current song's album art from the Spotify API, saving it as a standardized 640×640 JPEG. Designed for streamers using OBS or similar tools that display cover art as a browser/image source.

## Features

- Watches a text file for changes and fetches album art automatically
- Saves a standardized 640×640 JPEG output file (ideal for OBS image sources)
- Falls back to a configurable default cover image when no track is playing
- System tray icon with track/status info and a cover thumbnail
- Main window with full-size cover display, inline settings, and inline log viewer
- Artist name verification to avoid incorrect matches
- Retries with `&` if artist name contains "og" or "and" (e.g. Norwegian artist names)
- Session cache — same track won't hit Spotify twice while the app is running
- Remembers window position between sessions
- Detailed and brief log files for debugging
- Close to tray or quit on window close (configurable)

## Requirements

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8) (for building from source)
- A Spotify app with Client ID and Client Secret ([create one here](https://developer.spotify.com/dashboard))

## Setup

### 1. Get Spotify credentials

1. Go to the [Spotify Developer Dashboard](https://developer.spotify.com/dashboard)
2. Create a new app (any name, any redirect URI — e.g. `http://localhost`)
3. Copy the **Client ID** and **Client Secret**

### 2. Configure `now_playing.txt`

coverutil reads a plain text file in the format:

```
Artist - Title
```

For example: `Radiohead - Creep`

This file is typically written by your streaming software, a media player plugin, or a custom script. The file is watched for changes — any write triggers a cover fetch.

If the file is empty or doesn't match the `Artist - Title` format, the default cover is applied (if configured).

### 3. Run and configure

1. Launch `coverutil.exe` — it appears in the system tray
2. Right-click the tray icon → **Settings...**
3. Fill in:
   - **Spotify Client ID** and **Client Secret**
   - **now_playing.txt** — path to the file your source writes to
   - **Output file** — where the cover JPEG should be saved (e.g. `C:\obs\cover.jpg`)
   - **Default cover** — image to show when no track is playing (optional)
4. Click **Save** — the watcher starts immediately

### 4. Add to OBS

Add an **Image** source in OBS pointing to your output file. Enable "Unload image when not showing" if you want OBS to pick up changes automatically, or use a browser source with a local file URL.

## Building from source

```
dotnet build
```

To produce a standalone single-file executable:

```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true
```

The output will be in `bin\Release\net8.0-windows\win-x64\publish\`.

## Log files

Two log files are written to `%APPDATA%\coverutil\`:

| File | Contents |
|------|----------|
| `coverutil-app.log` | Brief operational log — track changes, errors, status |
| `coverutil.log` | Detailed debug log — includes full Spotify API responses |

Access logs from the tray menu → **View log**, or from the main window → **View Log**.

## Tray menu

| Item | Action |
|------|--------|
| Cover thumbnail | Opens cover preview window |
| Track name | Shows current artist and title |
| Status | Shows current state (Watching / Fetching / OK / Error) |
| Open window | Opens the main cover window |
| Open now_playing folder | Opens the folder containing `now_playing.txt` |
| Open cover folder | Opens the folder containing the output file |
| Show cover preview | Opens a floating cover preview window |
| Settings... | Opens the settings dialog |
| View log | Opens the in-app log viewer |
| Quit | Exits the application |

## How it works

1. `FileSystemWatcher` monitors the configured `now_playing.txt` file for writes
2. On change, the file is read and parsed as `Artist - Title`
3. The Spotify search API is queried using field filters (`artist:X track:Y`) for precision
4. The first result's artist name is verified against the search query (diacritic-normalized, substring match) — mismatches trigger a retry with `og`/`and` substituted to `&`
5. The largest available album image is downloaded and resized to 640×640 JPEG, written to the output path

## Project structure

```
coverutil/
├── Program.cs           # Entry point
├── TrayApp.cs           # ApplicationContext: tray icon, watcher, orchestration
├── MainForm.cs          # Main window — cover, settings, and log tabs
├── SettingsForm.cs      # Settings dialog (standalone, used until UI redesign ships)
├── SpotifyClient.cs     # Spotify API: token cache, search, image download
├── AppConfig.cs         # Config model (persisted to %APPDATA%\coverutil\config.json)
├── ImageHelper.cs       # Image resize to 640×640 JPEG
├── Logger.cs            # Two-tier logging
├── LogViewerForm.cs     # In-app log viewer (standalone, used until UI redesign ships)
├── CoverPreviewForm.cs  # Floating cover preview window
└── assets/
    └── tray-icon.ico
```

## Development

### Running tests

```bash
dotnet test coverutil.Tests/ --filter "Category!=Integration"   # unit tests (fast, no network)
dotnet test coverutil.Tests/ --filter "Category=Integration"    # requires SPOTIFY_CLIENT_ID + SPOTIFY_CLIENT_SECRET env vars
```

### Git workflow

Work on short-lived feature branches, merge to `master` when done:

```bash
git checkout -b feature/my-feature
# ... commit as you go ...
dotnet test coverutil.Tests/ --filter "Category!=Integration"
git checkout master && git merge feature/my-feature
git branch -d feature/my-feature
```

### Releasing

1. Bump `<Version>` in `coverutil.csproj`
2. Build and publish:

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/
```

3. Tag and create GitHub release:

```bash
git add coverutil.csproj && git commit -m "chore: bump version to X.Y.Z"
git tag vX.Y.Z && git push && git push origin vX.Y.Z
gh release create vX.Y.Z publish/coverutil.exe --title "coverutil vX.Y.Z" --notes "..."
```
