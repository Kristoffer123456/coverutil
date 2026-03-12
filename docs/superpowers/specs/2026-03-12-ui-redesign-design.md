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
- Accent: `#6a9e98` (dusty teal) — active tab indicator, status OK text, focused inputs
- Error text: `#c0392b`
- Font: system default (Segoe UI on Windows)

### Layout

```
┌─────────────────────────────┐
│                             │
│        [tab content]        │  ← fills all space above status bar
│                             │
├─────────────────────────────┤
│  Artist — Title      status │  ← status bar, always visible (~22px)
├─────────────────────────────┤
│  Cover  │ Settings │  Log   │  ← tab strip (~32px)
└─────────────────────────────┘
```

### Tab strip

Implemented as a plain `Panel` (`_tabStrip`, height 32px, `DockStyle.Bottom`) containing three `Label` controls laid out to fill equal thirds of the strip width. A 2px accent-color `Panel` (`_tabIndicator`) is positioned above the active label and repositioned on tab switch. Active tab label uses accent color text; inactive labels use `#333333`.

The three content panels (`_coverPanel`, `_settingsPanel`, `_logPanel`) share the same `Bounds` (the space above the status bar). Only the active panel has `Visible = true`; the others are `false`.

`SwitchToTab` must be called on the UI thread. If called from a background thread it must `Invoke`:

```csharp
public void SwitchToTab(MainTab tab)
{
    if (InvokeRequired) { Invoke(() => SwitchToTab(tab)); return; }
    // ... switch logic
}

public enum MainTab { Cover, Settings, Log }
```

### Status bar

A `Panel` (`_statusBar`, ~22px, `DockStyle.Bottom`, placed above `_tabStrip`) containing two `Label` controls:
- Left (`_trackLabel`): `Artist — Title` or `No track`, color `#cccccc`
- Right (`_statusLabel`): status text — accent color for `OK`, `#888888` for idle/watching, `#c0392b` for errors

The existing `UpdateTrack(string? artist, string? title)` and `UpdateStatus(string status)` public methods are preserved with identical signatures so all `TrayApp` call sites are unchanged.

### EnforceSquareCover

Updated to use both bottom panel heights:

```csharp
private void EnforceSquareCover()
{
    if (WindowState != FormWindowState.Normal) return;
    int bottomHeight = _statusBar.Height + _tabStrip.Height;
    ClientSize = new Size(ClientSize.Width, ClientSize.Width + bottomHeight);
    MinimumSize = new Size(
        160 + SystemInformation.BorderSize.Width * 2,
        160 + bottomHeight + SystemInformation.CaptionHeight + SystemInformation.BorderSize.Height * 2);
}
```

### Navigation

The old "Settings" and "View Log" buttons that lived in the bottom strip of the current `MainForm` are **removed**. Navigation is provided entirely by the tab strip. The Cover panel contains no navigation buttons.

### Cover panel

`PictureBox` fills `_coverPanel` exactly. The `OnLayout` override sizes it to `ClientSize.Width × ClientSize.Width` (a square — equivalent to `ClientSize.Width × (ClientSize.Height - _statusBar.Height - _tabStrip.Height)` since `EnforceSquareCover` makes those equal):

```csharp
protected override void OnLayout(LayoutEventArgs e)
{
    base.OnLayout(e);
    if (_coverBox == null) return;
    _coverBox.SetBounds(0, 0, ClientSize.Width, ClientSize.Width);
}
```

The existing `UpdateCover(string? imagePath)` public method is preserved.

### Settings panel

The `_settingsPanel` contains the same fields as the current `SettingsForm`: Client ID, Client Secret (masked), now_playing path (+ Browse), output path (+ Browse), default cover path (+ Browse), Close to tray checkbox, Save button, status label.

**Constructor signature:** `MainForm` drops `Action openSettings` and `Action viewLog`, keeps `Func<AppConfig> getConfig`, and adds `Action onSaved`:

```csharp
public MainForm(Func<AppConfig> getConfig, Action onSaved, Action quit)
```

