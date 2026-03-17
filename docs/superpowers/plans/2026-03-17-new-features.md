# New Features Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add auto-start with Windows, configurable output image size, and prioritised multi-source now-playing support to coverutil.

**Architecture:** Six production files are touched. `AppConfig` gains three new fields and a migration path from the legacy `NowPlayingPath`. `WindowsAutoStart` is a new isolated static class for registry logic. `ImageHelper` and `SpotifyClient` get an optional `size` parameter threaded through. `TrayApp` replaces its single watcher with a list and reads sources in priority order. `MainForm` gains three new settings controls.

**Tech Stack:** .NET 8 Windows Forms, `Microsoft.Win32.Registry` (in-box on net8.0-windows), xUnit 2.x

---

## File Map

| Action | File | Change |
|---|---|---|
| Modify | `AppConfig.cs` | Add `StartWithWindows`, `OutputSize`, `NowPlayingSources`; auto-migrate `NowPlayingPath` |
| Create | `WindowsAutoStart.cs` | Internal registry helper (Enable/Disable/IsEnabled) |
| Modify | `ImageHelper.cs` | Add optional `size` parameter to `ResizeAndSaveAsJpeg` |
| Modify | `SpotifyClient.cs` | Add optional `size` parameter to `FetchAndSaveImageAsync` |
| Modify | `TrayApp.cs` | Multi-watcher list, priority-source reading, output size wiring, tray menu fix |
| Modify | `MainForm.cs` | Settings panel: source 1/2 fields, output size field, auto-start checkbox |
| Modify | `coverutil.Tests/AppConfigTests.cs` | Add tests for new fields, defaults, migration |
| Modify | `coverutil.Tests/ImageHelperTests.cs` | Add explicit size arg to existing calls; add custom-size test |

---

## Chunk 1: AppConfig + WindowsAutoStart

### Task 1: Update `AppConfig`

**Files:**
- Modify: `coverutil/AppConfig.cs`

- [ ] **Step 1: Add new fields and migration to `AppConfig.cs`**

Replace the entire file with:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace coverutil;

public class AppConfig
{
    public string SpotifyClientId     { get; set; } = "";
    public string SpotifyClientSecret { get; set; } = "";
    public string OutputPath          { get; set; } = "";
    public string DefaultCoverPath    { get; set; } = "";
    public bool   CloseToTray         { get; set; } = true;
    public int    WindowX             { get; set; } = -1;
    public int    WindowY             { get; set; } = -1;
    public int    WindowWidth         { get; set; } = 280;

    // New fields
    public List<string> NowPlayingSources { get; set; } = new();
    public int  OutputSize        { get; set; } = 640;
    public bool StartWithWindows  { get; set; } = false;

    // Legacy — kept for migration only; not used in new code
    public string NowPlayingPath { get; set; } = "";

    internal static string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "coverutil", "config.json");

    public static AppConfig Load()
    {
        AppConfig result;
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                result = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            }
            else
            {
                result = new AppConfig();
            }
        }
        catch
        {
            result = new AppConfig();
        }

        // Migrate legacy NowPlayingPath → NowPlayingSources
        if (result.NowPlayingSources.Count == 0 && !string.IsNullOrWhiteSpace(result.NowPlayingPath))
        {
            result.NowPlayingSources.Add(result.NowPlayingPath);
            result.NowPlayingPath = "";
        }

        return result;
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(ConfigPath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json);
    }
}
```

- [ ] **Step 2: Build to verify**

```bash
cd c:/Users/Kristoffer/dev/coverutil
dotnet build
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add coverutil/AppConfig.cs
git commit -m "feat: add OutputSize, StartWithWindows, NowPlayingSources to AppConfig"
```

---

### Task 2: Create `WindowsAutoStart.cs`

**Files:**
- Create: `coverutil/WindowsAutoStart.cs`

- [ ] **Step 1: Create `WindowsAutoStart.cs`**

```csharp
using System;
using Microsoft.Win32;

namespace coverutil;

internal static class WindowsAutoStart
{
    private const string RegKey    = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "coverutil";

    /// <summary>Returns true if the coverutil autostart registry entry exists.</summary>
    internal static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegKey, writable: false);
        return key?.GetValue(ValueName) != null;
    }

    /// <summary>Writes the autostart registry entry pointing to exePath.</summary>
    internal static void Enable(string exePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegKey, writable: true)
            ?? throw new Exception($"Cannot open registry key: {RegKey}");
        key.SetValue(ValueName, $"\"{exePath}\"");
    }

    /// <summary>Removes the autostart registry entry. No-op if not present.</summary>
    internal static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add coverutil/WindowsAutoStart.cs
