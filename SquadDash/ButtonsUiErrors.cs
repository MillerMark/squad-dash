using System;
using System.ComponentModel;
using System.IO;
using System.Security;
using System.Text.Json;

namespace SquadDash;

internal static class ButtonsUiErrors {
    internal static bool IsExpected(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException
            or InvalidOperationException or OverflowException or SecurityException or TimeoutException
            or Win32Exception or NotSupportedException;
}
