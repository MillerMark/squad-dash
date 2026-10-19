using System.Windows;
using System.Windows.Input;

namespace SquadDash;

internal static class ReadOnlyTextInputGuard
{
    internal static void DisableInputMethod(DependencyObject element)
    {
        InputMethod.SetIsInputMethodEnabled(element, false);
        InputMethod.SetPreferredImeState(element, InputMethodState.Off);
    }
}
