using System.Collections.Generic;
using System.Text;

namespace SquadDash;

/// <summary>
/// The 16 standard ANSI terminal colors (8 normal + 8 bright/high-intensity variants),
/// addressed by SGR codes 30-37/90-97 (foreground) and 40-47/100-107 (background).
/// </summary>
internal enum AnsiColor {
    Black, Red, Green, Yellow, Blue, Magenta, Cyan, White,
    BrightBlack, BrightRed, BrightGreen, BrightYellow, BrightBlue, BrightMagenta, BrightCyan, BrightWhite
}

/// <summary>A 24-bit color from an extended SGR sequence (38;2;r;g;b or 48;2;r;g;b).</summary>
internal readonly record struct AnsiRgbColor(byte R, byte G, byte B);

/// <summary>
/// The accumulated text style in effect at a point in an ANSI-formatted stream. Immutable; SGR
/// codes produce a new style via `with` expressions rather than mutating shared state.
/// </summary>
internal readonly record struct AnsiStyle(
    bool Bold = false,
    bool Underline = false,
    AnsiColor? Foreground = null,
    AnsiColor? Background = null,
    AnsiRgbColor? ForegroundRgb = null,
    AnsiRgbColor? BackgroundRgb = null) {
    public static readonly AnsiStyle Default = new();
}

/// <summary>A run of plain text (with ANSI escape sequences already stripped) and the style active for it.</summary>
internal readonly record struct AnsiTextSpan(string Text, AnsiStyle Style);

/// <summary>
/// Single-pass state machine that strips ANSI/VT100 CSI escape sequences (e.g. produced by
/// PowerShell for colored/bold error and warning text) out of captured process output, turning
/// SGR ("Select Graphic Rendition") sequences into style changes rather than visible garbage.
///
/// This class has no WPF dependency so the parsing logic can be unit tested in isolation; a
/// presentation layer (see <see cref="SquadCommandOutputWindow"/>) turns the resulting spans into
/// styled WPF <c>Inline</c>s.
///
/// Unsupported or malformed sequences (unknown SGR codes, non-SGR CSI sequences like cursor
/// movement, an escape truncated at the end of a buffer) are silently discarded rather than
/// surfaced as text or treated as an error.
/// </summary>
internal static class AnsiTextParser {
    private const char Escape = '\x1b';

    public static IReadOnlyList<AnsiTextSpan> Parse(string input) {
        var spans = new List<AnsiTextSpan>();
        if (string.IsNullOrEmpty(input)) return spans;

        var style = AnsiStyle.Default;
        var text = new StringBuilder();
        var i = 0;
        while (i < input.Length) {
            var ch = input[i];
            if (ch == Escape && i + 1 < input.Length && input[i + 1] == '[') {
                var end = i + 2;
                while (end < input.Length && !IsFinalByte(input[end])) end++;
                if (end >= input.Length) {
                    // Escape sequence truncated by the end of this chunk (can happen when output
                    // is captured/progress-reported mid-stream). Drop it rather than show raw bytes.
                    break;
                }
                var parameters = input[(i + 2)..end];
                var final = input[end];
                if (final == 'm') {
                    if (text.Length > 0) {
                        spans.Add(new AnsiTextSpan(text.ToString(), style));
                        text.Clear();
                    }
                    style = ApplySgr(style, parameters);
                }
                // Any other final byte (cursor movement, clear-line, etc.) is discarded unchanged;
                // only SGR (color/style) sequences are interpreted.
                i = end + 1;
                continue;
            }
            text.Append(ch);
            i++;
        }
        if (text.Length > 0) spans.Add(new AnsiTextSpan(text.ToString(), style));
        return spans;
    }

    private static bool IsFinalByte(char c) => c is >= '@' and <= '~';

    private static AnsiStyle ApplySgr(AnsiStyle style, string parameters) {
        if (parameters.Length == 0) return AnsiStyle.Default;
        var codes = parameters.Split(';');
        for (var index = 0; index < codes.Length; index++) {
            if (!int.TryParse(codes[index], out var code)) continue;
            switch (code) {
                case 0: style = AnsiStyle.Default; break;
                case 1: style = style with { Bold = true }; break;
                case 22: style = style with { Bold = false }; break;
                case 4: style = style with { Underline = true }; break;
                case 24: style = style with { Underline = false }; break;
                case 39: style = style with { Foreground = null, ForegroundRgb = null }; break;
                case 49: style = style with { Background = null, BackgroundRgb = null }; break;
                case >= 30 and <= 37: style = style with { Foreground = (AnsiColor)(code - 30), ForegroundRgb = null }; break;
                case >= 40 and <= 47: style = style with { Background = (AnsiColor)(code - 40), BackgroundRgb = null }; break;
                case >= 90 and <= 97: style = style with { Foreground = (AnsiColor)(code - 90 + 8), ForegroundRgb = null }; break;
                case >= 100 and <= 107: style = style with { Background = (AnsiColor)(code - 100 + 8), BackgroundRgb = null }; break;
                case 38: index = ConsumeExtendedColor(codes, index, isForeground: true, ref style); break;
                case 48: index = ConsumeExtendedColor(codes, index, isForeground: false, ref style); break;
                default: break; // Unsupported/unknown SGR codes (italic, blink, strikethrough, ...) are ignored.
            }
        }
        return style;
    }

    private static int ConsumeExtendedColor(string[] codes, int index, bool isForeground, ref AnsiStyle style) {
        if (index + 1 >= codes.Length || !int.TryParse(codes[index + 1], out var mode)) return index;
        if (mode == 2 && index + 4 < codes.Length &&
            byte.TryParse(codes[index + 2], out var r) &&
            byte.TryParse(codes[index + 3], out var g) &&
            byte.TryParse(codes[index + 4], out var b)) {
            var rgb = new AnsiRgbColor(r, g, b);
            style = isForeground
                ? style with { ForegroundRgb = rgb, Foreground = null }
                : style with { BackgroundRgb = rgb, Background = null };
            return index + 4;
        }
        if (mode == 5 && index + 2 < codes.Length) {
            // 256-color palette indices are not mapped to a specific color; skip the parameter
            // so the surrounding codes still parse correctly, but leave the current color as-is.
            return index + 2;
        }
        return index + 1;
    }
}
