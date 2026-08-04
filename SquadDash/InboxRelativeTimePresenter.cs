using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;

namespace SquadDash;

/// <summary>Turns durable UTC time markers into live relative-time runs in an open Inbox message.</summary>
internal static class InboxRelativeTimePresenter
{
    private static readonly Regex Marker = new(
        @"\{\{utc-time:(?<value>[^}]+)\}\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static string Encode(DateTimeOffset timestamp) =>
        $"{{{{utc-time:{timestamp.ToUniversalTime():O}}}}}";

    internal static DispatcherTimer? Attach(FlowDocument document)
    {
        var bindings = new List<(Run Run, string Template, DateTimeOffset Timestamp)>();
        foreach (var run in EnumerateRuns(document))
        {
            var match = Marker.Match(run.Text ?? string.Empty);
            if (!match.Success || !DateTimeOffset.TryParse(match.Groups["value"].Value, out var timestamp))
                continue;
            bindings.Add((run, run.Text ?? string.Empty, timestamp));
            run.Cursor = Cursors.Help;
            run.TextDecorations = TextDecorations.Underline;
            run.SetResourceReference(TextElement.ForegroundProperty, "DocumentLinkText");
            run.ToolTip = timestamp.ToUniversalTime().ToString("O");
        }
        if (bindings.Count == 0) return null;

        void Refresh()
        {
            foreach (var (run, template, timestamp) in bindings)
                run.Text = Marker.Replace(template, StatusTimingPresentation.FormatRelativeTimestamp(timestamp));
        }

        Refresh();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        timer.Tick += (_, _) => Refresh();
        timer.Start();
        return timer;
    }

    private static IEnumerable<Run> EnumerateRuns(FlowDocument document)
    {
        foreach (var block in document.Blocks)
            foreach (var run in EnumerateRuns(block))
                yield return run;
    }

    private static IEnumerable<Run> EnumerateRuns(Block block)
    {
        switch (block)
        {
            case Paragraph paragraph:
                foreach (var inline in paragraph.Inlines)
                    foreach (var run in EnumerateRuns(inline)) yield return run;
                break;
            case Section section:
                foreach (var child in section.Blocks)
                    foreach (var run in EnumerateRuns(child)) yield return run;
                break;
            case List list:
                foreach (var item in list.ListItems)
                    foreach (var child in item.Blocks)
                        foreach (var run in EnumerateRuns(child)) yield return run;
                break;
            case Table table:
                foreach (var rowGroup in table.RowGroups)
                    foreach (var row in rowGroup.Rows)
                        foreach (var cell in row.Cells)
                            foreach (var child in cell.Blocks)
                                foreach (var run in EnumerateRuns(child)) yield return run;
                break;
        }
    }

    private static IEnumerable<Run> EnumerateRuns(Inline inline)
    {
        if (inline is Run run) yield return run;
        if (inline is Span span)
            foreach (var child in span.Inlines)
                foreach (var nested in EnumerateRuns(child)) yield return nested;
    }
}

/// <summary>
/// Converts exact commit hashes emitted as Markdown code spans into host-owned commit links.
/// Requiring the complete run to be a hash avoids turning plan IDs, dates, and ordinary numbers
/// into links.
/// </summary>
internal static class InboxCommitLinkPresenter
{
    private static readonly Regex CommitHash = new(
        @"^[0-9a-fA-F]{7,40}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static void Attach(FlowDocument document, Action<string>? openCommit)
    {
        if (openCommit is null) return;

        foreach (var run in EnumerateRuns(document).ToArray())
        {
            var value = run.Text?.Trim();
            if (string.IsNullOrWhiteSpace(value) || !CommitHash.IsMatch(value)) continue;
            if (run.FontFamily?.Source.Contains("Consolas", StringComparison.OrdinalIgnoreCase) != true) continue;

            var link = new Hyperlink(new Run(value))
            {
                Cursor = Cursors.Hand,
                ToolTip = ToolTipHelper.MakeThemedToolTip("Open this commit in SquadDash"),
            };
            link.SetResourceReference(TextElement.ForegroundProperty, "DocumentLinkText");
            var captured = value;
            link.Click += (_, _) => openCommit(captured);

            switch (run.Parent)
            {
                case Paragraph paragraph:
                    paragraph.Inlines.InsertBefore(run, link);
                    paragraph.Inlines.Remove(run);
                    break;
                case Span span when span is not Hyperlink:
                    span.Inlines.InsertBefore(run, link);
                    span.Inlines.Remove(run);
                    break;
            }
        }
    }

    private static IEnumerable<Run> EnumerateRuns(FlowDocument document)
    {
        foreach (var block in document.Blocks)
            foreach (var run in EnumerateRuns(block))
                yield return run;
    }

    private static IEnumerable<Run> EnumerateRuns(Block block)
    {
        switch (block)
        {
            case Paragraph paragraph:
                foreach (var inline in paragraph.Inlines)
                    foreach (var run in EnumerateRuns(inline)) yield return run;
                break;
            case Section section:
                foreach (var child in section.Blocks)
                    foreach (var run in EnumerateRuns(child)) yield return run;
                break;
            case List list:
                foreach (var item in list.ListItems)
                    foreach (var child in item.Blocks)
                        foreach (var run in EnumerateRuns(child)) yield return run;
                break;
            case Table table:
                foreach (var rowGroup in table.RowGroups)
                    foreach (var row in rowGroup.Rows)
                        foreach (var cell in row.Cells)
                            foreach (var child in cell.Blocks)
                                foreach (var run in EnumerateRuns(child)) yield return run;
                break;
        }
    }

    private static IEnumerable<Run> EnumerateRuns(Inline inline)
    {
        if (inline is Run run) yield return run;
        if (inline is Span span)
            foreach (var child in span.Inlines)
                foreach (var nested in EnumerateRuns(child)) yield return nested;
    }
}