git commit -m "feat: add WindowsAutoStart helper for registry auto-start"
```

---

### Task 3: Update `AppConfigTests`

**Files:**
- Modify: `coverutil.Tests/AppConfigTests.cs`

- [ ] **Step 1: Update `AppConfigTests.cs`**

Replace the entire file with:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace coverutil.Tests;

[Collection("SharedState")]
public class AppConfigTests : IDisposable
{
    private readonly string _originalPath = AppConfig.ConfigPath;

    public AppConfigTests()
    {
        AppConfig.ConfigPath = Path.Combine(
            Path.GetTempPath(), $"coverutil-test-{Guid.NewGuid()}.json");
    }

    public void Dispose()
    {
        if (File.Exists(AppConfig.ConfigPath))
            File.Delete(AppConfig.ConfigPath);
        AppConfig.ConfigPath = _originalPath;
    }

    [Fact]
    public void SaveAndLoad_RoundTrip_AllFieldsMatch()
    {
        var cfg = new AppConfig
        {
            SpotifyClientId     = "id123",
            SpotifyClientSecret = "secret456",
            NowPlayingSources   = new List<string> { @"C:\src1.txt", @"C:\src2.txt" },
            OutputPath          = @"C:\out.jpg",
            DefaultCoverPath    = @"C:\def.jpg",
            CloseToTray         = false,
            WindowX             = 100,
            WindowY             = 200,
            WindowWidth         = 350,
            OutputSize          = 400,
            StartWithWindows    = true
        };
        cfg.Save();

        var loaded = AppConfig.Load();
        Assert.Equal(cfg.SpotifyClientId,     loaded.SpotifyClientId);
        Assert.Equal(cfg.SpotifyClientSecret, loaded.SpotifyClientSecret);
        Assert.Equal(cfg.NowPlayingSources,   loaded.NowPlayingSources);
        Assert.Equal(cfg.OutputPath,          loaded.OutputPath);
        Assert.Equal(cfg.DefaultCoverPath,    loaded.DefaultCoverPath);
        Assert.Equal(cfg.CloseToTray,         loaded.CloseToTray);
        Assert.Equal(cfg.WindowX,             loaded.WindowX);
        Assert.Equal(cfg.WindowY,             loaded.WindowY);
        Assert.Equal(cfg.WindowWidth,         loaded.WindowWidth);
        Assert.Equal(cfg.OutputSize,          loaded.OutputSize);
        Assert.Equal(cfg.StartWithWindows,    loaded.StartWithWindows);
    }

    [Fact]
    public void Load_FileMissing_ReturnsDefaultWithoutThrowing()
    {
        var result = AppConfig.Load();
        Assert.NotNull(result);
    }

    [Fact]
    public void Load_InvalidJson_ReturnsDefaultWithoutThrowing()
    {
        File.WriteAllText(AppConfig.ConfigPath, "not json {{{{");
        var result = AppConfig.Load();
        Assert.NotNull(result);
    }

    [Fact]
    public void Load_LegacyNowPlayingPath_MigratedToSources()
    {
        File.WriteAllText(AppConfig.ConfigPath,
            """{"NowPlayingPath": "C:\\np.txt", "NowPlayingSources": []}""");
        var cfg = AppConfig.Load();
        Assert.Single(cfg.NowPlayingSources);
        Assert.Equal(@"C:\np.txt", cfg.NowPlayingSources[0]);
        Assert.Empty(cfg.NowPlayingPath);
    }

    [Fact]
    public void Default_CloseToTray_IsTrue() =>
        Assert.True(new AppConfig().CloseToTray);

    [Fact]
    public void Default_WindowXY_IsMinusOne()
    {
        var cfg = new AppConfig();
        Assert.Equal(-1, cfg.WindowX);
        Assert.Equal(-1, cfg.WindowY);
    }

    [Fact]
    public void Default_WindowWidth_Is280() =>
        Assert.Equal(280, new AppConfig().WindowWidth);

    [Fact]
    public void Default_OutputSize_Is640() =>
        Assert.Equal(640, new AppConfig().OutputSize);

    [Fact]
    public void Default_StartWithWindows_IsFalse() =>
        Assert.False(new AppConfig().StartWithWindows);
}
```

- [ ] **Step 2: Run tests**

