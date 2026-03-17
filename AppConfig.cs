using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

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

    public List<string> NowPlayingSources { get; set; } = new();
    public int  OutputSize        { get; set; } = 640;
    public bool StartWithWindows  { get; set; } = false;

    // Legacy — kept for migration only; not used in new code
    [JsonIgnore]
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
