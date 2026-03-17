# New Features Design — coverutil

**Date:** 2026-03-17
**Status:** Approved

## Overview

Three new features added to coverutil: Windows auto-start toggle, configurable output image size, and prioritised multi-source now-playing support. All changes are additive and backward-compatible with existing configs.

---

## Feature 1: Auto-start with Windows

### Goal

Allow the user to configure coverutil to launch automatically when Windows starts. Toggled via a checkbox in the Settings panel.

### Implementation

**New file: `WindowsAutoStart.cs`**

An `internal static` helper class isolating all registry logic:

```csharp
internal static class WindowsAutoStart
{
    private const string RegKey   = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "coverutil";

    internal static bool IsEnabled();   // reads registry, returns true if key exists
    internal static void Enable(string exePath);  // writes key
    internal static void Disable();     // deletes key
}
```

Uses `Microsoft.Win32.Registry` (included in `net8.0-windows`, no new package dependency). Operates on `HKEY_CURRENT_USER` — no administrator rights required.

**`AppConfig` change**

```csharp
public bool StartWithWindows { get; set; } = false;
```

This field is persisted in `config.json` and reflects the user's *intent*. The Settings panel reads the *actual registry state* via `WindowsAutoStart.IsEnabled()` when it loads, not the config value, so the checkbox is always accurate even if the exe path changed. Note: `StartWithWindows` is write-only from the UI's perspective — it is set on Save but never read back to populate the checkbox. This is intentional; the registry is the single source of truth.

**Settings panel change**

Add a **"Start with Windows"** checkbox row. On Save:
- If checked → `WindowsAutoStart.Enable(Process.GetCurrentProcess().MainModule!.FileName)`
- If unchecked → `WindowsAutoStart.Disable()`
- `cfg.StartWithWindows` is set to match and saved to config

**Error handling**

`Enable`/`Disable` can throw if the user has unusual registry permissions (rare). Catch and display via the existing `_settingsStatus` label.

---

## Feature 2: Configurable Output Size

### Goal

Allow the user to set the output JPEG size in pixels. Output is always square. Default is 640.

### Implementation

**`AppConfig` change**

```csharp
public int OutputSize { get; set; } = 640;
```

**`ImageHelper` change**

Add an optional `size` parameter:

```csharp
public static void ResizeAndSaveAsJpeg(byte[] imageBytes, string outputPath, int size = 640)
```

Replace the `private const int TargetSize = 640` constant with the `size` parameter throughout the method body. Existing calls without the parameter continue to work unchanged.

**`TrayApp` change**

The call to `_spotify.FetchAndSaveImageAsync(imageUrl, _config.OutputPath)` passes the size indirectly: `FetchAndSaveImageAsync` is updated to accept `int size` and passes it to `ImageHelper.ResizeAndSaveAsJpeg`.

Updated `SpotifyClient.FetchAndSaveImageAsync` signature:

```csharp
public async Task FetchAndSaveImageAsync(string imageUrl, string outputPath, int size = 640)
```

`TrayApp` passes `_config.OutputSize`.

**Settings panel change**

Add an **"Output size (px)"** text field with placeholder text `e.g. 240, 500, 640`. Validation on Save: must parse as an integer between 50 and 4000. If invalid, show error via `_settingsStatus` and do not save.

**`ImageHelper` doc comment**

Update the XML doc comment from `"Resize image bytes to 640×640"` to `"Resize image bytes to size×size"` to remain accurate for non-default sizes.

**`SpotifyClient` log string**

`FetchAndSaveImageAsync` currently logs `"resizing to 640×640 JPEG"`. Update to `$"resizing to {size}×{size} JPEG"` to keep the log accurate at non-default sizes.

**`ImageHelperTests` change**

All calls to `ResizeAndSaveAsJpeg` in tests explicitly pass `640` as the size argument to remain self-documenting. No logic changes.

---

## Feature 3: Prioritised Multi-Source

### Goal

Watch up to two `now_playing` files. Source 1 (primary) takes priority: if it has content, it is used. Source 2 (fallback) is only used when Source 1 is empty. Both sources use the same strict Spotify artist verification as today.