```bash
cd c:/Users/Kristoffer/dev/coverutil
dotnet test coverutil.Tests/ --filter "FullyQualifiedName~AppConfigTests"
```

Expected: `9 passed, 0 failed`

- [ ] **Step 3: Commit**

```bash
git add coverutil.Tests/AppConfigTests.cs
git commit -m "test: update AppConfigTests for new fields and migration"
```

---

## Chunk 2: ImageHelper + SpotifyClient + TrayApp

### Task 4: Update `ImageHelper`

**Files:**
- Modify: `coverutil/ImageHelper.cs`

- [ ] **Step 1: Update `ImageHelper.cs`**

Replace the entire file with:

```csharp
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace coverutil;

public static class ImageHelper
{
    /// <summary>
    /// Resize image bytes to size×size and save as JPEG at the given path.
    /// Non-square inputs are letterboxed/pillarboxed with black bars.
    /// </summary>
    public static void ResizeAndSaveAsJpeg(byte[] imageBytes, string outputPath, int size = 640)
    {
        using var ms     = new MemoryStream(imageBytes);
        using var source = Image.FromStream(ms);
        using var dest   = new Bitmap(size, size);
        using var g      = Graphics.FromImage(dest);

        g.InterpolationMode    = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode        = SmoothingMode.HighQuality;
        g.PixelOffsetMode      = PixelOffsetMode.HighQuality;
        g.CompositingQuality   = CompositingQuality.HighQuality;

        float srcRatio = (float)source.Width / source.Height;
        Rectangle destRect;
        if (srcRatio > 1f)
        {
            int h = (int)(size / srcRatio);
            destRect = new Rectangle(0, (size - h) / 2, size, h);
        }
        else if (srcRatio < 1f)
        {
            int w = (int)(size * srcRatio);
            destRect = new Rectangle((size - w) / 2, 0, w, size);
        }
        else
        {
            destRect = new Rectangle(0, 0, size, size);
        }

        g.Clear(Color.Black);
        g.DrawImage(source, destRect);

        var encoder = ImageCodecInfo.GetImageEncoders()
            .First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        var encoderParams = new EncoderParameters(1);
        encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, 95L);

        dest.Save(outputPath, encoder, encoderParams);
    }
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add coverutil/ImageHelper.cs
git commit -m "feat: add configurable size parameter to ImageHelper.ResizeAndSaveAsJpeg"
```

---

### Task 5: Update `SpotifyClient.FetchAndSaveImageAsync`

**Files:**
- Modify: `coverutil/SpotifyClient.cs`

- [ ] **Step 1: Update `FetchAndSaveImageAsync`**

Find the method (near the bottom of `SpotifyClient.cs`):

```csharp
public async Task FetchAndSaveImageAsync(string imageUrl, string outputPath)
{
    Logger.Log($"Downloading image: {imageUrl}");
    var bytes = await _http.GetByteArrayAsync(imageUrl);
    Logger.Log($"Downloaded {bytes.Length} bytes — resizing to 640×640 JPEG");
    ImageHelper.ResizeAndSaveAsJpeg(bytes, outputPath);
}
```

Replace with:

```csharp
public async Task FetchAndSaveImageAsync(string imageUrl, string outputPath, int size = 640)
{
    Logger.Log($"Downloading image: {imageUrl}");
    var bytes = await _http.GetByteArrayAsync(imageUrl);
    Logger.Log($"Downloaded {bytes.Length} bytes — resizing to {size}×{size} JPEG");
    ImageHelper.ResizeAndSaveAsJpeg(bytes, outputPath, size);
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add coverutil/SpotifyClient.cs
git commit -m "feat: thread size parameter through SpotifyClient.FetchAndSaveImageAsync"
```

---

### Task 6: Update `TrayApp`

**Files:**
- Modify: `coverutil/TrayApp.cs`

- [ ] **Step 1: Update using directives at the top of `TrayApp.cs`**

Current top:
```csharp
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
```

Replace with:
```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
```

- [ ] **Step 2: Replace `_watcher` field with `_watchers` list**

Find:
```csharp
    private FileSystemWatcher? _watcher;
```

Replace with:
```csharp
    private readonly List<FileSystemWatcher> _watchers = new();
```

- [ ] **Step 3: Fix tray menu "Open now_playing folder" binding**

Find in `BuildTrayIcon()` (around line 65):
```csharp
        openNowPlayingItem.Click += (_, _) => OpenFolder(_config.NowPlayingPath);
```

