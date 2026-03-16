# Test Suite Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an xUnit test suite covering NowPlayingParser, Logger, AppConfig, ImageHelper, and SpotifyClient with both fast unit tests and optional Spotify integration tests.

**Architecture:** Six minimal production code changes expose internal seams for testing (injectable HttpClient, overridable static paths, promoted access modifiers, extracted parser class). All tests live in a single `coverutil.Tests` project; shared-static-state tests use `[Collection("SharedState")]` to prevent parallel pollution.

**Tech Stack:** .NET 8, xUnit 2.x, Xunit.SkippableFact 1.x, coverlet, System.Drawing (GDI+, Windows-only)

**Spec:** `docs/superpowers/specs/2026-03-17-test-suite-design.md`

---

## File Map

| Action | File | Responsibility |
|---|---|---|
| Modify | `coverutil.csproj` | Add `InternalsVisibleTo` for test project |
| Modify | `SpotifyClient.cs` | Add internal constructor; promote 3 private methods to internal |
| Modify | `Logger.cs` | Change `_dir` to `internal static Dir`; make `LogPath`/`AppLogPath` use `Dir` |
| Modify | `AppConfig.cs` | Change `ConfigPath` to `internal static` (non-readonly) |
| Create | `NowPlayingParser.cs` | Extracted `internal static` parser used by TrayApp |
| Modify | `TrayApp.cs` | Replace private `ParseNowPlaying` call with `NowPlayingParser.Parse` |
| Create | `coverutil.Tests/coverutil.Tests.csproj` | Test project targeting net8.0-windows |
| Create | `coverutil.Tests/Helpers/FakeHttpHandler.cs` | Queue-based fake HttpMessageHandler |
| Create | `coverutil.Tests/SharedStateCollection.cs` | xUnit collection definition for serial execution |
| Create | `coverutil.Tests/ParsingTests.cs` | Tests for NowPlayingParser |
| Create | `coverutil.Tests/LoggerTests.cs` | Tests for Logger (SharedState collection) |
| Create | `coverutil.Tests/AppConfigTests.cs` | Tests for AppConfig (SharedState collection) |
| Create | `coverutil.Tests/ImageHelperTests.cs` | Tests for ImageHelper resize/letterbox logic |
| Create | `coverutil.Tests/SpotifyClientTests.cs` | Unit + integration tests for SpotifyClient |

---

## Chunk 1: Production Code Changes and Test Project Scaffold

### Task 1: Add `InternalsVisibleTo` to `coverutil.csproj`

**Files:**
- Modify: `coverutil.csproj`

- [ ] **Step 1: Edit `coverutil.csproj`** — add the following `ItemGroup` anywhere inside `<Project>`:

```xml
<ItemGroup>
  <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleTo">
    <_Parameter1>coverutil.Tests</_Parameter1>
  </AssemblyAttribute>
</ItemGroup>
```

- [ ] **Step 2: Verify it builds**

```bash
cd coverutil
dotnet build
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add coverutil.csproj
git commit -m "chore: add InternalsVisibleTo for coverutil.Tests"
```

---

### Task 2: Extract `NowPlayingParser` and update `TrayApp`

**Files:**
- Create: `NowPlayingParser.cs`
- Modify: `TrayApp.cs`

- [ ] **Step 1: Create `NowPlayingParser.cs`**

```csharp
namespace coverutil;

internal static class NowPlayingParser
{
    internal static (string artist, string title)? Parse(string content)
    {
        int idx = content.IndexOf(" - ", StringComparison.Ordinal);
        if (idx < 0) return null;
        return (content[..idx].Trim(), content[(idx + 3)..].Trim());
    }
}
```

- [ ] **Step 2: Update `TrayApp.cs`** — replace the private `ParseNowPlaying` method and its call site.