`Func<AppConfig>` is retained (not replaced with a snapshot `AppConfig`) so that `OnFormClosing` and other live reads always see the current config (e.g. `CloseToTray` after a settings save within the same session).

The Save button in the Settings panel executes in this exact order:
1. Validates required fields (Client ID, Client Secret); aborts with error label if invalid
2. Mutates the config object returned by `_getConfig()` (writes TextBox/CheckBox values into its properties)
3. Calls `_getConfig().Save()` — serialises to disk
4. Calls `_onSaved()` — causes TrayApp to execute `_config = AppConfig.Load(); StartWatcher()`; after this returns, `_getConfig()` now returns the newly loaded object
5. Re-populates all Settings panel fields from `_getConfig()` — uses the post-reload object, not the pre-save snapshot

Fields are stored as local UI state (TextBox text, CheckBox.Checked) at all times. There is no held reference to the AppConfig object between steps — all reads go through `_getConfig()`. This means a second Save click is always safe.

TrayApp's `OpenMainWindow()` is updated to use the new 3-argument constructor:

```csharp
_mainForm = new MainForm(
    () => _config,
    () => { _config = AppConfig.Load(); StartWatcher(); },
    Quit);
```

`SettingsForm.cs` and `LogViewerForm.cs` are **deleted entirely** — they are no longer referenced anywhere in the codebase.

### Log panel

The `_logPanel` contains a dark `RichTextBox` (Consolas 9pt, background `#121212`, foreground `#dcdcdc`, no border, vertical scrollbar, no word wrap) and two buttons: **Refresh** and **Open full log** (opens `Logger.LogPath` in Notepad).

Log loading reads `Logger.AppLogPath` from disk on demand — identical to the current `LogViewerForm.LoadLog()` approach. `TrayApp` does not push entries to `MainForm`. The log panel calls `LoadLog()` automatically when the Log tab is activated (in the tab-switch handler).

`LogViewerForm.cs` is deleted.

### Tray menu changes

`TrayApp.OpenSettings()` is updated to call `OpenMainWindow()` then `_mainForm.SwitchToTab(MainTab.Settings)`.

`TrayApp.ViewLog()` is updated to call `OpenMainWindow()` then `_mainForm.SwitchToTab(MainTab.Log)`.

The `private LogViewerForm? _logViewerForm` field and all its usages are removed from `TrayApp`.

`CoverPreviewForm` is opened exclusively from `TrayApp.ShowCoverPreview()` with constructor arguments only (`outputPath`, `currentArtist`, `currentTitle`). It holds no reference to `MainForm` and is unaffected by this redesign.

### Files changed

| File | Change |
|------|--------|
| `MainForm.cs` | Full rewrite — dark theme, status bar, custom tab strip, three inline panels, updated constructor signature, updated `EnforceSquareCover` |
| `SettingsForm.cs` | Deleted |
| `LogViewerForm.cs` | Deleted |
| `TrayApp.cs` | Update `OpenMainWindow()` (new constructor call), `OpenSettings()`, `ViewLog()`; remove `_logViewerForm` field |

---

## 2. Session Cache

### Purpose

Avoid hitting the Spotify API twice for the same track within a session. Useful for tracks that repeat across a broadcast day and when the file watcher fires multiple times per save.

### Implementation

The cache is a field on `SpotifyClient`:

```csharp
private readonly ConcurrentDictionary<string, string> _urlCache =
    new(StringComparer.OrdinalIgnoreCase);
```

`ConcurrentDictionary` is used because `ProcessChange` runs on a `ThreadPool` thread and two rapid changes could execute concurrently (the `_processing` Interlocked flag prevents full concurrency, but cache access should be safe regardless).

Key: `$"{artist.Trim()}|{title.Trim()}"` using the **original** (pre-transform) artist name. This ensures that if the og/and→& retry succeeded last time, the next call with the same original name hits the cache directly without needing the retry.

Value: Spotify image URL string.