Replace with:
```csharp
        openNowPlayingItem.Click += (_, _) => OpenFolder(_config.NowPlayingSources.FirstOrDefault());
```

- [ ] **Step 4: Rewrite `StartWatcher()`**

Find and replace the entire `StartWatcher()` method:

```csharp
    public void StartWatcher()
    {
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();

        if (string.IsNullOrWhiteSpace(_config.SpotifyClientId) ||
            string.IsNullOrWhiteSpace(_config.SpotifyClientSecret) ||
            _config.NowPlayingSources.Count == 0 ||
            string.IsNullOrWhiteSpace(_config.OutputPath))
        {
            SetStatus("Config incomplete — open Settings");
            Logger.LogApp("Watcher not started: config incomplete");
            return;
        }

        _spotify.Configure(_config.SpotifyClientId, _config.SpotifyClientSecret);

        bool anyStarted = false;
        foreach (var sourcePath in _config.NowPlayingSources)
        {
            if (string.IsNullOrWhiteSpace(sourcePath)) continue;

            var dir  = Path.GetDirectoryName(sourcePath);
            var file = Path.GetFileName(sourcePath);

            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(file))
            {
                Logger.LogApp($"Skipping invalid source path: {sourcePath}");
                continue;
            }

            if (!Directory.Exists(dir))
            {
                Logger.LogApp($"Skipping source — directory not found: {dir}");
                continue;
            }

            try
            {
                var watcher = new FileSystemWatcher(dir, file)
                {
                    NotifyFilter = NotifyFilters.LastWrite,
                    EnableRaisingEvents = true
                };
                watcher.Changed += OnFileChanged;
                watcher.Error   += (_, e) =>
                {
                    var msg = $"Watcher error: {e.GetException().Message}";
                    Logger.LogApp(msg);
                    SetStatus(msg);
                };
                _watchers.Add(watcher);
                Logger.LogApp($"Watching: {sourcePath}");
                anyStarted = true;
            }
            catch (Exception ex)
            {
                Logger.LogApp($"Watcher setup error for {sourcePath}: {ex.Message}");
            }
        }

        if (anyStarted)
        {
            SetStatus("Watching...");
            System.Threading.Tasks.Task.Run(ProcessChange);
        }
        else
        {
            SetStatus("No valid sources configured");
            Logger.LogApp("Watcher not started: no valid sources");
        }
    }
```

- [ ] **Step 5: Rewrite file-reading in `DoProcessChange()`**

Find the file-reading block at the top of `DoProcessChange()`:

```csharp
        string content;
        try
        {
            content = File.ReadAllText(_config.NowPlayingPath).Trim();
        }
        catch (Exception ex)
        {
            var msg = $"Read error: {ex.Message}";
            Logger.LogApp(msg);
            SetStatus(msg);
            return;
        }
```

Replace with:

```csharp
        // Read sources in priority order — use first non-empty
        string content = "";
        foreach (var sourcePath in _config.NowPlayingSources)
        {
            if (string.IsNullOrWhiteSpace(sourcePath)) continue;
            try
            {
                var read = File.ReadAllText(sourcePath).Trim();
                if (!string.IsNullOrEmpty(read)) { content = read; break; }
            }
            catch { continue; }
        }
```

- [ ] **Step 6: Pass `OutputSize` to `FetchAndSaveImageAsync`**

Find in `DoProcessChange()`:
```csharp
            _spotify.FetchAndSaveImageAsync(imageUrl, _config.OutputPath).GetAwaiter().GetResult();
```

Replace with:
```csharp
            _spotify.FetchAndSaveImageAsync(imageUrl, _config.OutputPath, _config.OutputSize).GetAwaiter().GetResult();
```

- [ ] **Step 7: Update `Quit()` to dispose all watchers**

Find:
```csharp
    public void Quit()
    {
        Logger.LogApp("coverutil exiting");
        _watcher?.Dispose();
        _debounceTimer?.Dispose();
```

Replace with:
```csharp
    public void Quit()
    {
        Logger.LogApp("coverutil exiting");
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
        _debounceTimer?.Dispose();
```

- [ ] **Step 8: Update `Dispose()` to dispose all watchers**

Find:
```csharp
        if (disposing)
        {
            _watcher?.Dispose();
            _debounceTimer?.Dispose();
```

Replace with:
```csharp
        if (disposing)
        {
            foreach (var w in _watchers) w.Dispose();
            _debounceTimer?.Dispose();
```

