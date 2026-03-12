# coverutil UI Redesign — Design Spec

**Date:** 2026-03-12
**Status:** Approved

---

## Overview

Three coordinated improvements:

1. **Main window redesign** — dark theme, inline tabbed Settings and Log panels, custom bottom tab strip
2. **Session cache** — in-memory Spotify result cache to avoid redundant API calls
3. **Window position memory** — persist and restore main window position and size

---

## 1. Main Window Redesign

### Visual language

- Background: `#0e0e0e` (near-black)
- Surface: `#1a1a1a` (slightly lighter, for cover background)
- Border/separator: `#1e1e1e`
- Primary text: `#cccccc`
- Secondary text / inactive tabs: `#333333`
- Accent: `#6a9e98` (dusty teal) — used for active tab indicator, status text, focused inputs
- Font: system default (Segoe UI on Windows)

### Layout

```
┌─────────────────────────────┐
│                             │
│        [tab content]        │  ← fills all space above status bar
│                             │
├─────────────────────────────┤
│  Artist — Title      status │  ← slim status bar, always visible (~22px)
├─────────────────────────────┤
│  Cover  │ Settings │  Log   │  ← custom tab strip (~32px)
└─────────────────────────────┘
```

### Tab strip

- Implemented as a `FlowLayoutPanel` or manual `Panel` at the bottom of the form, height 32px
- Three tab labels: **Cover**, **Settings**, **Log**
- Active tab: accent color text + 2px accent top border
- Inactive tabs: `#333333` text, no border
- Clicking a tab shows the corresponding panel and hides the other two

### Status bar

- Slim panel (~22px) above the tab strip, always visible
- Left: track label (`Artist — Title` or `No track`) in `#cccccc`
- Right: status text (`OK`, `Fetching...`, `Error: ...`) in accent color for OK, `#888` for idle, `#c0392b` for errors
- Replaces the existing `TableLayoutPanel` strip

### Cover tab

- `PictureBox` fills the entire content area above the status bar
- `SizeMode.Zoom`, background `#1a1a1a`
- Sized explicitly via `OnLayout` override (same approach as current code)

### Settings tab

- Contents of the current `SettingsForm` moved into a `Panel` with dark styling
- Labels: `#888888`, TextBoxes: dark background `#1a1a1a`, border `#2a2a2a`, text `#cccccc`
- Browse buttons and Save button styled to match dark theme
- Status feedback label for save result (green/red)
- `SettingsForm.cs` is deleted; settings are always inline

### Log tab

- Contents of the current `LogViewerForm` moved into a `Panel`
- Dark `RichTextBox` (`#0e0e0e` background, `#888888` text)
- "Refresh" and "Open full log" buttons styled to match
- `LogViewerForm.cs` is deleted

### Tray menu changes

- "Settings..." item → opens main window and switches to Settings tab
- "View log" item → opens main window and switches to Log tab
- `CoverPreviewForm` remains unchanged (standalone floating window)

### Files changed

| File | Change |
|------|--------|
| `MainForm.cs` | Full rewrite — dark theme, status bar, custom tab strip, three inline panels |
| `SettingsForm.cs` | Deleted — content moved into MainForm's Settings panel |
| `LogViewerForm.cs` | Deleted — content moved into MainForm's Log panel |
| `TrayApp.cs` | Update `OpenSettings()` and `ViewLog()` to open MainForm at correct tab |

---

## 2. Session Cache

### Purpose

Avoid hitting the Spotify API twice for the same track within a single session. Particularly useful for tracks that repeat across a broadcast day or when the watcher fires multiple times for the same file write.

### Implementation

A `Dictionary<string, string>` field in `SpotifyClient`:

```csharp
private readonly Dictionary<string, string> _urlCache = new();
```

Key: `$"{artist}|{title}"` (lowercased, trimmed)
Value: Spotify image URL string

### Lookup logic

In `SearchTrackAsync`, before calling `GetTokenAsync` or `DoSearchAsync`:
1. Check `_urlCache` for the key
2. On hit: log the cache hit, return the cached URL directly
3. On miss: proceed with normal search; on success, store the URL in `_urlCache` before returning

### Scope

- In-memory only — resets on app restart
- No size limit (unbounded dictionary of URL strings — negligible memory even after weeks of runtime)
- Cache is not invalidated or pruned during a session

### Files changed

| File | Change |
|------|--------|
| `SpotifyClient.cs` | Add `_urlCache` dictionary; check cache at top of `SearchTrackAsync`; populate on successful search |

---

## 3. Window Position Memory

### Purpose

Restore the main window to its last position and size when reopened, rather than re-centering every time.

### Config fields added to `AppConfig`

```csharp
public int WindowX { get; set; } = -1;
public int WindowY { get; set; } = -1;
public int WindowWidth { get; set; } = 280;
```

`-1` for X/Y signals "not set" — fall back to `FormStartPosition.CenterScreen`.

### Save

In `MainForm.OnFormClosing` (before cancelling or quitting): capture `Location.X`, `Location.Y`, `ClientSize.Width` and write to `_getConfig()`, then call `_config.Save()`.

### Restore

In `MainForm` constructor: if `WindowX >= 0`, set `StartPosition = FormStartPosition.Manual` and `Location = new Point(WindowX, WindowY)`. Always call `EnforceSquareCover()` after to ensure the height is correct.

### Off-screen guard

Before restoring, verify the saved position is on a visible screen using `Screen.AllScreens`. If none of the screens contain the saved point, fall back to `CenterScreen`.

### Files changed

| File | Change |
|------|--------|
| `AppConfig.cs` | Add `WindowX`, `WindowY`, `WindowWidth` properties |
| `MainForm.cs` | Save position on close; restore position on open with off-screen guard |

---

## Out of scope

- Persistent cache (across restarts)
- Per-track manual cover overrides
- Track history view
- Auto-start on Windows login
- Balloon notifications
