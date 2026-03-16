# Test Suite Design — coverutil

**Date:** 2026-03-17
**Status:** Approved

## Overview

Add an automated xUnit test suite to coverutil covering core logic stability and Spotify integration correctness. Tests run via `dotnet test` with trait-based filtering to separate fast unit tests from network-dependent integration tests.

## Goals

- Catch regressions in Spotify search logic (artist normalization, retry, caching)
- Verify edge-case handling in file parsing, config serialization, and image resizing
- Provide a fast unit-test pass (`Category!=Integration`) suitable as a local pre-commit check
- Provide optional integration tests against the real Spotify API when credentials are available

## Project Structure

```
coverutil/
├── coverutil.csproj
├── coverutil.Tests/
│   ├── coverutil.Tests.csproj
│   ├── SpotifyClientTests.cs
│   ├── AppConfigTests.cs
│   ├── ImageHelperTests.cs
│   ├── LoggerTests.cs
│   ├── ParsingTests.cs
│   └── Helpers/
│       └── FakeHttpHandler.cs
```

## Test Project `.csproj`

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

The test project targets `net8.0-windows` with `UseWindowsForms` because it references the production project which depends on `System.Drawing` (GDI+). Tests must run on Windows; GDI+ is not available on headless Linux agents without additional native libraries.

## Production Code Changes

Six minimal, non-breaking changes to production code are required:

### 1. `SpotifyClient` — injectable `HttpClient`

Add an `internal` constructor that accepts an `HttpClient` for use in tests. The existing public parameterless constructor is unchanged.

```csharp
internal SpotifyClient(HttpClient http)
{
    _http = http;
}
```

### 2. `SpotifyClient` — promote private helpers to `internal`

Change the access modifier of `SubstituteConjunction`, `NormalizeArtist`, and `VerifyArtist` from `private static` to `internal static`. `InternalsVisibleTo` only exposes `internal` members; `private` members remain inaccessible regardless.

### 3. Extract `ParseNowPlaying` into `NowPlayingParser`

`TrayApp.ParseNowPlaying` is currently a `private static` method. Extract it to a new `internal static class NowPlayingParser` with a single method:

```csharp
internal static (string artist, string title)? Parse(string content)
```

The implementation uses `content.IndexOf(" - ", StringComparison.Ordinal)` and splits on the **first occurrence only**, returning everything after as the title (e.g. `"A - B - C"` → `("A", "B - C")`). `TrayApp` calls `NowPlayingParser.Parse(content)` — behaviour is identical.

### 4. `Logger` — overridable directory for testing

Change `_dir` from `private static readonly` to `internal static`:

```csharp
internal static string Dir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "coverutil");
```

Update all references in `Logger` from `_dir` to `Dir`. This includes the `Append` method and the computed properties `LogPath` and `AppLogPath`, which must both be changed to derive from `Dir`:

```csharp
public static string LogPath    => Path.Combine(Dir, "coverutil.log");
public static string AppLogPath => Path.Combine(Dir, "coverutil-app.log");
```

Removing `readonly` is intentional to allow test-time redirection. Because this is mutable shared static state, `LoggerTests` must carry `[Collection("SharedState")]` (see Parallelism section below).

### 5. `AppConfig` — overridable config path for testing

Change `ConfigPath` from `private static readonly` to `internal static`:

```csharp
internal static string ConfigPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "coverutil", "config.json");
```

Tests set `AppConfig.ConfigPath` to a unique temp file path before each test and restore the original value in `Dispose`. Removing `readonly` is intentional. `AppConfigTests` must carry `[Collection("SharedState")]` (see Parallelism section below).

### 6. `InternalsVisibleTo`

Add to `coverutil.csproj`:

```xml
<ItemGroup>
  <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleTo">
    <_Parameter1>coverutil.Tests</_Parameter1>
  </AssemblyAttribute>
</ItemGroup>
```

## Parallelism and Shared State