- [ ] **Step 9: Build to verify**

```bash
dotnet build
```

Expected: `Build succeeded.`

- [ ] **Step 10: Commit**

```bash
git add coverutil/TrayApp.cs
git commit -m "feat: multi-source watcher, output size wiring, tray menu fix in TrayApp"
```

---

### Task 7: Update `ImageHelperTests`

**Files:**
- Modify: `coverutil.Tests/ImageHelperTests.cs`

- [ ] **Step 1: Update `ImageHelperTests.cs`**

Replace the entire file with:

```csharp
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Xunit;

namespace coverutil.Tests;

public class ImageHelperTests : IDisposable
{
    private readonly string _outputPath = Path.GetTempFileName();

    public void Dispose()
    {
        if (File.Exists(_outputPath)) File.Delete(_outputPath);
    }

    private static byte[] MakePng(int width, int height, Color color)
    {
        using var bmp = new Bitmap(width, height);
        using var g   = Graphics.FromImage(bmp);
        g.Clear(color);
        using var ms  = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    [Fact]
    public void Square_Input_OutputIs640x640()
    {
        ImageHelper.ResizeAndSaveAsJpeg(MakePng(100, 100, Color.Red), _outputPath, 640);
        using var img = Image.FromFile(_outputPath);
        Assert.Equal(640, img.Width);
        Assert.Equal(640, img.Height);
    }

    [Fact]
    public void Landscape_Input_HasLetterboxBars()
    {
        ImageHelper.ResizeAndSaveAsJpeg(MakePng(200, 100, Color.Red), _outputPath, 640);
        using var bmp = new Bitmap(_outputPath);
        Assert.Equal(640, bmp.Width);
        Assert.Equal(640, bmp.Height);
        var pixel = bmp.GetPixel(320, 0);
        Assert.True(pixel.R < 10 && pixel.G < 10 && pixel.B < 10,
            $"Expected black letterbox bar at (320,0), got R={pixel.R} G={pixel.G} B={pixel.B}");
    }

    [Fact]
    public void Portrait_Input_HasPillarboxBars()
    {
        ImageHelper.ResizeAndSaveAsJpeg(MakePng(100, 200, Color.Red), _outputPath, 640);
        using var bmp = new Bitmap(_outputPath);
        Assert.Equal(640, bmp.Width);
        Assert.Equal(640, bmp.Height);
        var pixel = bmp.GetPixel(0, 320);
        Assert.True(pixel.R < 10 && pixel.G < 10 && pixel.B < 10,
            $"Expected black pillarbox bar at (0,320), got R={pixel.R} G={pixel.G} B={pixel.B}");
    }

    [Fact]
    public void Output_IsValidJpeg()
    {
        ImageHelper.ResizeAndSaveAsJpeg(MakePng(100, 100, Color.Blue), _outputPath, 640);
        using var img = Image.FromFile(_outputPath);
        Assert.NotNull(img);
    }

    [Fact]
    public void CustomSize_OutputMatchesRequestedSize()
    {
        ImageHelper.ResizeAndSaveAsJpeg(MakePng(100, 100, Color.Green), _outputPath, 320);
        using var img = Image.FromFile(_outputPath);
        Assert.Equal(320, img.Width);
        Assert.Equal(320, img.Height);
    }
}
```

- [ ] **Step 2: Run tests**

```bash
cd c:/Users/Kristoffer/dev/coverutil
dotnet test coverutil.Tests/ --filter "Category!=Integration"
```

Expected: all tests pass (total ~37 across all test classes).

- [ ] **Step 3: Commit**

```bash
git add coverutil.Tests/ImageHelperTests.cs
git commit -m "test: update ImageHelperTests — explicit size args + custom size test"
```

---

## Chunk 3: MainForm Settings UI

### Task 8: Update `MainForm` settings panel

**Files:**
- Modify: `coverutil/MainForm.cs`

This task has many small edits. Read the file carefully before each step to confirm exact line numbers.

- [ ] **Step 1: Add new field declarations**

Find the settings field declarations block (around line 39):
```csharp
    private TextBox   _clientIdBox      = null!;
    private TextBox   _clientSecretBox  = null!;
    private TextBox   _nowPlayingBox    = null!;
    private TextBox   _outputBox        = null!;
    private TextBox   _defaultCoverBox  = null!;
    private CheckBox  _closeToTrayBox   = null!;
    private Label     _settingsStatus   = null!;
```