### Lookup logic in `SearchTrackAsync`

```
1. key = $"{artist.Trim()}|{title.Trim()}"
2. if _urlCache.TryGetValue(key, out url):
       Logger.Log($"Cache hit: {key}")
       return url   // skip token refresh and HTTP entirely
3. proceed with normal search (GetTokenAsync, DoSearchAsync, retry logic)
4. if search succeeds with imageUrl: _urlCache[key] = imageUrl
5. return imageUrl
```

"Success" means `DoSearchAsync` returned a URL and `VerifyArtist` passed. The URL is cached at that point — before `FetchAndSaveImageAsync` is called. A download failure is transient and does not invalidate a valid URL; the cached URL will be used correctly on the next attempt.

### Bounds and lifecycle

In-memory only — resets on app restart. `ConcurrentDictionary` of URL strings. At ~100 bytes per entry, a radio station playing 12 unique tracks/hour non-stop for 30 days ≈ 8,600 entries ≈ 860 KB. Explicitly acceptable for a long-running tray utility. No eviction policy.

### Files changed

| File | Change |
|------|--------|
| `SpotifyClient.cs` | Replace `Dictionary` with `ConcurrentDictionary`; add cache check at top of `SearchTrackAsync`; populate on successful return |

---

## 3. Window Position Memory

### Config fields

Added to `AppConfig`:

```csharp
public int WindowX { get; set; } = -1;
public int WindowY { get; set; } = -1;
public int WindowWidth { get; set; } = 280;
```

`WindowX == -1` is the sentinel for "never saved." `WindowHeight` is not persisted — the form derives height from width via `EnforceSquareCover()`, so width alone restores the correct size.

### Save

Position is saved in `OnFormClosing` (before the cancel/hide decision) so it is always up to date whether the form is hidden to tray or truly closed:

```csharp
private void OnFormClosing(object? sender, FormClosingEventArgs e)
{
    if (e.CloseReason == CloseReason.UserClosing && WindowState == FormWindowState.Normal)
    {
        var cfg = _getConfig();
        cfg.WindowX = Location.X;
        cfg.WindowY = Location.Y;
        cfg.WindowWidth = ClientSize.Width;
        cfg.Save();
    }

    if (e.CloseReason != CloseReason.UserClosing) return;
    if (_getConfig().CloseToTray) { e.Cancel = true; Hide(); }
    else { _quit(); }
}
```

Saving on hide ensures the last-used position is remembered even when the form is closed via the tray "Quit" action (which calls `Application.Exit()` and fires `CloseReason.ApplicationExitCall`).

### Restore

In the `MainForm` constructor, after setting `ClientSize`:

```csharp
var cfg = getConfig();
if (cfg.WindowX >= 0 &&
    Screen.AllScreens.Any(s => s.Bounds.Contains(new Point(cfg.WindowX, cfg.WindowY))))
{
    StartPosition = FormStartPosition.Manual;
    Location = new Point(cfg.WindowX, cfg.WindowY);
    ClientSize = new Size(cfg.WindowWidth, cfg.WindowWidth + 112); // placeholder height
}
else
{
    StartPosition = FormStartPosition.CenterScreen;
}
```

The off-screen guard checks that `(WindowX, WindowY)` lies within the `Bounds` of at least one screen in `Screen.AllScreens`. After panels are added and `EnforceSquareCover()` is called (via `ResizeEnd` or `Shown`), the height is corrected to use actual panel heights.

`MinimumSize` is set inside `EnforceSquareCover()` (which is called after layout), so it is applied after `Width` is restored — no ordering conflict.

### Files changed

| File | Change |
|------|--------|
| `AppConfig.cs` | Add `WindowX`, `WindowY`, `WindowWidth` |
| `MainForm.cs` | Save in `OnFormClosing`; restore in constructor with sentinel + `Screen.AllScreens` bounds guard |

---

## Out of scope

- Persistent cache (across restarts)
- Per-track manual cover overrides
- Track history view
- Auto-start on Windows login
- Balloon notifications