Find the private method (near line 342 in `TrayApp.cs`):
```csharp
private static (string artist, string title)? ParseNowPlaying(string content)
{
    int idx = content.IndexOf(" - ", StringComparison.Ordinal);
    if (idx < 0) return null;
    return (content[..idx].Trim(), content[(idx + 3)..].Trim());
}
```
Delete it entirely.

Find the call site in `DoProcessChange` (near line 291):
```csharp
var parsed = ParseNowPlaying(content);
```
Replace with:
```csharp
var parsed = NowPlayingParser.Parse(content);
```

- [ ] **Step 3: Verify it builds**

```bash
dotnet build
```

Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add NowPlayingParser.cs TrayApp.cs
git commit -m "refactor: extract NowPlayingParser from TrayApp"
```

---

### Task 3: `SpotifyClient` — injectable constructor and internal helpers

**Files:**
- Modify: `SpotifyClient.cs`

- [ ] **Step 1: Add internal constructor** after the existing field declarations (around line 18):

```csharp
internal SpotifyClient(HttpClient http)
{
    _http = http;
}
```

- [ ] **Step 2: Promote three private methods to `internal static`**

Change:
```csharp
private static string SubstituteConjunction(string artist)
```
To:
```csharp
internal static string SubstituteConjunction(string artist)
```

Change:
```csharp
private static string NormalizeArtist(string s)
```
To:
```csharp
internal static string NormalizeArtist(string s)
```

Change:
```csharp
private static void VerifyArtist(string queryArtist, JsonElement track)
```
To:
```csharp
internal static void VerifyArtist(string queryArtist, JsonElement track)
```

- [ ] **Step 3: Verify it builds**

```bash
dotnet build
```

Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add SpotifyClient.cs
git commit -m "refactor: add internal test seams to SpotifyClient"
```

---

### Task 4: `Logger` — overridable `Dir`

**Files:**
- Modify: `Logger.cs`

- [ ] **Step 1: Replace `_dir` field and update `LogPath`/`AppLogPath`/`Append`**

The current field is `static readonly string _dir`. You must remove **both** `readonly` (otherwise test assignments to `Logger.Dir` cause CS0198) and change the implicit `private` access to `internal`.

Current `Logger.cs` top section:
```csharp
static readonly string _dir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "coverutil");

public static string LogPath => Path.Combine(_dir, "coverutil.log");
public static string AppLogPath => Path.Combine(_dir, "coverutil-app.log");
```

Replace with:
```csharp
internal static string Dir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "coverutil");

public static string LogPath    => Path.Combine(Dir, "coverutil.log");
public static string AppLogPath => Path.Combine(Dir, "coverutil-app.log");
```

In the `Append` method, replace `Directory.CreateDirectory(_dir)` with `Directory.CreateDirectory(Dir)`.

- [ ] **Step 2: Verify it builds**

```bash
dotnet build
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add Logger.cs
git commit -m "refactor: make Logger.Dir overridable for testing"
```

---

### Task 5: `AppConfig` — overridable `ConfigPath`

**Files:**
- Modify: `AppConfig.cs`

- [ ] **Step 1: Change `ConfigPath` from `private static readonly` to `internal static`**

Current:
```csharp
static readonly string ConfigPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "coverutil", "config.json");
```

Replace with:
```csharp
internal static string ConfigPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "coverutil", "config.json");
```

No other changes — `Load()` and `Save()` already reference `ConfigPath` by name and will automatically use the new value.

- [ ] **Step 2: Verify it builds**

```bash
dotnet build
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add AppConfig.cs
git commit -m "refactor: make AppConfig.ConfigPath overridable for testing"
```

---

### Task 6: Scaffold `coverutil.Tests` project

**Files:**
- Create: `coverutil.Tests/coverutil.Tests.csproj`

- [ ] **Step 1: Create directory and project file**