### Implementation

**`AppConfig` change**

```csharp
public List<string> NowPlayingSources { get; set; } = new();

// Legacy field — kept for migration only, not shown in UI
public string NowPlayingPath { get; set; } = "";
```

**Migration in `AppConfig.Load()`**

After deserialisation, if `NowPlayingSources` is empty and `NowPlayingPath` is non-empty, automatically migrate:

```csharp
if (result.NowPlayingSources.Count == 0 && !string.IsNullOrWhiteSpace(result.NowPlayingPath))
{
    result.NowPlayingSources.Add(result.NowPlayingPath);
    result.NowPlayingPath = "";
}
```

The migrated value is saved back on the next `Save()` call, cleanly removing the legacy field.

**`TrayApp` change**

Replace the single `FileSystemWatcher? _watcher` with `List<FileSystemWatcher> _watchers = new()`.

`StartWatcher()` disposes and clears `_watchers`, then iterates `_config.NowPlayingSources`. For each path: if the directory does not exist or the path is blank, log a warning and skip that source (do not abort). Creates one `FileSystemWatcher` per valid source. All watchers share the same `OnFileChanged` handler. This means Source 2 being temporarily absent is a normal operating condition — it is silently skipped rather than causing an error.

`DoProcessChange()` changes its file-reading logic:

```csharp
// Read sources in priority order — use first non-empty
string content = "";
foreach (var sourcePath in _config.NowPlayingSources)
{
    try { content = File.ReadAllText(sourcePath).Trim(); }
    catch { continue; }
    if (!string.IsNullOrEmpty(content)) break;
}
```

The rest of `DoProcessChange` is unchanged — it processes `content` exactly as before.

`Quit()` and `Dispose()` iterate `_watchers` to dispose all.

**Settings panel change**

Replace the single "Now playing file" row with two rows:
- **"Source 1 (primary)"** — text field + Browse button (required for watcher to start)
- **"Source 2 (fallback)"** — text field + Browse button (optional, leave blank to disable)

On Save, `NowPlayingSources` is built from these two fields, omitting blank entries.

On load (both `BuildSettingsPanel` initial population and `SwitchToTab` repopulation on tab switch):
```csharp
_source1Box.Text = sources.ElementAtOrDefault(0) ?? "";
_source2Box.Text = sources.ElementAtOrDefault(1) ?? "";
```
Both locations must be updated — `SwitchToTab` currently repopulates `_nowPlayingBox.Text = cfg.NowPlayingPath` and must be changed to populate the two new source boxes instead.

**Tray menu — "Open now_playing folder"**

`TrayApp.BuildTrayIcon()` currently binds this item to `OpenFolder(_config.NowPlayingPath)`. After migration `NowPlayingPath` is always empty. Update the binding to use `_config.NowPlayingSources.FirstOrDefault()` (opens the folder of Source 1, or does nothing if no sources are configured).

---

## Files Changed

| Action | File | Change |
|---|---|---|
| Create | `WindowsAutoStart.cs` | New internal helper for registry auto-start |
| Modify | `AppConfig.cs` | Add `StartWithWindows`, `OutputSize`, `NowPlayingSources`; migrate `NowPlayingPath` |
| Modify | `ImageHelper.cs` | Add `size` parameter to `ResizeAndSaveAsJpeg` |
| Modify | `SpotifyClient.cs` | Add `size` parameter to `FetchAndSaveImageAsync` |
| Modify | `TrayApp.cs` | Multi-watcher, priority source reading, pass `OutputSize`, fix tray menu "Open now_playing folder" |
| Modify | `MainForm.cs` | Settings panel: auto-start checkbox, output size field, two source fields |
| Modify | `coverutil.Tests/ImageHelperTests.cs` | Pass explicit `640` to updated method signature |

---

## Out of Scope

- More than 2 sources (UI would become cluttered; easy to extend later by changing the list approach)
- Per-source output paths (single output keeps OBS setup simple)
- Auto-start for other users on the machine (HKCU scope is correct for a tray app)
- JPEG quality as a setting (separate concern, not requested)
