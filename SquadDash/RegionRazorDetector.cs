using System;
using System.Text.RegularExpressions;

namespace SquadDash;

/// <summary>Structural signals only; expressions and isolated @ characters are not evidence.</summary>
internal static class RegionRazorDetector
{
    private const RegexOptions Options =
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking;

    // Consume comments and literals before looking for directives or actual opening tags.
    // This is a bounded lexical scan, not Razor validation or a source-file parser.
    private static readonly Regex Tokens = new(
        """
        <!--[\s\S]*?(?:-->|$)
        | @\*[\s\S]*?(?:\*@|$)
        | /\*[\s\S]*?(?:\*/|$)
        | //[^\r\n]*
        | "{3,}[\s\S]*?(?:"{3,}|$)
        | @"(?:""|[^"])*(?:"|$)
        | "(?:\\[\s\S]|[^"\\])*(?:"|$)
        | '(?:\\[\s\S]|[^'\\])*(?:'|$)
        | <(?<tag>[A-Za-z][\w:.-]*)(?<attributes>(?:[^"'<>]|"[^"]*"|'[^']*')*)(?:>|$)
        | (?m:^[ \t]*@(?<directive>
            page[ \t]+"
            | (?:code|functions)[ \t\r\n]*\{
            | inject[ \t]+[\w.<>?,\[\]]+[ \t]+[A-Za-z_]\w*[ \t]*\r?$
            | using[ \t]+(?:static[ \t]+)?[A-Za-z_][\w.]*(?:[ \t]*;)?[ \t]*\r?$
            | (?:model|inherits|implements|namespace|layout|typeparam|rendermode)
                [ \t]+[A-Za-z_][\w.<>?,\[\]]*[ \t]*\r?$
        ))
        """,
        Options | RegexOptions.IgnorePatternWhitespace);

    private static readonly Regex Attributes = new(
        """\s+(?<name>[^\s"'<>/=`]+)(?:\s*=\s*(?:"[^"]*"|'[^']*'|[^\s"'=<>`]+))?""",
        Options);

    private static readonly Regex DirectiveAttribute = new(
        """\A@(?:bind(?:-[A-Za-z_][\w.-]*)?(?::(?:event|format|culture|after|get|set))?|on[a-z]+(?::(?:preventDefault|stopPropagation))?|ref|key|attributes|rendermode)\z""",
        Options);

    internal static bool IsRazor(string sample)
    {
        var offset = 0;
        while (offset < sample.Length)
        {
            var token = Tokens.Match(sample, offset);
            if (!token.Success)
                return false;
            if (token.Groups["directive"].Success)
                return true;

            offset = token.Index + token.Length;
            if (!token.Groups["tag"].Success)
                continue;

            foreach (Match attribute in Attributes.Matches(token.Groups["attributes"].Value))
            {
                if (DirectiveAttribute.IsMatch(attribute.Groups["name"].Value))
                    return true;
            }

            var tag = token.Groups["tag"].Value;
            if (tag.Equals("script", StringComparison.OrdinalIgnoreCase) ||
                tag.Equals("style", StringComparison.OrdinalIgnoreCase))
            {
                var close = sample.IndexOf("</" + tag, offset, StringComparison.OrdinalIgnoreCase);
                offset = close < 0 ? sample.Length : close;
            }
        }
        return false;
    }
}