Create `coverutil.Tests/coverutil.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\coverutil.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
    <PackageReference Include="coverlet.collector" Version="6.*" />
    <PackageReference Include="Xunit.SkippableFact" Version="1.*" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Restore packages**

```bash
cd coverutil.Tests
dotnet restore
```

Expected: `Restore succeeded.`

- [ ] **Step 3: Add test project to solution**

The solution file at `C:\Users\Kristoffer\dev\dev.sln` must include the test project so that `dotnet test` from the repo root discovers it:

```bash
dotnet sln C:/Users/Kristoffer/dev/dev.sln add coverutil.Tests/coverutil.Tests.csproj
```

Expected: `Project 'coverutil.Tests/coverutil.Tests.csproj' added to the solution.`

- [ ] **Step 4: Create `SharedStateCollection.cs`**

xUnit v2 requires a `[CollectionDefinition]` marker class for `[Collection("SharedState")]` to actually enforce serial execution. Without this class the attribute is silently ignored.

```csharp
using Xunit;

namespace coverutil.Tests;

[CollectionDefinition("SharedState")]
public class SharedStateCollection { }
```

- [ ] **Step 5: Create `Helpers/FakeHttpHandler.cs`**

```csharp
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace coverutil.Tests.Helpers;

internal class FakeHttpHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();

    public void Enqueue(HttpStatusCode status, string json) =>
        _responses.Enqueue(new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(_responses.Dequeue());
}
```

- [ ] **Step 6: Verify test project builds**

```bash
dotnet build coverutil.Tests/coverutil.Tests.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 7: Commit**

```bash
git add coverutil.Tests/ dev.sln
git commit -m "chore: scaffold coverutil.Tests project with FakeHttpHandler and SharedStateCollection"
```

---

## Chunk 2: Pure Logic Tests

### Task 7: `ParsingTests`

**Files:**
- Create: `coverutil.Tests/ParsingTests.cs`

- [ ] **Step 1: Write `ParsingTests.cs`**

```csharp
using Xunit;

namespace coverutil.Tests;

public class ParsingTests
{
    [Theory]
    [InlineData("Artist - Title", "Artist", "Title")]
    [InlineData("A - B - C",      "A",      "B - C")]
    [InlineData("  Artist  -  Title  ", "Artist", "Title")]
    public void Parse_ValidInput_ReturnsParsedPair(string input, string expectedArtist, string expectedTitle)
    {
        var result = NowPlayingParser.Parse(input);
        Assert.NotNull(result);
        Assert.Equal(expectedArtist, result.Value.artist);
        Assert.Equal(expectedTitle,  result.Value.title);
    }

    [Theory]
    [InlineData("NoSeparator")]
    [InlineData("")]
    public void Parse_InvalidInput_ReturnsNull(string input)
    {
        Assert.Null(NowPlayingParser.Parse(input));
    }
}
```

- [ ] **Step 2: Run tests**

```bash
dotnet test coverutil.Tests/ --filter "FullyQualifiedName~ParsingTests"
```

Expected: `5 passed, 0 failed`

- [ ] **Step 3: Commit**

```bash
git add coverutil.Tests/ParsingTests.cs
git commit -m "test: add ParsingTests for NowPlayingParser"
```

---

### Task 8: `LoggerTests`

**Files:**
- Create: `coverutil.Tests/LoggerTests.cs`

- [ ] **Step 1: Write `LoggerTests.cs`**

```csharp
using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace coverutil.Tests;

[Collection("SharedState")]
public class LoggerTests : IDisposable
{
    private readonly string _originalDir = Logger.Dir;
    private readonly string _tempDir;

    public LoggerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"coverutil-log-{Guid.NewGuid()}");
        Logger.Dir = _tempDir;
    }

    public void Dispose()
    {
        Logger.Dir = _originalDir;
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Log_WritesToDetailedLog()
    {
        Logger.Log("hello");
        Assert.Contains("hello", File.ReadAllText(Logger.LogPath));
    }

    [Fact]
    public void LogApp_WritesToBothLogs()
    {
        Logger.LogApp("event");
        Assert.Contains("event",       File.ReadAllText(Logger.AppLogPath));
        Assert.Contains("[APP] event", File.ReadAllText(Logger.LogPath));
    }

    [Fact]
    public void Log_CreatesDirectoryIfMissing()
    {
        Assert.False(Directory.Exists(Logger.Dir));
        Logger.Log("create-dir-test");
        Assert.True(File.Exists(Logger.LogPath));
    }

    [Fact]
    public void Log_TimestampPrefixFormat()
    {
        Logger.Log("ts-check");
        var line = File.ReadAllLines(Logger.LogPath)[0];
        Assert.Matches(@"^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\]", line);
    }
}
```

