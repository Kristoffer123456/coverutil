using System;
using System.IO;

namespace coverutil;

public static class Logger
{
    internal static string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "coverutil");

    // Full detailed log — opened in Notepad via LogViewerForm
    public static string LogPath => Path.Combine(Dir, "coverutil.log");

    // Brief app log — shown in the in-app log viewer
    public static string AppLogPath => Path.Combine(Dir, "coverutil-app.log");

    // Writes to detailed log only (API responses, internals, stack traces)
    public static void Log(string message) =>
        Append(LogPath, message);

    // Writes to both logs (high-level events the user cares about)
    public static void LogApp(string message)
    {
        Append(LogPath, $"[APP] {message}");
        Append(AppLogPath, message);
    }

    private static void Append(string path, string message)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { }
    }
}