`AppConfig.ConfigPath` and `Logger.Dir` are mutable static fields. xUnit runs test classes in parallel by default. To prevent cross-test state pollution, both `AppConfigTests` and `LoggerTests` must be decorated with the same collection name, which forces them to run sequentially:

```csharp
[Collection("SharedState")]
public class AppConfigTests { ... }

[Collection("SharedState")]
public class LoggerTests { ... }
```

All other test classes (`SpotifyClientTests`, `ImageHelperTests`, `ParsingTests`) are stateless and may run in parallel freely.

## Test Cases

### `NowPlayingParser` (ParsingTests.cs)

| Input | Expected output |
|---|---|
| `"Artist - Title"` | `("Artist", "Title")` |
| `"A - B - C"` | `("A", "B - C")` (title keeps everything after first ` - `) |
| `"NoSeparator"` | `null` |
| `""` | `null` |
| `"  Artist  -  Title  "` | `("Artist", "Title")` (`IndexOf(" - ")` matches the first ` - ` embedded in `  -  `; leading/trailing whitespace on each token is trimmed) |

### `SpotifyClient` — unit (SpotifyClientTests.cs)

Tests construct `new SpotifyClient(new HttpClient(fakeHandler))` and call `Configure("id", "secret")`.

**Token response JSON** (used for all `Enqueue` calls to the token endpoint):
```json
{ "access_token": "test-token", "expires_in": 3600 }
```

**Search response JSON** (valid track):
```json
{
  "tracks": {
    "items": [{
      "artists": [{ "name": "Daft Punk" }],
      "album": {
        "artists": [{ "name": "Daft Punk" }],
        "images": [{ "url": "https://example.com/cover.jpg" }]
      }
    }]
  }
}
```

**Empty results JSON**: `{ "tracks": { "items": [] } }`

**Mismatched artist JSON**: same as valid track but artist name changed to `"Coldplay"`.

| Scenario | `FakeHttpHandler` queue | Assertion |
|---|---|---|
| Successful search | token, search | Returns `"https://example.com/cover.jpg"` |
| Cache hit on second call | token, search | Second `SearchTrackAsync("Daft Punk", "Get Lucky")` call (identical args) returns same URL; `handler` has no more items to dequeue |
| 401 on search triggers retry | token, 401 search, token, valid search | Returns correct URL |
| Zero results | token, empty results | Throws with message starting `"No results for:"` |
| Artist mismatch (no conjunction to try) | token, mismatched artist | Throws with message starting `"Artist mismatch:"` |
| Artist mismatch triggers conjunction retry | token, mismatched artist, token, valid search; artist = `"Marit og Irene"` | Returns URL; second search uses `"Marit & Irene"` |
| `SubstituteConjunction("Marit og Irene")` | — | `"Marit & Irene"` |
| `SubstituteConjunction("Simon and Garfunkel")` | — | `"Simon & Garfunkel"` |
| `SubstituteConjunction("AC/DC")` | — | `"AC/DC"` (unchanged) |
| `NormalizeArtist("Björk")` | — | `"bjork"` |
| `NormalizeArtist("  Ålborg  ")` | — | `"alborg"` |
| `NormalizeArtist("Sigur Rós")` | — | `"sigur ros"` |
| `VerifyArtist` exact match | track JSON with artist `"Radiohead"`, query `"Radiohead"` | Does not throw |
| `VerifyArtist` query is substring of returned | track JSON with artist `"Daft Punk feat. Pharrell"`, query `"Daft Punk"` | Does not throw |
| `VerifyArtist` returned is substring of query | track JSON with artist `"Beatles"`, query `"The Beatles"` | Does not throw |
| `VerifyArtist` no match | track JSON with artist `"Coldplay"`, query `"Radiohead"` | Throws `"Artist mismatch:"` |

**`VerifyArtist` minimal JSON shape** — the method receives a `JsonElement` representing a single track item. The minimal shape required is:

```json
{
  "artists": [{ "name": "ArtistName" }],
  "album": {
    "artists": [{ "name": "ArtistName" }]
  }
}
```

