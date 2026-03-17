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
