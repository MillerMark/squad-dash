using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace SquadDash;

/// <summary>
/// Fresh, document-ordered projection shared by rendered copy/export and find. Never cache a
/// projection across VisibilityChanged. TextPointers belong to their nested document, not the root.
/// </summary>
internal static class MarkdownVisibleContent
{
    internal sealed class DocumentState
    {
        internal IReadOnlyList<string> Diagnostics { get; set; } = [];
        internal event Action? VisibilityChanged;
        internal void Notify() => VisibilityChanged?.Invoke();
        internal void Clear() => VisibilityChanged = null;
    }

    private static readonly ConditionalWeakTable<FlowDocument, DocumentState> States = new();
    internal static DocumentState State(FlowDocument document) => States.GetOrCreateValue(document);

    internal sealed record Part(string Text, FlowDocument Document, TextPointer Start, TextPointer End,
        FrameworkElement? Control = null, IReadOnlyList<TextPointer>? Positions = null);

    internal sealed record Match(Part Part, int Offset, int Length)
    {
        internal void Select(FlowDocumentScrollViewer root)
        {
            if (!Documents(root.Document).Any(entry => ReferenceEquals(entry.Document, Part.Document)))
                return;
            if (Part.Control is TextBox code)
            {
                code.Focus();
                code.Select(Offset, Length);
                code.BringIntoView();
            }
            else if (Part.Control is Expander header)
            {
                header.Focus();
                header.BringIntoView();
            }
            else if (Part.Control is TextBlock cell)
            {
                cell.BringIntoView();
            }
            else if (Part.Positions is { } positions)
            {
                var viewer = ReferenceEquals(root.Document, Part.Document) ? root
                    : Documents(root.Document).First(item => ReferenceEquals(item.Document, Part.Document)).Viewer!;
                viewer.ApplyTemplate();
                viewer.Focus();
                viewer.Selection?.Select(positions[Offset], positions[Offset + Length - 1].GetPositionAtOffset(1)!);
                positions[Offset].Paragraph?.BringIntoView();
            }
        }
    }

    internal static IEnumerable<(FlowDocument Document, FlowDocumentScrollViewer? Viewer)> Documents(
        FlowDocument document, bool visibleOnly = true)
    {
        yield return (document, null);
        foreach (var detail in Details(document))
        {
            if (visibleOnly && !detail.Expander.IsExpanded)
                continue;
            yield return (detail.Viewer.Document, detail.Viewer);
            foreach (var nested in Documents(detail.Viewer.Document, visibleOnly).Skip(1))
                yield return nested;
        }
    }

    internal static IEnumerable<MarkdownDetailView> Details(FlowDocument document) =>
        Blocks(document.Blocks).OfType<BlockUIContainer>().Select(block => block.Tag).OfType<MarkdownDetailView>();

    internal static IEnumerable<Block> Blocks(BlockCollection blocks)
    {
        foreach (var block in blocks)
        {
            yield return block;
            IEnumerable<Block> children = block switch
            {
                Section section => Blocks(section.Blocks),
                List list => list.ListItems.SelectMany(item => Blocks(item.Blocks)),
                Table table => table.RowGroups.SelectMany(group => group.Rows)
                    .SelectMany(row => row.Cells).SelectMany(cell => Blocks(cell.Blocks)),
                _ => [],
            };
            foreach (var child in children)
                yield return child;
        }
    }

