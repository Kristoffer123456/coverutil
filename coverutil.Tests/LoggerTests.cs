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
