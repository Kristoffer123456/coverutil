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
        // Queue: token, mismatch, valid (3 responses — no second token needed for this retry path)
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