Replace with:
```csharp
    private TextBox   _clientIdBox          = null!;
    private TextBox   _clientSecretBox      = null!;
    private TextBox   _source1Box           = null!;
    private TextBox   _source2Box           = null!;
    private TextBox   _outputBox            = null!;
    private TextBox   _defaultCoverBox      = null!;
    private TextBox   _outputSizeBox        = null!;
    private CheckBox  _closeToTrayBox       = null!;
    private CheckBox  _startWithWindowsBox  = null!;
    private Label     _settingsStatus       = null!;
```

- [ ] **Step 2: Add `using System.Linq;` to MainForm.cs**

Find the using block at the top:
```csharp
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
```

(If `using System.Linq;` is already there, skip this step. If not, add it.)

- [ ] **Step 3: Replace the `now_playing.txt` row in `BuildSettingsPanel()`**

Find:
```csharp
        layout.Controls.Add(MakeLabel("now_playing.txt:"), 0, row);
        _nowPlayingBox = MakeTextBox(readOnly: true);
        layout.Controls.Add(_nowPlayingBox, 1, row);
        var browseNow = MakeBrowseBtn();
        browseNow.Click += BrowseNowPlaying;
        layout.Controls.Add(browseNow, 2, row);
        row++;
```

Replace with:
```csharp
        layout.Controls.Add(MakeLabel("Source 1 (primary):"), 0, row);
        _source1Box = MakeTextBox(readOnly: true);
        layout.Controls.Add(_source1Box, 1, row);
        var browseSource1 = MakeBrowseBtn();
        browseSource1.Click += BrowseSource1;
        layout.Controls.Add(browseSource1, 2, row);
        row++;

        layout.Controls.Add(MakeLabel("Source 2 (fallback):"), 0, row);
        _source2Box = MakeTextBox(readOnly: true);
        layout.Controls.Add(_source2Box, 1, row);
        var browseSource2 = MakeBrowseBtn();
        browseSource2.Click += BrowseSource2;
        layout.Controls.Add(browseSource2, 2, row);
        row++;
```

- [ ] **Step 4: Add Output size row in `BuildSettingsPanel()`**

Find the Default cover row block:
```csharp
        layout.Controls.Add(MakeLabel("Default cover:"), 0, row);
        _defaultCoverBox = MakeTextBox(readOnly: true);
        layout.Controls.Add(_defaultCoverBox, 1, row);
        var browseDefault = MakeBrowseBtn();
        browseDefault.Click += BrowseDefaultCover;
        layout.Controls.Add(browseDefault, 2, row);
        row++;
```

After it, add:
```csharp
        layout.Controls.Add(MakeLabel("Output size (px):"), 0, row);
        _outputSizeBox = MakeTextBox();
        layout.Controls.Add(_outputSizeBox, 1, row);
        layout.SetColumnSpan(_outputSizeBox, 2);
        row++;
```

- [ ] **Step 5: Add "Start with Windows" checkbox in `BuildSettingsPanel()`**

Find the `_closeToTrayBox` block:
```csharp
        _closeToTrayBox = new CheckBox
        {
            Text = "Close window to tray (uncheck to quit on close)",
            ForeColor = PrimaryText, BackColor = BgColor,
            Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(3, 6, 3, 3)
        };
        layout.Controls.Add(_closeToTrayBox, 0, row);
        layout.SetColumnSpan(_closeToTrayBox, 3);
        row++;
```

After it, add:
```csharp
        _startWithWindowsBox = new CheckBox
        {
            Text = "Start with Windows",
            ForeColor = PrimaryText, BackColor = BgColor,
            Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(3, 6, 3, 3)
        };
        layout.Controls.Add(_startWithWindowsBox, 0, row);
        layout.SetColumnSpan(_startWithWindowsBox, 3);
        row++;
```

- [ ] **Step 6: Update initial population in `BuildSettingsPanel()`**

Find:
```csharp
        var config = _getConfig();
        _clientIdBox.Text     = config.SpotifyClientId;
        _clientSecretBox.Text = config.SpotifyClientSecret;
        _nowPlayingBox.Text   = config.NowPlayingPath;
        _outputBox.Text       = config.OutputPath;
        _defaultCoverBox.Text = config.DefaultCoverPath;
        _closeToTrayBox.Checked = config.CloseToTray;
```

