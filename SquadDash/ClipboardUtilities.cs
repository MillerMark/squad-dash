using System.Windows;

namespace SquadDash;

/// <summary>Shared clipboard helpers used across the application.</summary>
internal static class ClipboardUtilities
{
    /// <summary>
    /// Appends <paramref name="newText"/> to whatever text is already on the clipboard,
    /// separated by CRLF. If the clipboard is empty or holds non-text data the text
    /// simply replaces the current contents (same behaviour as a plain copy).
    /// </summary>
    internal static void AppendToClipboard(string newText)
    {
        if (string.IsNullOrEmpty(newText)) return;
        string existing = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
        string combined = string.IsNullOrEmpty(existing) ? newText : existing + "\r\n" + newText;
        Clipboard.SetText(combined);
    }
}
