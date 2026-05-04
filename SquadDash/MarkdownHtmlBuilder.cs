using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SquadDash;

internal static class MarkdownHtmlBuilder {
    private static readonly Regex InlineCodeRegex = new("`([^`]+)`", RegexOptions.Compiled);
    private static readonly Regex ImageRegex = new(@"!\[([^\]]*)\]\(([^)]+)\)", RegexOptions.Compiled);
    private static readonly Regex LinkRegex = new(@"\[([^\]]+)\]\(([^)]+)\)", RegexOptions.Compiled);
    private static readonly Regex BoldRegex = new(@"\*\*(.+?)\*\*", RegexOptions.Compiled);
    private static readonly Regex ItalicRegex = new(@"(?<!\*)\*(?!\s)(.+?)(?<!\s)\*(?!\*)", RegexOptions.Compiled);
    private static readonly Regex BareUrlRegex = new(@"https?://[^\s\[\]()'`""*_<>]+", RegexOptions.Compiled);

    public static string Build(string markdown, string title, string? filePath = null, bool isDark = false) {
        var body = BuildBody(markdown ?? string.Empty);
        var baseTag = string.Empty;
        if (!string.IsNullOrEmpty(filePath)) {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir)) {
                var normalized = dir.Replace('\\', '/').TrimEnd('/') + '/';
                baseTag = $"\n<base href=\"file:///{normalized}\" />";
            }
        }

        var bg          = isDark ? "#1e1c17"                  : "#fffdf9";
        var fg          = isDark ? "#d8c8b0"                  : "#31281f";
        var line        = isDark ? "rgba(216,200,176,0.14)"   : "rgba(49,40,31,0.14)";
        var lineStrong  = isDark ? "rgba(216,200,176,0.28)"   : "rgba(49,40,31,0.22)";
        var quote       = isDark ? "#27221a"                  : "#f7f2e9";
        var code        = isDark ? "#252018"                  : "#f6f1e9";
        var link        = isDark ? "#6890c8"                  : "#2d5ea8";
        var headingColor = isDark ? "#e5d5c0" : "#2a211a";
        var thHeaderBg  = isDark ? "rgba(216,200,176,0.07)"   : "rgba(49,40,31,0.05)";
        var prioHigh    = isDark ? "#FF5252"                  : "#DE3333";
        var prioMid     = isDark ? "#FFD740"                  : "#A06800";
        var prioLow     = isDark ? "#64B5F6"                  : "#1976D2";

        return $$"""
<!DOCTYPE html>
<html>
<head>
<meta http-equiv="X-UA-Compatible" content="IE=edge" />{{baseTag}}
<meta charset="utf-8">
<title>{{EscapeHtml(title)}}</title>
<style>
  body {
    margin: 0;
    padding: 26px;
    background: {{bg}};
    color: {{fg}};
    font: 15px/1.55 "Segoe UI", sans-serif;
  }
  h1,h2,h3,h4 { margin: 1.2em 0 0.45em; color: {{headingColor}}; }
  h1:first-child,h2:first-child,h3:first-child,h4:first-child { margin-top: 0; }
  h1 { font-size: 1.75rem; }
  h2 { font-size: 1.4rem; }
  h3 { font-size: 1.15rem; }
  p { margin: 0 0 0.9em; }
  blockquote {
    margin: 0 0 1em;
    padding: 0.8em 1em;
    background: {{quote}};
    border-left: 3px solid {{lineStrong}};
    border-radius: 10px;
  }
  pre {
    margin: 0 0 1em;
    padding: 0.9em 1em;
    background: {{code}};
    border: 1px solid {{line}};
    border-radius: 10px;
    overflow: auto;
    font: 13px/1.45 Consolas, monospace;
  }
  code {
    background: {{code}};
    border-radius: 4px;
    padding: 0.08em 0.32em;
    font: 0.95em Consolas, monospace;
  }
  pre > code {
    background: transparent;
    border-radius: 0;
    padding: 0;
    font: inherit;
  }
  ul {
    margin: 0 0 1em 1.2em;
    padding: 0;
  }
  li { margin: 0.18em 0; }
  hr {
    border: none;
    border-top: 1px solid {{line}};
    margin: 1em 0 1.15em;
  }
  table {
    width: auto;
    border-collapse: collapse;
    margin: 0 0 1em;
  }
  th, td {
    border: 1px solid {{line}};
    padding: 0.45em 0.6em;
    vertical-align: top;
    text-align: left;
    white-space: nowrap;
  }
  th {
    background: {{thHeaderBg}};
    font-weight: 600;
  }
  a { color: {{link}}; text-decoration: none; }
  a:hover { text-decoration: underline; }
  a[href] { cursor: pointer; }
  /* Ensure transparent-corner images never show a black host-window background. */
  html { background: {{bg}}; }
  img { background-color: {{(isDark ? "#000000" : bg)}}; }
  /* Themed scrollbar — webkit, Firefox, IE */
  html, body {
    scrollbar-width: thin;
    scrollbar-color: {{(isDark ? "#555 #2a2a2a" : "#aaa #f0f0f0")}};
    {{(isDark
        ? "scrollbar-base-color:#555;scrollbar-face-color:#555;scrollbar-track-color:#2a2a2a;scrollbar-arrow-color:#777;scrollbar-highlight-color:#2a2a2a;scrollbar-shadow-color:#333;"
        : "scrollbar-base-color:#aaa;scrollbar-face-color:#aaa;scrollbar-track-color:#f0f0f0;scrollbar-arrow-color:#888;scrollbar-highlight-color:#f0f0f0;scrollbar-shadow-color:#ccc;"
    )}}
  }
  pre {
    scrollbar-width: thin;
    scrollbar-color: {{(isDark ? "#555 #2a2a2a" : "#aaa #f0f0f0")}};
  }
  ::-webkit-scrollbar {
    width: 11px;
    height: 11px;
  }
  ::-webkit-scrollbar-track {
    background: {{(isDark ? "#2a2a2a" : "#f0f0f0")}};
    border-radius: 6px;
  }
  ::-webkit-scrollbar-thumb {
    background: {{(isDark ? "#555" : "#aaa")}};
    border-radius: 6px;
  }
  ::-webkit-scrollbar-thumb:hover {
    background: {{(isDark ? "#777" : "#888")}};
  }
  ::-webkit-scrollbar-thumb:active {
    background: {{(isDark ? "#999" : "#666")}};
  }
  /* ── Priority circle dots (emoji replaced with <span class="pdot">) */
  .pdot {
    display: inline-block;
    width: 0.75em; height: 0.75em;
    border-radius: 50%;
    vertical-align: middle;
    margin: 0 3px 1px 0;
  }
  .pdot-high { background: {{prioHigh}}; }
  .pdot-mid  { background: {{prioMid}}; }
  .pdot-low  { background: {{prioLow}}; }
</style>
</head>
<body>
{{body}}
<script>
// Suppress IE script error dialogs — errors in our injected scripts are handled gracefully.
window.onerror = function() { return true; };
document.addEventListener('click', function(e) {
  var a = e.target;
  while (a && a.tagName !== 'A') a = a.parentElement;
  if (a && a.href) {
    e.preventDefault();
    e.stopPropagation();
    try { window.external.Navigate(a.href); } catch(ex) {}
  }
});
</script>
<script>
(function() {
  // ── 📸 placeholder blockquotes: right-click to paste screenshot ──────────
  var bqList = document.querySelectorAll('blockquote');
  for (var i = 0; i < bqList.length; i++) { (function(bq) {
    var text = bq.textContent || '';
    if (text.indexOf('\uD83D\uDCF8') === -1 && text.indexOf('Screenshot needed') === -1) return;
    var imgSrc = '';
    var prev = bq.previousElementSibling;
    if (prev) {
      var img = prev.tagName === 'IMG' ? prev : prev.querySelector('img');
      if (img) imgSrc = img.getAttribute('src') || '';
    }
    bq.style.cursor = 'context-menu';
    bq.oncontextmenu = function(e) {
      if (e && e.preventDefault) e.preventDefault();
      if (e) e.cancelBubble = true;
      var src = imgSrc;
      window.setTimeout(function() {
        try { window.external.ShowScreenshotMenu(src); } catch(ex) {}
      }, 0);
      return false;
    };
  })(bqList[i]); }

  // ── Existing images: right-click to replace from clipboard ───────────────
  var imgBg = '{{(isDark ? "#000000" : bg)}}';
  var imgList = document.querySelectorAll('img');
  for (var j = 0; j < imgList.length; j++) { (function(img) {
    img.style.backgroundColor = imgBg;
    img.style.cursor = 'context-menu';
    img.oncontextmenu = function(e) {
      if (e && e.preventDefault) e.preventDefault();
      if (e) e.cancelBubble = true;
      var src = img.getAttribute('src') || '';
      window.setTimeout(function() {
        try { window.external.ShowImageMenu(src); } catch(ex) {}
      }, 0);
      return false;
    };
  })(imgList[j]); }
})();
</script>
</body>
</html>
""";
    }

    private static string BuildBody(string markdown) {
        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var builder = new StringBuilder();
        string? currentPrioClass = null; // tracks active priority section

        for (var index = 0; index < lines.Length; index++) {
            var lineNum = index + 1; // 1-based line numbers
            var trimmed = lines[index].Trim();
            if (trimmed.Length == 0)
                continue;

            if (trimmed.StartsWith("```", StringComparison.Ordinal)) {
                var code = new StringBuilder();
                var startLine = lineNum;
                index++;
                while (index < lines.Length && !lines[index].TrimStart().StartsWith("```", StringComparison.Ordinal)) {
                    if (code.Length > 0)
                        code.Append('\n');
                    code.Append(lines[index]);
                    index++;
                }

                builder.Append($"<pre data-source-line=\"{startLine}\"><code>")
                    .Append(EscapeHtml(code.ToString()))
                    .AppendLine("</code></pre>");
                continue;
            }

            if (TryReadTable(lines, ref index, out var rows)) {
                builder.AppendLine(BuildTable(rows, lineNum));
                continue;
            }

            if (IsHorizontalRule(trimmed)) {
                builder.AppendLine($"<hr data-source-line=\"{lineNum}\" />");
                continue;
            }

            if (trimmed.StartsWith("#", StringComparison.Ordinal)) {
                var level = Math.Clamp(trimmed.TakeWhile(character => character == '#').Count(), 1, 4);
                var headingText = trimmed[level..].Trim();

                // Detect priority headings (## 🔴/🟡/🟢 ...)
                if (level == 2) {
                    if (headingText.Contains("🔴"))      currentPrioClass = "prio-high";
                    else if (headingText.Contains("🟡")) currentPrioClass = "prio-mid";
                    else if (headingText.Contains("🟢")) currentPrioClass = "prio-low";
                    else                                  currentPrioClass = null;
                } else if (level < 2) {
                    currentPrioClass = null;
                }

                var classAttr = currentPrioClass is not null && level == 2
                    ? $" class=\"{currentPrioClass}\""
                    : string.Empty;
                builder.Append('<').Append('h').Append(level).Append($"{classAttr} data-source-line=\"{lineNum}\">")
                    .Append(RenderInline(headingText))
                    .Append("</h").Append(level).AppendLine(">");
                continue;
            }

            if (trimmed.StartsWith("> ", StringComparison.Ordinal)) {
                builder.Append($"<blockquote data-source-line=\"{lineNum}\"><p>")
                    .Append(RenderInline(trimmed[2..].Trim()))
                    .AppendLine("</p></blockquote>");
                continue;
            }

            if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal)) {
                var ulClass = currentPrioClass is not null ? $" class=\"{currentPrioClass}-list\"" : string.Empty;
                builder.AppendLine($"<ul{ulClass} data-source-line=\"{lineNum}\">");
                while (index < lines.Length) {
                    var listLine = lines[index].Trim();
                    if (!listLine.StartsWith("- ", StringComparison.Ordinal) &&
                        !listLine.StartsWith("* ", StringComparison.Ordinal)) {
                        index--;
                        break;
                    }

                    var itemLineNum = index + 1; // 1-based line number for this specific item
                    builder.Append($"<li data-source-line=\"{itemLineNum}\">")
                        .Append(RenderInline(listLine[2..].Trim()))
                        .AppendLine("</li>");
                    index++;
                }
                builder.AppendLine("</ul>");
                continue;
            }

            if (IsOrderedListItem(trimmed, out var olText)) {
                builder.AppendLine($"<ol data-source-line=\"{lineNum}\">");
                while (index < lines.Length) {
                    var listLine = lines[index].Trim();
                    if (!IsOrderedListItem(listLine, out var itemText)) {
                        index--;
                        break;
                    }
                    var itemLineNum = index + 1; // 1-based line number for this specific item
                    builder.Append($"<li data-source-line=\"{itemLineNum}\">")
                        .Append(RenderInline(itemText))
                        .AppendLine("</li>");
                    index++;
                }
                builder.AppendLine("</ol>");
                continue;
            }

            // Standalone image line: ![alt](src)
            var imgMatch = ImageRegex.Match(trimmed);
            if (imgMatch.Success && imgMatch.Index == 0 && imgMatch.Length == trimmed.Length) {
                var alt = EscapeHtml(imgMatch.Groups[1].Value);
                var src = EscapeHtml(imgMatch.Groups[2].Value);
                builder.AppendLine($"<p data-source-line=\"{lineNum}\"><img src=\"{src}\" alt=\"{alt}\" style=\"max-width:100%;\" /></p>");
                continue;
            }

            var paragraphLines = new List<string> { trimmed };
            while (index + 1 < lines.Length) {
                var next = lines[index + 1].Trim();
                if (next.Length == 0 ||
                    next.StartsWith("#", StringComparison.Ordinal) ||
                    next.StartsWith("> ", StringComparison.Ordinal) ||
                    next.StartsWith("- ", StringComparison.Ordinal) ||
                    next.StartsWith("* ", StringComparison.Ordinal) ||
                    next.StartsWith("```", StringComparison.Ordinal) ||
                    IsHorizontalRule(next) ||
                    (IsTableRow(next) && index + 2 < lines.Length && IsTableSeparator(lines[index + 2]))) {
                    break;
                }

                paragraphLines.Add(next);
                index++;
            }

            builder.Append($"<p data-source-line=\"{lineNum}\">")
                .Append(RenderInline(string.Join(" ", paragraphLines)))
                .AppendLine("</p>");
        }

        return builder.ToString();
    }

    private static string RenderInline(string text) {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        // ── Phase 0: extract bare URLs before HTML escaping ──────────────────
        // URLs that are already inside [text](url) or ![alt](url) are left alone
        // so the LinkRegex/ImageRegex steps can handle them normally.
        var bareUrlPlaceholders = new Dictionary<string, string>();
        var bareUrlIndex = 0;
        var withUrlPlaceholders = BareUrlRegex.Replace(text, m => {
            // Skip if preceded by "](" — this URL is the target of a markdown link/image.
            if (m.Index >= 2 && text[m.Index - 1] == '(' && text[m.Index - 2] == ']')
                return m.Value;
            // Strip common trailing punctuation that follows URLs in prose.
            var url = m.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')', '*', '_');
            var key = $"@@BAREURL{bareUrlIndex++}@@";
            bareUrlPlaceholders[key] = url;
            return key + m.Value[url.Length..]; // re-append any stripped chars
        });

        var escaped = EscapeHtml(withUrlPlaceholders);

        // Replace priority circle emoji with themed HTML dots before other inline processing.
        escaped = escaped
            .Replace("🔴", "<span class=\"pdot pdot-high\"></span>")
            .Replace("🟡", "<span class=\"pdot pdot-mid\"></span>")
            .Replace("🟢", "<span class=\"pdot pdot-low\"></span>");

        var codePlaceholders = new Dictionary<string, string>();
        var placeholderIndex = 0;

        escaped = InlineCodeRegex.Replace(escaped, match => {
            var key = $"@@CODE{placeholderIndex++}@@";
            codePlaceholders[key] = $"<code>{match.Groups[1].Value}</code>";
            return key;
        });

        // Images before links so the ![alt](src) pattern isn't consumed by LinkRegex.
        escaped = ImageRegex.Replace(escaped, "<img src=\"$2\" alt=\"$1\" style=\"max-width:100%;\" />");
        escaped = LinkRegex.Replace(escaped, "<a href=\"$2\">$1</a>");
        escaped = BoldRegex.Replace(escaped, "<strong>$1</strong>");
        escaped = ItalicRegex.Replace(escaped, "<em>$1</em>");

        foreach (var pair in codePlaceholders)
            escaped = escaped.Replace(pair.Key, pair.Value, StringComparison.Ordinal);

        // ── Phase last: restore bare URL placeholders as anchor tags ─────────
        foreach (var pair in bareUrlPlaceholders) {
            var escapedUrl = EscapeHtml(pair.Value);
            escaped = escaped.Replace(pair.Key, $"<a href=\"{escapedUrl}\">{escapedUrl}</a>", StringComparison.Ordinal);
        }

        return escaped;
    }

    private static string BuildTable(IReadOnlyList<string[]> rows, int lineNum) {
        var builder = new StringBuilder();
        builder.AppendLine($"<table data-source-line=\"{lineNum}\">");

        if (rows.Count > 0) {
            // Header row is at lineNum; the separator row (lineNum+1) is not emitted.
            builder.AppendLine($"<thead><tr data-source-line=\"{lineNum}\">");
            foreach (var cell in rows[0])
                builder.Append("<th>").Append(RenderInline(cell)).AppendLine("</th>");
            builder.AppendLine("</tr></thead>");
        }

        if (rows.Count > 1) {
            builder.AppendLine("<tbody>");
            for (var rowIndex = 1; rowIndex < rows.Count; rowIndex++) {
                // rows[0]=header@lineNum, separator@lineNum+1, rows[1]@lineNum+2, rows[k]@lineNum+1+k
                var rowLineNum = lineNum + 1 + rowIndex;
                builder.AppendLine($"<tr data-source-line=\"{rowLineNum}\">");
                foreach (var cell in rows[rowIndex])
                    builder.Append("<td>").Append(RenderInline(cell)).AppendLine("</td>");
                builder.AppendLine("</tr>");
            }
            builder.AppendLine("</tbody>");
        }

        builder.AppendLine("</table>");
        return builder.ToString();
    }

    private static bool TryReadTable(string[] lines, ref int index, out List<string[]> rows) {
        rows = [];
        if (!IsTableRow(lines[index]) || index + 1 >= lines.Length || !IsTableSeparator(lines[index + 1]))
            return false;

        rows.Add(ParseTableRow(lines[index]));
        index++;

        while (index + 1 < lines.Length && IsTableRow(lines[index + 1])) {
            rows.Add(ParseTableRow(lines[index + 1]));
            index++;
        }

        return rows.Count > 0;
    }

    private static bool IsTableRow(string line) {
        var trimmed = line.Trim();
        return trimmed.StartsWith("|", StringComparison.Ordinal) &&
               trimmed.EndsWith("|", StringComparison.Ordinal) &&
               trimmed.Count(character => character == '|') >= 2;
    }

    private static bool IsTableSeparator(string line) {
        if (!IsTableRow(line))
            return false;

        return ParseTableRow(line)
            .All(cell => cell.Length > 0 && cell.All(character => character is '-' or ':' or ' '));
    }

    private static string[] ParseTableRow(string line) {
        return line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToArray();
    }

    private static bool IsHorizontalRule(string line) {
        return line.Length >= 3 && line.All(character => character is '-' or '_' or '*');
    }

    private static bool IsOrderedListItem(string line, out string text) {
        var dotIdx = line.IndexOf(". ", StringComparison.Ordinal);
        if (dotIdx > 0 && line[..dotIdx].All(char.IsDigit)) {
            text = line[(dotIdx + 2)..].Trim();
            return true;
        }
        text = string.Empty;
        return false;
    }

    private static string EscapeHtml(string text) {
        return text
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }
}