- [ ] **Step 2: Run tests**

```bash
dotnet test coverutil.Tests/ --filter "FullyQualifiedName~LoggerTests"
```

Expected: `4 passed, 0 failed`

- [ ] **Step 3: Commit**

```bash
git add coverutil.Tests/LoggerTests.cs
git commit -m "test: add LoggerTests"
```

---

### Task 9: `AppConfigTests`

**Files:**
- Create: `coverutil.Tests/AppConfigTests.cs`

- [ ] **Step 1: Write `AppConfigTests.cs`**

```csharp
using System;
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
            NowPlayingPath      = @"C:\np.txt",
            OutputPath          = @"C:\out.jpg",
            DefaultCoverPath    = @"C:\def.jpg",
            CloseToTray         = false,
            WindowX             = 100,
            WindowY             = 200,
            WindowWidth         = 350
        };
        cfg.Save();

        var loaded = AppConfig.Load();
        Assert.Equal(cfg.SpotifyClientId,     loaded.SpotifyClientId);
        Assert.Equal(cfg.SpotifyClientSecret, loaded.SpotifyClientSecret);
        Assert.Equal(cfg.NowPlayingPath,      loaded.NowPlayingPath);
        Assert.Equal(cfg.OutputPath,          loaded.OutputPath);
        Assert.Equal(cfg.DefaultCoverPath,    loaded.DefaultCoverPath);
        Assert.Equal(cfg.CloseToTray,         loaded.CloseToTray);
        Assert.Equal(cfg.WindowX,             loaded.WindowX);
        Assert.Equal(cfg.WindowY,             loaded.WindowY);
        Assert.Equal(cfg.WindowWidth,         loaded.WindowWidth);
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
}
```

- [ ] **Step 2: Run tests**

```bash
dotnet test coverutil.Tests/ --filter "FullyQualifiedName~AppConfigTests"
```

Expected: `6 passed, 0 failed`

- [ ] **Step 3: Commit**

```bash
git add coverutil.Tests/AppConfigTests.cs
git commit -m "test: add AppConfigTests"
```

---

### Task 10: `ImageHelperTests`

**Files:**
- Create: `coverutil.Tests/ImageHelperTests.cs`

- [ ] **Step 1: Write `ImageHelperTests.cs`**

```csharp
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
        ImageHelper.ResizeAndSaveAsJpeg(MakePng(100, 100, Color.Red), _outputPath);
        using var img = Image.FromFile(_outputPath);
        Assert.Equal(640, img.Width);
        Assert.Equal(640, img.Height);
    }

    [Fact]
    public void Landscape_Input_HasLetterboxBars()
    {
        ImageHelper.ResizeAndSaveAsJpeg(MakePng(200, 100, Color.Red), _outputPath);
        using var bmp = new Bitmap(_outputPath);
        Assert.Equal(640, bmp.Width);
        Assert.Equal(640, bmp.Height);
        // Top-centre pixel should be black (letterbox bar above the image)
        var pixel = bmp.GetPixel(320, 0);
        Assert.True(pixel.R < 10 && pixel.G < 10 && pixel.B < 10,
            $"Expected black letterbox bar at (320,0), got R={pixel.R} G={pixel.G} B={pixel.B}");
    }

    [Fact]
    public void Portrait_Input_HasPillarboxBars()
    {
        ImageHelper.ResizeAndSaveAsJpeg(MakePng(100, 200, Color.Red), _outputPath);
        using var bmp = new Bitmap(_outputPath);
        Assert.Equal(640, bmp.Width);
        Assert.Equal(640, bmp.Height);
        // Left-centre pixel should be black (pillarbox bar left of the image)
        var pixel = bmp.GetPixel(0, 320);
        Assert.True(pixel.R < 10 && pixel.G < 10 && pixel.B < 10,
            $"Expected black pillarbox bar at (0,320), got R={pixel.R} G={pixel.G} B={pixel.B}");
    }

    [Fact]
    public void Output_IsValidJpeg()
    {
        ImageHelper.ResizeAndSaveAsJpeg(MakePng(100, 100, Color.Blue), _outputPath);
        using var img = Image.FromFile(_outputPath);
        Assert.NotNull(img);
    }
}
```

