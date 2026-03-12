using System;
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
        // Invalidate cached token when credentials change
        _accessToken = null;
        _expiresAt = DateTime.MinValue;
    }

    private async Task GetTokenAsync()
    {
        if (_accessToken != null && DateTime.UtcNow < _expiresAt - TimeSpan.FromSeconds(60))
            return;

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_clientId}:{_clientSecret}"));
        var request = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded");

        var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        _accessToken = root.GetProperty("access_token").GetString()
            ?? throw new Exception("Spotify token response missing access_token");
        int expiresIn = root.GetProperty("expires_in").GetInt32();
        _expiresAt = DateTime.UtcNow.AddSeconds(expiresIn);
    }

    public async Task<string> SearchTrackAsync(string artist, string title)
    {
        await GetTokenAsync();
        return await DoSearchAsync(artist, title, retried: false);
    }

    private async Task<string> DoSearchAsync(string artist, string title, bool retried)
    {
        var query = Uri.EscapeDataString($"{artist} {title}");
        var url = $"https://api.spotify.com/v1/search?q={query}&type=track&limit=1";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        var response = await _http.SendAsync(request);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && !retried)
        {
            _accessToken = null;
            _expiresAt = DateTime.MinValue;
            await GetTokenAsync();
            return await DoSearchAsync(artist, title, retried: true);
        }

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var items = root.GetProperty("tracks").GetProperty("items");
        if (items.GetArrayLength() == 0)
            throw new Exception($"No results for: {artist} - {title}");

        var images = items[0].GetProperty("album").GetProperty("images");
        if (images.GetArrayLength() == 0)
            throw new Exception($"No art for: {artist} - {title}");

        return images[0].GetProperty("url").GetString()
            ?? throw new Exception("Image URL was null");
    }

    public async Task FetchAndSaveImageAsync(string imageUrl, string outputPath)
    {
        var bytes = await _http.GetByteArrayAsync(imageUrl);
        await System.IO.File.WriteAllBytesAsync(outputPath, bytes);
    }
}
