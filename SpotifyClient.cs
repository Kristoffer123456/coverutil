using System;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace coverutil;

public class SpotifyClient
{
    private readonly HttpClient _http = new();
    private string? _accessToken;
    private DateTime _expiresAt = DateTime.MinValue;

    private string _clientId = "";
    private string _clientSecret = "";

    public void Configure(string clientId, string clientSecret)
    {
        _clientId = clientId;
        _clientSecret = clientSecret;
        _accessToken = null;
        _expiresAt = DateTime.MinValue;
    }

    private async Task GetTokenAsync()
    {
        if (_accessToken != null && DateTime.UtcNow < _expiresAt - TimeSpan.FromSeconds(60))
            return;

        Logger.Log("Refreshing Spotify access token");
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_clientId}:{_clientSecret}"));
        var request = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded");

        var response = await _http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Logger.Log($"Token response: HTTP {(int)response.StatusCode} — {body}");
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        _accessToken = root.GetProperty("access_token").GetString()
            ?? throw new Exception("Spotify token response missing access_token");
        int expiresIn = root.GetProperty("expires_in").GetInt32();
        _expiresAt = DateTime.UtcNow.AddSeconds(expiresIn);
        Logger.Log($"Token acquired, expires in {expiresIn}s");
    }

    public async Task<string> SearchTrackAsync(string artist, string title)
    {
        await GetTokenAsync();
        return await DoSearchAsync(artist, title, retried: false);
    }

    private async Task<string> DoSearchAsync(string artist, string title, bool retried)
    {
        // Use Spotify field filters for a precise search instead of freetext
        var query = Uri.EscapeDataString($"artist:{artist} track:{title}");
        var url = $"https://api.spotify.com/v1/search?q={query}&type=track&limit=1";
        Logger.Log($"Spotify search: {url}");

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        var response = await _http.SendAsync(request);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && !retried)
        {
            Logger.Log("Spotify 401 — clearing token cache and retrying");
            _accessToken = null;
            _expiresAt = DateTime.MinValue;
            await GetTokenAsync();
            return await DoSearchAsync(artist, title, retried: true);
        }

        var body = await response.Content.ReadAsStringAsync();
        Logger.Log($"Search response: HTTP {(int)response.StatusCode} — {body}");
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var items = root.GetProperty("tracks").GetProperty("items");
        if (items.GetArrayLength() == 0)
            throw new Exception($"No results for: {artist} - {title}");

        var track = items[0];

        // Verify the returned artist matches what we searched for
        VerifyArtist(artist, track);

        var images = track.GetProperty("album").GetProperty("images");
        if (images.GetArrayLength() == 0)
            throw new Exception($"No art for: {artist} - {title}");

        var imageUrl = images[0].GetProperty("url").GetString()
            ?? throw new Exception("Image URL was null");
        Logger.Log($"Image URL: {imageUrl}");
        return imageUrl;
    }

    /// <summary>
    /// Throws if none of the track's artists (or album artists) match the queried artist.
    /// Comparison is case-insensitive, diacritic-folded, and allows substring matches
    /// to handle "feat.", "og", "and", etc.
    /// </summary>
    private static void VerifyArtist(string queryArtist, JsonElement track)
    {
        var qNorm = NormalizeArtist(queryArtist);

        // Collect all artist names from both track-level and album-level
        var names = new System.Collections.Generic.List<string>();

        foreach (var a in track.GetProperty("artists").EnumerateArray())
        {
            var n = a.GetProperty("name").GetString();
            if (n != null) names.Add(n);
        }
        foreach (var a in track.GetProperty("album").GetProperty("artists").EnumerateArray())
        {
            var n = a.GetProperty("name").GetString();
            if (n != null) names.Add(n);
        }

        foreach (var name in names)
        {
            var rNorm = NormalizeArtist(name);
            // Pass if either side contains the other (handles "The X" vs "X", "X feat. Y" vs "X")
            if (qNorm == rNorm || rNorm.Contains(qNorm) || qNorm.Contains(rNorm))
                return;
        }

        var returned = names.Count > 0 ? string.Join(", ", names) : "unknown";
        Logger.Log($"Artist mismatch: searched '{queryArtist}', got '{returned}'");
        throw new Exception($"Artist mismatch: searched for '{queryArtist}', got '{returned}'");
    }

    /// <summary>
    /// Lowercase + strip diacritics (ø→o, å→a, é→e, etc.) for fuzzy comparison.
    /// </summary>
    private static string NormalizeArtist(string s)
    {
        var decomposed = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (char c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC).Trim();
    }

    public async Task FetchAndSaveImageAsync(string imageUrl, string outputPath)
    {
        Logger.Log($"Downloading image: {imageUrl}");
        var bytes = await _http.GetByteArrayAsync(imageUrl);
        Logger.Log($"Downloaded {bytes.Length} bytes — resizing to 640×640 JPEG");
        ImageHelper.ResizeAndSaveAsJpeg(bytes, outputPath);
    }
}