- [ ] **Step 2: Run tests**

```bash
dotnet test coverutil.Tests/ --filter "FullyQualifiedName~ImageHelperTests"
```

Expected: `4 passed, 0 failed`

- [ ] **Step 3: Commit**

```bash
git add coverutil.Tests/ImageHelperTests.cs
git commit -m "test: add ImageHelperTests"
```

---

## Chunk 3: SpotifyClient Tests

### Task 11: `SpotifyClientTests` — unit tests

**Files:**
- Create: `coverutil.Tests/SpotifyClientTests.cs`

> **Note on queue sizes:** The conjunction-retry test requires only 3 queued responses (token, mismatch, valid), not 4. The spec table shows "token, mismatch, token, valid" but that sequence applies to the 401-retry path. The conjunction retry reuses the existing access token and goes straight to a second `DoSearchAsync` call without requesting a new token.

- [ ] **Step 1: Write `SpotifyClientTests.cs`**

```csharp
using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using coverutil.Tests.Helpers;

namespace coverutil.Tests;

public class SpotifyClientTests
{
    // ── Canned JSON ───────────────────────────────────────────────────────────

    private const string TokenJson =
        """{ "access_token": "test-token", "expires_in": 3600 }""";

    private static string SearchJson(string artistName) => $$"""
        {
          "tracks": {
            "items": [{
              "artists": [{ "name": "{{artistName}}" }],
              "album": {
                "artists": [{ "name": "{{artistName}}" }],
                "images": [{ "url": "https://example.com/cover.jpg" }]
              }
            }]
          }
        }
        """;

    private const string EmptyResultsJson =
        """{ "tracks": { "items": [] } }""";

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static SpotifyClient MakeClient(FakeHttpHandler handler)
    {
        var client = new SpotifyClient(new HttpClient(handler));
        client.Configure("test-id", "test-secret");
        return client;
    }

    private static JsonElement MakeTrackElement(string artistName)
    {
        var json = $$"""
            {
              "artists": [{ "name": "{{artistName}}" }],
              "album": { "artists": [{ "name": "{{artistName}}" }] }
            }
            """;
        return JsonDocument.Parse(json).RootElement;
    }

    // ── SearchTrackAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task Search_Success_ReturnsImageUrl()
    {
        var h = new FakeHttpHandler();
        h.Enqueue(HttpStatusCode.OK, TokenJson);
        h.Enqueue(HttpStatusCode.OK, SearchJson("Daft Punk"));

        var url = await MakeClient(h).SearchTrackAsync("Daft Punk", "Get Lucky");
        Assert.Equal("https://example.com/cover.jpg", url);
    }

    [Fact]
    public async Task Search_CacheHit_HttpCalledOnlyOnce()
    {
        var h = new FakeHttpHandler();
        h.Enqueue(HttpStatusCode.OK, TokenJson);
        h.Enqueue(HttpStatusCode.OK, SearchJson("Daft Punk"));

        var sut  = MakeClient(h);
        var url1 = await sut.SearchTrackAsync("Daft Punk", "Get Lucky");
        var url2 = await sut.SearchTrackAsync("Daft Punk", "Get Lucky"); // cache hit — no dequeue
        Assert.Equal(url1, url2);
        // If there were a cache miss, Dequeue on an empty queue throws InvalidOperationException,
        // which would fail the test automatically.
    }

    [Fact]
    public async Task Search_401_RefreshesTokenAndRetries()
    {
        var h = new FakeHttpHandler();
        h.Enqueue(HttpStatusCode.OK,           TokenJson);          // initial token
        h.Enqueue(HttpStatusCode.Unauthorized, "{}");               // 401 on first search
        h.Enqueue(HttpStatusCode.OK,           TokenJson);          // token refresh
        h.Enqueue(HttpStatusCode.OK,           SearchJson("Daft Punk")); // retry search

        var url = await MakeClient(h).SearchTrackAsync("Daft Punk", "Get Lucky");
        Assert.Equal("https://example.com/cover.jpg", url);
    }

    [Fact]
    public async Task Search_NoResults_Throws()
    {
        var h = new FakeHttpHandler();
        h.Enqueue(HttpStatusCode.OK, TokenJson);
        h.Enqueue(HttpStatusCode.OK, EmptyResultsJson);

        var ex = await Assert.ThrowsAsync<Exception>(
            () => MakeClient(h).SearchTrackAsync("Daft Punk", "Get Lucky"));
        Assert.StartsWith("No results for:", ex.Message);
    }

    [Fact]
    public async Task Search_ArtistMismatch_ThrowsWhenNoConjunction()
    {
        var h = new FakeHttpHandler();
        h.Enqueue(HttpStatusCode.OK, TokenJson);
        h.Enqueue(HttpStatusCode.OK, SearchJson("Coldplay")); // mismatch

        var ex = await Assert.ThrowsAsync<Exception>(
            () => MakeClient(h).SearchTrackAsync("Radiohead", "Creep"));
        Assert.StartsWith("Artist mismatch:", ex.Message);
    }

    [Fact]
    public async Task Search_ArtistMismatch_RetriesWithConjunctionTransform()
    {
        // "Marit og Irene" → SubstituteConjunction → "Marit & Irene"
        // Queue: token, mismatch (3 responses — no second token needed for this retry path)
        var h = new FakeHttpHandler();
        h.Enqueue(HttpStatusCode.OK, TokenJson);
        h.Enqueue(HttpStatusCode.OK, SearchJson("Coldplay"));          // first attempt: mismatch
        h.Enqueue(HttpStatusCode.OK, SearchJson("Marit & Irene"));     // retry with transformed artist

        var url = await MakeClient(h).SearchTrackAsync("Marit og Irene", "En sang");
        Assert.Equal("https://example.com/cover.jpg", url);
    }

    // ── SubstituteConjunction ─────────────────────────────────────────────────

    [Theory]
    [InlineData("Marit og Irene",      "Marit & Irene")]
    [InlineData("Simon and Garfunkel", "Simon & Garfunkel")]
    [InlineData("AC/DC",               "AC/DC")]
    public void SubstituteConjunction_TransformsAsExpected(string input, string expected) =>
        Assert.Equal(expected, SpotifyClient.SubstituteConjunction(input));

    // ── NormalizeArtist ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("Björk",      "bjork")]
    [InlineData("  Ålborg  ", "alborg")]
    [InlineData("Sigur Rós",  "sigur ros")]
    public void NormalizeArtist_FoldsDiacriticsAndLowercases(string input, string expected) =>
        Assert.Equal(expected, SpotifyClient.NormalizeArtist(input));

    // ── VerifyArtist ──────────────────────────────────────────────────────────

    [Fact]
    public void VerifyArtist_ExactMatch_DoesNotThrow() =>
        SpotifyClient.VerifyArtist("Radiohead", MakeTrackElement("Radiohead"));

    [Fact]
    public void VerifyArtist_QueryIsSubstringOfReturned_DoesNotThrow() =>
        SpotifyClient.VerifyArtist("Daft Punk", MakeTrackElement("Daft Punk feat. Pharrell"));

    [Fact]
    public void VerifyArtist_ReturnedIsSubstringOfQuery_DoesNotThrow() =>
        SpotifyClient.VerifyArtist("The Beatles", MakeTrackElement("Beatles"));

    [Fact]
    public void VerifyArtist_NoMatch_Throws()
    {
        var ex = Assert.Throws<Exception>(
            () => SpotifyClient.VerifyArtist("Radiohead", MakeTrackElement("Coldplay")));
        Assert.StartsWith("Artist mismatch:", ex.Message);
    }
}
```