Replace with:
```csharp
        var config = _getConfig();
        _clientIdBox.Text          = config.SpotifyClientId;
        _clientSecretBox.Text      = config.SpotifyClientSecret;
        _source1Box.Text           = config.NowPlayingSources.ElementAtOrDefault(0) ?? "";
        _source2Box.Text           = config.NowPlayingSources.ElementAtOrDefault(1) ?? "";
        _outputBox.Text            = config.OutputPath;
        _defaultCoverBox.Text      = config.DefaultCoverPath;
        _outputSizeBox.Text        = config.OutputSize.ToString();
        _closeToTrayBox.Checked    = config.CloseToTray;
        _startWithWindowsBox.Checked = WindowsAutoStart.IsEnabled();
```

- [ ] **Step 7: Update `SwitchToTab` repopulation**

Find (in `SwitchToTab`, around line 418):
```csharp
            var cfg = _getConfig();
            _clientIdBox.Text       = cfg.SpotifyClientId;
            _clientSecretBox.Text   = cfg.SpotifyClientSecret;
            _nowPlayingBox.Text     = cfg.NowPlayingPath;
            _outputBox.Text         = cfg.OutputPath;
            _defaultCoverBox.Text   = cfg.DefaultCoverPath;
            _closeToTrayBox.Checked = cfg.CloseToTray;
            _settingsStatus.Text    = "";
```

Replace with:
```csharp
            var cfg = _getConfig();
            _clientIdBox.Text              = cfg.SpotifyClientId;
            _clientSecretBox.Text          = cfg.SpotifyClientSecret;
            _source1Box.Text               = cfg.NowPlayingSources.ElementAtOrDefault(0) ?? "";
            _source2Box.Text               = cfg.NowPlayingSources.ElementAtOrDefault(1) ?? "";
            _outputBox.Text                = cfg.OutputPath;
            _defaultCoverBox.Text          = cfg.DefaultCoverPath;
            _outputSizeBox.Text            = cfg.OutputSize.ToString();
            _closeToTrayBox.Checked        = cfg.CloseToTray;
            _startWithWindowsBox.Checked   = WindowsAutoStart.IsEnabled();
            _settingsStatus.Text           = "";
```

- [ ] **Step 8: Update `SaveSettings()`**

Find the validation block at the top of `SaveSettings()`:
```csharp
        if (string.IsNullOrWhiteSpace(_clientIdBox.Text) || string.IsNullOrWhiteSpace(_clientSecretBox.Text))
        {
            _settingsStatus.ForeColor = ErrorColor;
            _settingsStatus.Text      = "Client ID and Secret are required.";
            return;
        }
```

Replace with:
```csharp
        if (string.IsNullOrWhiteSpace(_clientIdBox.Text) || string.IsNullOrWhiteSpace(_clientSecretBox.Text))
        {
            _settingsStatus.ForeColor = ErrorColor;
            _settingsStatus.Text      = "Client ID and Secret are required.";
            return;
        }

        if (!int.TryParse(_outputSizeBox.Text.Trim(), out int outputSize) || outputSize < 50 || outputSize > 4000)
        {
            _settingsStatus.ForeColor = ErrorColor;
            _settingsStatus.Text      = "Output size must be a number between 50 and 4000.";
            return;
        }
```

Find the field-assignment block inside `SaveSettings()`:
```csharp
        var cfg = _getConfig();
        cfg.SpotifyClientId     = _clientIdBox.Text.Trim();
        cfg.SpotifyClientSecret = _clientSecretBox.Text.Trim();
        cfg.NowPlayingPath      = _nowPlayingBox.Text.Trim();
        cfg.OutputPath          = _outputBox.Text.Trim();
        cfg.DefaultCoverPath    = _defaultCoverBox.Text.Trim();
        cfg.CloseToTray         = _closeToTrayBox.Checked;
```

Replace with:
```csharp
        var cfg = _getConfig();
        cfg.SpotifyClientId     = _clientIdBox.Text.Trim();
        cfg.SpotifyClientSecret = _clientSecretBox.Text.Trim();
        var sources = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrWhiteSpace(_source1Box.Text)) sources.Add(_source1Box.Text.Trim());
        if (!string.IsNullOrWhiteSpace(_source2Box.Text)) sources.Add(_source2Box.Text.Trim());
        cfg.NowPlayingSources   = sources;
        cfg.OutputPath          = _outputBox.Text.Trim();
        cfg.DefaultCoverPath    = _defaultCoverBox.Text.Trim();
        cfg.OutputSize          = outputSize;
        cfg.CloseToTray         = _closeToTrayBox.Checked;
        cfg.StartWithWindows    = _startWithWindowsBox.Checked;
```

