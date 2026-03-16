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

## Test Framework & Dependencies

| Package | Purpose |
|---|---|
| `xunit` | Test framework |
| `xunit.runner.visualstudio` | VS / `dotnet test` runner |
| `Microsoft.NET.Test.Sdk` | Required SDK for test execution |
| `coverlet.collector` | Code coverage collection |

No mocking library needed. HTTP responses are faked via a hand-written `FakeHttpHandler : HttpMessageHandler` (~15 lines).

## Production Code Changes

Three minimal, non-breaking changes to production code are required:

### 1. `SpotifyClient` — injectable `HttpClient`

Add an `internal` constructor that accepts an `HttpClient` for use in tests. The existing public parameterless constructor is unchanged.

```csharp
internal SpotifyClient(HttpClient http)
{
    _http = http;
}
```

### 2. Extract `ParseNowPlaying` into `NowPlayingParser`

`TrayApp.ParseNowPlaying` is currently a private static method. Extract it to a new `internal static class NowPlayingParser` so it can be tested directly. `TrayApp` calls `NowPlayingParser.Parse(content)` — behaviour is identical.

### 3. `InternalsVisibleTo`

Add to `coverutil.csproj`:

```xml
<ItemGroup>
  <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleTo">
    <_Parameter1>coverutil.Tests</_Parameter1>
  </AssemblyAttribute>
</ItemGroup>
```

## Test Cases

### `NowPlayingParser` (ParsingTests.cs)

| Input | Expected output |
|---|---|
| `"Artist - Title"` | `("Artist", "Title")` |
| `"A - B - C"` | `("A", "B - C")` |
| `"NoSeparator"` | `null` |
| `""` | `null` |
| `"  Artist  -  Title  "` | `("Artist", "Title")` (trimmed) |

### `SpotifyClient` — unit (SpotifyClientTests.cs)

All tests use `FakeHttpHandler` configured to return canned JSON responses.

| Scenario | Assertion |
|---|---|
| First search → token fetched, image URL returned | Returns correct URL |
| Second search with same key → cache hit | HTTP called only once total |
| 401 on search → token refreshed, search retried | Returns correct URL on retry |
| Zero results in response | Throws `"No results for: ..."` |
| Artist mismatch in response | Throws `"Artist mismatch: ..."` |
| `SubstituteConjunction("Marit og Irene")` | `"Marit & Irene"` |
| `SubstituteConjunction("Simon and Garfunkel")` | `"Simon & Garfunkel"` |
| `SubstituteConjunction("AC/DC")` | `"AC/DC"` (unchanged) |
| `NormalizeArtist("Björk")` | `"bjork"` |
| `NormalizeArtist("  Ålborg  ")` | `"alborg"` |
| `VerifyArtist` — exact match | Does not throw |
| `VerifyArtist` — substring match (feat.) | Does not throw |
| `VerifyArtist` — no match | Throws `"Artist mismatch: ..."` |

### `SpotifyClient` — integration (SpotifyClientTests.cs)

Decorated with `[Trait("Category", "Integration")]`. Skipped automatically if `SPOTIFY_CLIENT_ID` or `SPOTIFY_CLIENT_SECRET` environment variables are not set.

| Scenario | Assertion |
|---|---|
| Search for known track (e.g. "Daft Punk - Get Lucky") | Returns a non-empty HTTPS image URL |

### `AppConfig` (AppConfigTests.cs)

Uses a temp directory for all file I/O — no writes to `%APPDATA%`.

| Scenario | Assertion |
|---|---|
| Save then Load roundtrip | All fields match original |
| Load from non-existent file | Returns `new AppConfig()` with defaults |
| Load from invalid JSON | Returns `new AppConfig()` without throwing |
| Default `WindowX` / `WindowY` | `-1` |
| Default `WindowWidth` | `280` |
| Default `CloseToTray` | `true` |

### `ImageHelper` (ImageHelperTests.cs)

Uses in-memory image bytes — output written to temp file.

| Scenario | Assertion |
|---|---|
| Square input (100×100) | Output is 640×640 |
| Landscape input (200×100) | Output is 640×640; top/bottom rows are black (letterbox) |
| Portrait input (100×200) | Output is 640×640; left/right columns are black (pillarbox) |
| Output is valid JPEG | `Image.FromFile` does not throw |

### `Logger` (LoggerTests.cs)

Uses a temp directory — no writes to `%APPDATA%`.

| Scenario | Assertion |
|---|---|
| `Log("msg")` | Message appears in `coverutil.log` |
| `LogApp("msg")` | Message appears in both log files; `coverutil.log` entry has `[APP]` prefix |
| `Log` when directory doesn't exist | Directory created, file written without throwing |
| Timestamp format | Log lines start with `[YYYY-MM-DD HH:mm:ss]` |

## FakeHttpHandler

A simple reusable helper in `Helpers/FakeHttpHandler.cs`:

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
# All tests
dotnet test

# Unit tests only (no network, suitable for CI / pre-commit)
dotnet test --filter "Category!=Integration"

# Integration tests only
dotnet test --filter "Category=Integration"

# With code coverage
dotnet test --collect:"XPlat Code Coverage"
```

## Out of Scope

- `MainForm` and `TrayApp` UI instantiation — WinForms controls require a message pump and are not unit-testable without significant refactoring cost
- Screenshot or accessibility testing
- Performance benchmarks