- [ ] **Step 2: Run unit tests**

```bash
dotnet test coverutil.Tests/ --filter "Category!=Integration"
```

Expected: all tests pass (total ~25 across all test classes).

- [ ] **Step 3: Commit**

```bash
git add coverutil.Tests/SpotifyClientTests.cs
git commit -m "test: add SpotifyClient unit tests"
```

---

### Task 12: `SpotifyClientTests` — integration test

**Files:**
- Modify: `coverutil.Tests/SpotifyClientTests.cs`

- [ ] **Step 1: Append integration test class to `SpotifyClientTests.cs`**

Add this class after the closing brace of `SpotifyClientTests`:

```csharp
public class SpotifyClientIntegrationTests
{
    [SkippableFact]
    [Trait("Category", "Integration")]
    public async Task Search_RealSpotify_ReturnsImageUrl()
    {
        var clientId     = Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_ID");
        var clientSecret = Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_SECRET");
        Skip.If(string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret),
            "SPOTIFY_CLIENT_ID / SPOTIFY_CLIENT_SECRET not set — skipping integration test");

        var sut = new SpotifyClient();
        sut.Configure(clientId!, clientSecret!);

        var url = await sut.SearchTrackAsync("Daft Punk", "Get Lucky");
        Assert.False(string.IsNullOrEmpty(url));
        Assert.StartsWith("https://", url);
    }
}
```