Find the `try { cfg.Save(); _onSaved();` block and locate the reload section after `_onSaved()`:
```csharp
            var reloaded = _getConfig();
            _clientIdBox.Text       = reloaded.SpotifyClientId;
            _clientSecretBox.Text   = reloaded.SpotifyClientSecret;
            _nowPlayingBox.Text     = reloaded.NowPlayingPath;
            _outputBox.Text         = reloaded.OutputPath;
            _defaultCoverBox.Text   = reloaded.DefaultCoverPath;
            _closeToTrayBox.Checked = reloaded.CloseToTray;
```

Replace with:
```csharp
            var reloaded = _getConfig();
            _clientIdBox.Text              = reloaded.SpotifyClientId;
            _clientSecretBox.Text          = reloaded.SpotifyClientSecret;
            _source1Box.Text               = reloaded.NowPlayingSources.ElementAtOrDefault(0) ?? "";
            _source2Box.Text               = reloaded.NowPlayingSources.ElementAtOrDefault(1) ?? "";
            _outputBox.Text                = reloaded.OutputPath;
            _defaultCoverBox.Text          = reloaded.DefaultCoverPath;
            _outputSizeBox.Text            = reloaded.OutputSize.ToString();
            _closeToTrayBox.Checked        = reloaded.CloseToTray;
            _startWithWindowsBox.Checked   = WindowsAutoStart.IsEnabled();
```

Now add the auto-start registry call. Find the `cfg.Save();` line inside SaveSettings and add the registry update right after `cfg.Save(); _onSaved();`:

Find this pattern:
```csharp
            cfg.Save();
            _onSaved();

            var reloaded = _getConfig();
```

Replace with:
```csharp
            cfg.Save();

            try
            {
                if (_startWithWindowsBox.Checked)
                    WindowsAutoStart.Enable(Process.GetCurrentProcess().MainModule!.FileName);
                else
                    WindowsAutoStart.Disable();
            }
            catch (Exception regEx)
            {
                _settingsStatus.ForeColor = ErrorColor;
                _settingsStatus.Text      = $"Auto-start registry error: {regEx.Message}";
                return;
            }

            _onSaved();

            var reloaded = _getConfig();
```

- [ ] **Step 9: Replace `BrowseNowPlaying` with `BrowseSource1` and add `BrowseSource2`**

Find and replace the entire `BrowseNowPlaying` method:
```csharp
    private void BrowseNowPlaying(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Select now_playing.txt",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*"
        };
        if (!string.IsNullOrEmpty(_nowPlayingBox.Text))
            dlg.InitialDirectory = Path.GetDirectoryName(_nowPlayingBox.Text);
        if (dlg.ShowDialog() == DialogResult.OK)
            _nowPlayingBox.Text = dlg.FileName;
    }
```

Replace with:
```csharp
    private void BrowseSource1(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Select now_playing.txt (Source 1, primary)",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*"
        };
        if (!string.IsNullOrEmpty(_source1Box.Text))
            dlg.InitialDirectory = Path.GetDirectoryName(_source1Box.Text);
        if (dlg.ShowDialog() == DialogResult.OK)
            _source1Box.Text = dlg.FileName;
    }

    private void BrowseSource2(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Select now_playing.txt (Source 2, fallback)",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*"
        };
        if (!string.IsNullOrEmpty(_source2Box.Text))
            dlg.InitialDirectory = Path.GetDirectoryName(_source2Box.Text);
        if (dlg.ShowDialog() == DialogResult.OK)
            _source2Box.Text = dlg.FileName;
    }
```

- [ ] **Step 10: Build to verify**

```bash
dotnet build
```

Expected: `Build succeeded.` with no errors.

If there are compile errors mentioning `_nowPlayingBox`, search for any remaining references and replace with `_source1Box`.

- [ ] **Step 11: Run all unit tests**

```bash
dotnet test coverutil.Tests/ --filter "Category!=Integration"
```

Expected: all tests pass.

- [ ] **Step 12: Commit**

```bash
git add coverutil/MainForm.cs
git commit -m "feat: update settings panel — source 1/2 fields, output size, auto-start checkbox"
```

---

### Task 9: Final run and push

- [ ] **Step 1: Run all unit tests**

```bash
cd c:/Users/Kristoffer/dev/coverutil
dotnet test coverutil.Tests/ --filter "Category!=Integration"
```

Expected: all tests pass, 0 failures.

- [ ] **Step 2: Push**

```bash
git push
```
