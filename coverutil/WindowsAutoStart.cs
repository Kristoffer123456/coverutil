using System;
using Microsoft.Win32;

namespace coverutil;

internal static class WindowsAutoStart
{
    private const string RegKey    = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "coverutil";

    /// <summary>Returns true if the coverutil autostart registry entry exists.</summary>
    internal static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegKey, writable: false);
        return key?.GetValue(ValueName) != null;
    }

    /// <summary>Writes the autostart registry entry pointing to exePath.</summary>
    internal static void Enable(string exePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegKey, writable: true)
            ?? throw new Exception($"Cannot open registry key: {RegKey}");
        key.SetValue(ValueName, $"\"{exePath}\"");
    }

    /// <summary>Removes the autostart registry entry. No-op if not present.</summary>
    internal static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