Tests construct this via `JsonDocument.Parse(json).RootElement` and pass the root element directly to `SpotifyClient.VerifyArtist(queryArtist, trackElement)`.

### `SpotifyClient` — integration (SpotifyClientTests.cs)

Uses `[SkippableFact]` from `Xunit.SkippableFact`. At the start of the test body, reads `Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_ID")` and `"SPOTIFY_CLIENT_SECRET"`. If either is null or empty, calls `Skip.If(true, "SPOTIFY_CLIENT_ID / SPOTIFY_CLIENT_SECRET not set")`.

| Scenario | Assertion |
|---|---|
| Search for `"Daft Punk"` / `"Get Lucky"` | Returns non-empty string starting with `"https://"` |

### `AppConfig` (AppConfigTests.cs)

`[Collection("SharedState")]`. Each test saves the original `AppConfig.ConfigPath` in a field, sets it to `Path.Combine(Path.GetTempPath(), $"coverutil-test-{Guid.NewGuid()}.json")` before the test, and restores the original value in `Dispose`.

| Scenario | Assertion |
|---|---|
| Save then Load roundtrip | All fields (`SpotifyClientId`, `NowPlayingPath`, `OutputPath`, `DefaultCoverPath`, `CloseToTray`, `WindowX`, `WindowY`, `WindowWidth`) match original |
| Load when file does not exist | Returns `new AppConfig()` without throwing |
| Load when file contains invalid JSON | Returns `new AppConfig()` without throwing |
| Default `CloseToTray` | `true` |
| Default `WindowX` / `WindowY` | `-1` |
| Default `WindowWidth` | `280` |

### `ImageHelper` (ImageHelperTests.cs)

Each test creates a small in-memory `Bitmap`, saves it to a `MemoryStream` as PNG, then calls `ResizeAndSaveAsJpeg(bytes, outputPath)` where `outputPath = Path.GetTempFileName()`. After assertions, `File.Delete(outputPath)` is called in `Dispose`.

| Scenario | Input bitmap | Assertion |
|---|---|---|
| Square input | 100×100 solid colour | Loaded output image is 640×640 |
| Landscape input | 200×100 solid colour | Output is 640×640; `bitmap.GetPixel(0, 0)` is `Color.Black` (letterbox bar) |
| Portrait input | 100×200 solid colour | Output is 640×640; `bitmap.GetPixel(0, 0)` is `Color.Black` (pillarbox bar) |
| Valid JPEG output | any | `Image.FromFile(outputPath)` completes without throwing |

### `Logger` (LoggerTests.cs)

`[Collection("SharedState")]`. Each test saves the original `Logger.Dir`, sets it to `Path.Combine(Path.GetTempPath(), $"coverutil-log-test-{Guid.NewGuid()}")`, and restores + deletes the temp directory in `Dispose`.

| Scenario | Assertion |
|---|---|
| `Log("hello")` | `Path.Combine(Logger.Dir, "coverutil.log")` exists and contains `"hello"` |
| `LogApp("event")` | `coverutil-app.log` contains `"event"`; `coverutil.log` contains `"[APP] event"` |
| `Log` when directory does not exist | Directory is created; file written; no exception thrown |
| Timestamp prefix | Each written line matches regex `^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\]` |

## FakeHttpHandler

```csharp
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

## Running Tests

```bash
# All tests (unit + integration, requires Spotify credentials in env)
dotnet test

# Unit tests only — fast, no network, suitable for local pre-commit
dotnet test --filter "Category!=Integration"

# Integration tests only
dotnet test --filter "Category=Integration"

# With code coverage
dotnet test --collect:"XPlat Code Coverage"
```

## Out of Scope

- `MainForm` and `TrayApp` UI instantiation — WinForms controls require a message pump; not unit-testable without significant refactoring
- Screenshot or accessibility testing
- Performance benchmarks
- Linux / headless CI — `System.Drawing` (GDI+) requires Windows; tests target `net8.0-windows`
