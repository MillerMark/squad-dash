using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;

namespace SquadDash;

internal static class ReadOnlyHighlightedEditorFactory
{
    private static readonly ConditionalWeakTable<Dispatcher, TranscriptHighlightingPool> Pools = new();

    internal static TextEditor Create(
        string text, string? grammar, double fontSize, Thickness padding,
        Func<bool, string>? readCopyText = null, ContextMenu? contextMenu = null,
        double maximumHeight = double.PositiveInfinity)
    {
        var editor = new TextEditor
        {
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas"),
            FontSize = fontSize,
            Padding = padding,
            BorderThickness = new Thickness(0),
            MaxHeight = maximumHeight,
            WordWrap = false,
            ShowLineNumbers = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Tag = "codeblock",
        };
        var pool = Pools.GetValue(editor.Dispatcher,
            _ => new TranscriptHighlightingPool(RegionHighlightingEngine.Default,
                new ThemeResourcePaletteSource().CreatePalette()));
        editor.Options.EnableHyperlinks = false;
        editor.Options.EnableEmailHyperlinks = false;
        editor.Document.UndoStack.SizeLimit = 0;
        editor.Text = text;
        editor.SyntaxHighlighting = grammar is null ? null : pool.GetDefinition(grammar);
        editor.SetResourceReference(Control.BackgroundProperty, "CodeSurface");
        editor.SetResourceReference(Control.ForegroundProperty, "CodeText");
        pool.Editors.GetOrCreateValue(editor);
        ReadOnlyTextInputGuard.DisableInputMethod(editor);
        AvalonEditClipboardGuard.Attach(editor);
        string ReadCopyText(bool selectionOnly) => readCopyText?.Invoke(selectionOnly)
            ?? (selectionOnly && editor.SelectionLength > 0 ? editor.SelectedText : editor.Text);
        editor.ContextMenu = contextMenu ?? CopyableCodeBlockBuilder.CreateCopyMenu(
            () => ReadCopyText(true), editor.SelectAll, () => editor.SelectionLength);
        CommandManager.AddPreviewExecutedHandler(editor.TextArea, (_, args) =>
        {
            if (args.Command != ApplicationCommands.Copy)
                return;
            CopyableCodeBlockBuilder.TrySetClipboard(ReadCopyText(true));
            args.Handled = true;
        });
        // Register before Loaded so a theme change during layout reaches this editor.
        // Weak keys also cover controls that are constructed but never shown.
        editor.Loaded += (_, _) =>
        {
            pool.Editors.GetOrCreateValue(editor);
            editor.Foreground = pool.Palette.Text;
            editor.TextArea.TextView.Redraw();
        };
        editor.Unloaded += (_, _) => pool.Editors.Remove(editor);
        return editor;
    }

    internal static TextBlock CreateLanguageLabel(string language, double baseFontSize, double copyHostWidth)
    {
        var label = new TextBlock
        {
            Text = language,
            FontFamily = SystemFonts.MessageFontFamily,
            FontSize = baseFontSize * 0.7,
            Margin = new Thickness(10, 7, copyHostWidth + 12, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        return label;
    }

    internal static Grid Build(string text, string? grammar, string languageLabel, double baseFontSize)
    {
        var copySize = baseFontSize * 1.5;
        var editor = Create(text, grammar, baseFontSize * 0.9,
            new Thickness(10, Math.Max(23.6, copySize + 2.6), 10, 8));
        var container = new Grid();
        container.Children.Add(editor);
        container.Children.Add(CreateLanguageLabel(languageLabel, baseFontSize, copySize));
        var copyHost = CopyableCodeBlockBuilder.CreateCopyHost(copySize, () => editor.Text);
        container.Children.Add(copyHost);
        return container;
    }

    internal static void ApplyTheme(Dispatcher dispatcher, RegionEditorPalette palette)
    {
        dispatcher.VerifyAccess();
        if (Pools.TryGetValue(dispatcher, out var pool))
            pool.ApplyPalette(palette);
    }
}