Add the `using Xunit.Sdk;` import (needed for `SkippableFact`) at the top of the file. The `Skip` class comes from `Xunit.SkippableFact` package, already in the `.csproj`.

The full list of usings at the top of `SpotifyClientTests.cs` should be:
```csharp
using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using coverutil.Tests.Helpers;
```

(`SkippableFact` and `Skip` are picked up automatically from the `Xunit.SkippableFact` package without an extra using.)

- [ ] **Step 2: Run — verify integration test skips without credentials**

```bash
dotnet test coverutil.Tests/ --filter "Category=Integration" --logger "console;verbosity=normal"
```

Expected: `1 skipped` (or `0 passed, 0 failed, 1 skipped`) with message "SPOTIFY_CLIENT_ID / SPOTIFY_CLIENT_SECRET not set".

- [ ] **Step 3: (Optional) Run with real credentials**

```bash
$env:SPOTIFY_CLIENT_ID="your-id"; $env:SPOTIFY_CLIENT_SECRET="your-secret"
dotnet test coverutil.Tests/ --filter "Category=Integration"
```

Expected: `1 passed`

- [ ] **Step 4: Commit**

```bash
git add coverutil.Tests/SpotifyClientTests.cs
git commit -m "test: add Spotify integration test"
```

---

### Task 13: Full run and final commit

- [ ] **Step 1: Run all unit tests**

```bash
dotnet test coverutil.Tests/ --filter "Category!=Integration"
```

Expected: all tests pass, 0 failures.

- [ ] **Step 2: Push**

```bash
git push
```

---

## Running Tests — Quick Reference

```bash
# All unit tests (fast, no network)
dotnet test coverutil.Tests/ --filter "Category!=Integration"

# Integration tests only (requires env vars)
dotnet test coverutil.Tests/ --filter "Category=Integration"

# All tests
dotnet test coverutil.Tests/

# With coverage
dotnet test coverutil.Tests/ --collect:"XPlat Code Coverage"
```
