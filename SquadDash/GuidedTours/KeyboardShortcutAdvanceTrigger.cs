using System;
using System.Windows;
using System.Windows.Input;

namespace SquadDash.GuidedTours;

/// <summary>
/// Advance trigger that fires when a specified keyboard shortcut is pressed.
/// Parameter format: modifier-and-key tokens separated by '+', e.g. "Ctrl+Q" or "Ctrl+Shift+F5".
/// Recognized modifier tokens: Ctrl, Alt, Shift, Win.  The last token is the key name.
/// </summary>
internal sealed class KeyboardShortcutAdvanceTrigger : IGuidedTourAdvanceTrigger
{
    private readonly Window _window;

    public KeyboardShortcutAdvanceTrigger(Window window) => _window = window;

    /// <inheritdoc/>
    public IDisposable? Subscribe(string parameter, Action onAdvance)
    {
        if (!TryParse(parameter, out var modifiers, out var key)) return null;

        void Handler(object sender, KeyEventArgs e)
        {
            if (e.Key == key && Keyboard.Modifiers == modifiers)
            {
                e.Handled = true;
                onAdvance();
            }
        }

        _window.PreviewKeyDown += Handler;
        return new Subscription(() => _window.PreviewKeyDown -= Handler);
    }

    private static bool TryParse(string parameter, out ModifierKeys modifiers, out Key key)
    {
        modifiers = ModifierKeys.None;
        key = Key.None;

        var tokens = parameter.Split('+');
        if (tokens.Length == 0) return false;

        var keyConverter = new KeyConverter();
        for (int i = 0; i < tokens.Length - 1; i++)
        {
            modifiers |= tokens[i].Trim().ToUpperInvariant() switch
            {
                "CTRL"  => ModifierKeys.Control,
                "ALT"   => ModifierKeys.Alt,
                "SHIFT" => ModifierKeys.Shift,
                "WIN"   => ModifierKeys.Windows,
                _       => ModifierKeys.None
            };
        }

        string keyToken = tokens[tokens.Length - 1].Trim();
        if (keyConverter.ConvertFromString(keyToken) is Key parsed)
        {
            key = parsed;
            return key != Key.None;
        }
        return false;
    }

    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        public void Dispose() => unsubscribe();
    }
}