    internal static IEnumerable<Part> Project(FlowDocument document)
    {
        foreach (var block in Blocks(document.Blocks))
        {
            if (block is Paragraph paragraph)
            {
                var text = new StringBuilder();
                var positions = new List<TextPointer>();
                var current = paragraph.ContentStart;
                while (current is not null && current.CompareTo(paragraph.ContentEnd) < 0)
                {
                    if (current.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                    {
                        var run = current.GetTextInRun(LogicalDirection.Forward);
                        text.Append(run);
                        for (var offset = 0; offset < run.Length; offset++)
                            positions.Add(current.GetPositionAtOffset(offset)!);
                    }
                    else if (current.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.ElementStart &&
                             current.GetAdjacentElement(LogicalDirection.Forward) is LineBreak)
                    {
                        text.Append('\n');
                        positions.Add(current);
                    }
                    else if (current.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.EmbeddedElement &&
                             current.GetAdjacentElement(LogicalDirection.Forward) is CheckBox check)
                    {
                        var label = System.Windows.Automation.AutomationProperties.GetName(check);
                        var value = $"{(check.IsChecked == true ? "[x]" : "[ ]")} {label}";
                        text.Append(value);
                        positions.AddRange(Enumerable.Repeat(current, value.Length));
                    }
                    current = current.GetNextContextPosition(LogicalDirection.Forward);
                }
                yield return new(text.ToString(), document, paragraph.ContentStart, paragraph.ContentEnd, Positions: positions);
            }
            else if (block is BlockUIContainer { Tag: MarkdownDetailView detail } container)
            {
                yield return new(detail.Label, document, container.ElementStart, container.ElementEnd, detail.Expander);
                if (detail.Expander.IsExpanded)
                    foreach (var part in Project(detail.Viewer.Document))
                        yield return part;
            }
            else if (block is BlockUIContainer ui)
            {
                foreach (var code in CodeBoxes(ui.Child))
                    yield return new(code.Text, document, ui.ElementStart, ui.ElementEnd, code);
                foreach (var text in TextBlocks(ui.Child))
                    yield return new(new TextRange(text.ContentStart, text.ContentEnd).Text,
                        document, ui.ElementStart, ui.ElementEnd, text);
            }
        }
    }

    internal static string GetText(FlowDocument document) =>
        string.Join(Environment.NewLine, Project(document).Select(part => part.Text));

    internal static Match? Find(FlowDocument document, string text) => FindAll(document, text).FirstOrDefault();

    internal static IEnumerable<Match> FindAll(FlowDocument document, string text)
    {
        if (string.IsNullOrEmpty(text))
            yield break;
        foreach (var part in Project(document))
        {
            var offset = 0;
            while (offset < part.Text.Length)
            {
                var index = part.Text.IndexOf(text, offset, StringComparison.OrdinalIgnoreCase);
                if (index < 0) break;
                yield return new(part, index, text.Length);
                offset = index + text.Length;
            }
        }
    }

    internal static string GetSelectionText(FlowDocument root, TextRange selection)
    {
        if (selection.IsEmpty)
            return "";
        foreach (var (document, _) in Documents(root))
        {
            if (!selection.Start.IsInSameDocument(document.ContentStart))
                continue;
            var parts = new List<string>();
            var projected = Project(document).Where(part => ReferenceEquals(part.Document, document)).ToArray();
            foreach (var block in Blocks(document.Blocks))
            {
                if (block is not (Paragraph or BlockUIContainer) ||
                    block.ElementEnd.CompareTo(selection.Start) <= 0 ||
                    block.ElementStart.CompareTo(selection.End) >= 0)
                    continue;
                if (block is BlockUIContainer { Tag: MarkdownDetailView detail })
                {
                    parts.Add(detail.Label);
                    if (detail.Expander.IsExpanded)
                        parts.Add(GetText(detail.Viewer.Document));
                }
                else
                {
                    foreach (var part in projected.Where(part =>
                                 part.Start.CompareTo(block.ElementStart) >= 0 && part.End.CompareTo(block.ElementEnd) <= 0))
                    {
                        if (part.Positions is { } positions)
                            parts.Add(new string(part.Text.Where((_, i) => positions[i].CompareTo(selection.Start) >= 0 &&
                                positions[i].CompareTo(selection.End) < 0).ToArray()));
                        else
                            parts.Add(part.Text);
                    }
                }
            }
            return string.Join(Environment.NewLine, parts);
        }
        return "";
    }

    internal static string GetSelectedText(FlowDocumentScrollViewer root)
    {
        var documents = Documents(root.Document).ToArray();
        foreach (var (document, viewer) in documents.Reverse())
        {
            foreach (var code in Project(document).Select(part => part.Control).OfType<TextBox>())
                if (code.SelectionLength > 0)
                    return code.SelectedText;
            if (viewer?.Selection is { IsEmpty: false } nestedSelection)
                return GetSelectionText(root.Document, nestedSelection);
        }
        return root.Selection is { } selection ? GetSelectionText(root.Document, selection) : "";
    }

    internal static void ClearSelections(FlowDocument document)
    {
        foreach (var (child, viewer) in Documents(document, visibleOnly: false))
        {
            viewer?.Selection?.Select(child.ContentStart, child.ContentStart);
            foreach (var block in Blocks(child.Blocks).OfType<BlockUIContainer>())
                foreach (var code in CodeBoxes(block.Child))
                    code.Select(0, 0);
        }
    }

    internal static void Invalidate(FlowDocument document)
    {
        ClearSelections(document);
        State(document).Notify();
    }

    internal static void Detach(FlowDocument document)
    {
        foreach (var (child, _) in Documents(document, visibleOnly: false).ToArray())
        {
            foreach (var detail in Details(child))
                detail.Dispose();
            State(child).Clear();
        }
    }

    /// <summary>Call after composing/moving shared-builder blocks into a host document.</summary>
    internal static void Bind(FlowDocument document)
    {
        foreach (var (child, _) in Documents(document, visibleOnly: false))
        {
            foreach (var detail in Details(child))
                detail.BindRoot(document);
            foreach (var block in Blocks(child.Blocks).OfType<BlockUIContainer>())
                foreach (var code in CodeBoxes(block.Child))
                    CopyableCodeBlockBuilder.SetVisibleTextProvider(code, () =>
                        Project(document).FirstOrDefault(part => ReferenceEquals(part.Control, code))?.Text ?? "");
        }
    }

    internal static IDisposable TrackSelections(FlowDocumentScrollViewer root) => new SelectionScope(root);

    private sealed class SelectionScope : IDisposable
    {
        private readonly List<Action> _detach = [];
        private readonly List<(object Owner, Action Clear)> _owners = [];
        private bool _changing;

        internal SelectionScope(FlowDocumentScrollViewer root)
        {
            foreach (var (document, nested) in Documents(root.Document, visibleOnly: false))
            {
                var viewer = nested ?? root;
                var attached = false;
                void AttachSelection()
                {
                    if (attached || viewer.Selection is not { } selection) return;
                    attached = true;
                    EventHandler changed = (_, _) =>
                    {
                        if (!selection.IsEmpty) Activate(viewer);
                    };
                    selection.Changed += changed;
                    _detach.Add(() => selection.Changed -= changed);
                    _owners.Add((viewer, () => selection.Select(document.ContentStart, document.ContentStart)));
                }
                viewer.ApplyTemplate();
                AttachSelection();
                RoutedEventHandler loaded = (_, _) => AttachSelection();
                viewer.Loaded += loaded;
                _detach.Add(() => viewer.Loaded -= loaded);
                foreach (var block in Blocks(document.Blocks).OfType<BlockUIContainer>())
                foreach (var code in CodeBoxes(block.Child))
                {
                    RoutedEventHandler codeChanged = (_, _) =>
                    {
                        if (code.SelectionLength > 0) Activate(code);
                    };
                    code.SelectionChanged += codeChanged;
                    _detach.Add(() => code.SelectionChanged -= codeChanged);
                    _owners.Add((code, () => code.Select(0, 0)));
                }
            }
        }

        private void Activate(object owner)
        {
            if (_changing) return;
            _changing = true;
            try
            {
                foreach (var entry in _owners)
                    if (!ReferenceEquals(entry.Owner, owner)) entry.Clear();
            }
            finally { _changing = false; }
        }

        public void Dispose()
        {
            foreach (var detach in _detach) detach();
            _detach.Clear();
            _owners.Clear();
        }
    }

    internal static IEnumerable<TextBlock> TextBlocks(DependencyObject? element)
    {
        if (element is TextBlock text)
            yield return text;
        else if (element is Panel panel)
            foreach (UIElement child in panel.Children)
                foreach (var result in TextBlocks(child))
                    yield return result;
        else if (element is Decorator decorator)
            foreach (var result in TextBlocks(decorator.Child))
                yield return result;
    }

    private static IEnumerable<TextBox> CodeBoxes(DependencyObject? element)
    {
        if (element is TextBox { Tag: "codeblock" } code)
            yield return code;
        else if (element is Panel panel)
            foreach (UIElement child in panel.Children)
                foreach (var result in CodeBoxes(child))
                    yield return result;
        else if (element is Decorator decorator)
            foreach (var result in CodeBoxes(decorator.Child))
                yield return result;
    }
}
