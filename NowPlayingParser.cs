using System;

namespace coverutil;

internal static class NowPlayingParser
{
    internal static (string artist, string title)? Parse(string content)
    {
        int idx = content.IndexOf(" - ", StringComparison.Ordinal);
        if (idx < 0) return null;
        return (content[..idx].Trim(), content[(idx + 3)..].Trim());
    }
}
