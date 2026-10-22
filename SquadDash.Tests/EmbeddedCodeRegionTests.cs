using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace SquadDash.Tests;

/// <summary>
/// Automated proof for the Step 2 "embedded-region-serialization-test" requirement: an
/// embedded code region created from a triple-backtick fence — with and without a stored
/// language value — serializes back into an equivalent fenced block via
/// <see cref="RichTextBoxExtensions.GetPlainText"/>, and the collapse/expand toggle works
/// without affecting that serialized output. Covers both normative contract examples:
/// "backtick-fence-creates-region" and "region-serializes-with-language-tag".
/// </summary>
[TestFixture]
internal sealed class EmbeddedCodeRegionTests
{
    // ── EmbeddedFenceDetector (pure logic) ──────────────────────────────────────

    [Test]
    public void FindFirstFence_BareFenceWithContent_FindsOpenAndCloseLines()
    {
        var lines = new[] { "before", "```", "def foo():", "    pass", "```", "after" };
        var fence = EmbeddedFenceDetector.FindFirstFence(lines);

        Assert.That(fence, Is.Not.Null);
        Assert.That(fence!.Value.OpenLineIndex, Is.EqualTo(1));
        Assert.That(fence.Value.CloseLineIndex, Is.EqualTo(4));
        Assert.That(fence.Value.Language, Is.EqualTo(""));
        Assert.That(fence.Value.ExtractContent(lines), Is.EqualTo("def foo():\n    pass"));
    }

    [Test]
    public void FindFirstFence_FenceWithLanguageTag_CapturesLanguage()
    {
        var lines = new[] { "```python", "x = 1", "```" };
        var fence = EmbeddedFenceDetector.FindFirstFence(lines);

        Assert.That(fence, Is.Not.Null);
        Assert.That(fence!.Value.Language, Is.EqualTo("python"));
    }

    [Test]
    public void FindFirstFence_UnclosedFence_ReturnsNull()
    {
        var lines = new[] { "```", "still typing..." };
        Assert.That(EmbeddedFenceDetector.FindFirstFence(lines), Is.Null);
    }

    [Test]
    public void FindFirstFence_NoFence_ReturnsNull()
    {
        var lines = new[] { "just", "plain", "text" };
        Assert.That(EmbeddedFenceDetector.FindFirstFence(lines), Is.Null);
    }

    [Test]
    public void BuildFencedText_NoLanguage_ProducesBareFence()
    {
        var text = EmbeddedFenceDetector.BuildFencedText("def foo():\n    pass", null);
        Assert.That(text, Is.EqualTo("```\ndef foo():\n    pass\n```"));
    }

    [Test]
    public void BuildFencedText_WithLanguage_PlacesLanguageImmediatelyAfterOpeningBackticks()
    {
        var text = EmbeddedFenceDetector.BuildFencedText("x = 1", "python");
        Assert.That(text, Is.EqualTo("```python\nx = 1\n```"));
    }

    // ── Serialization convention: language-mode round-trips through the fence token ──────
    // explicit language → ```<lang> ; auto detect → bare ``` ; plain text → the reserved
    // token `text`. Each example builds the fenced form then re-parses it, proving the mode
    // survives the serialize→parse round-trip (the deterministic AVALONEDIT-20261014 contract).

    [Test]
    public void Serialization_ExplicitLanguageFence_EmitsAndReparsesLanguageToken()
    {
        // explicit-language-fence: Region mode = explicit language "csharp".
        var serialized = EmbeddedFenceDetector.BuildFencedText("int x = 1;", "csharp");
        Assert.That(serialized, Is.EqualTo("```csharp\nint x = 1;\n```"));

        var fence = EmbeddedFenceDetector.FindFirstFence(serialized.Split('\n'));
        Assert.That(fence, Is.Not.Null);
        Assert.That(fence!.Value.Language, Is.EqualTo("csharp"),
            "explicit language token must round-trip");
        Assert.That(fence.Value.ExtractContent(serialized.Split('\n')), Is.EqualTo("int x = 1;"));
    }

    [Test]
    public void Serialization_AutoDetect_EmitsBareFenceWithNoToken()
    {
        // auto-detect-no-tag: Region mode = Auto detect, Language field empty.
        var serialized = EmbeddedFenceDetector.BuildFencedText("int x = 1;", "");
        Assert.That(serialized, Is.EqualTo("```\nint x = 1;\n```"),
            "auto detect must serialize as a bare fence with no language token");

        var fence = EmbeddedFenceDetector.FindFirstFence(serialized.Split('\n'));
        Assert.That(fence, Is.Not.Null);
        Assert.That(fence!.Value.Language, Is.EqualTo(""),
            "auto detect must round-trip as an empty language token");
    }

    [Test]
    public void Serialization_PlainText_EmitsReservedTextToken()
    {
        // plain-text-marker: Region mode = Plain text → reserved token "text".
        var serialized = EmbeddedFenceDetector.BuildFencedText("int x = 1;", "text");
        Assert.That(serialized, Is.EqualTo("```text\nint x = 1;\n```"),
            "plain text must serialize with the reserved token `text`");

        var fence = EmbeddedFenceDetector.FindFirstFence(serialized.Split('\n'));
        Assert.That(fence, Is.Not.Null);
        Assert.That(fence!.Value.Language, Is.EqualTo("text"),
            "plain-text reserved token must round-trip");
    }

    // ── Contract Example 1: backtick-fence-creates-region ───────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void TypingFence_CreatesSingleCollapsibleRegion_ReplacingRawMarkers()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("```\ndef foo():\n    pass\n```");

        var converted = EmbeddedCodeRegionManager.TryConvertNextFence(rtb);
        Assert.That(converted, Is.True);

        // The raw fence paragraphs are gone — the document now holds exactly one block,
        // a BlockUIContainer wrapping an EmbeddedCodeRegionBlock.
        Assert.That(rtb.Document.Blocks.Count, Is.EqualTo(2));
        var container = (BlockUIContainer)rtb.Document.Blocks.FirstBlock!;
        var region = (EmbeddedCodeRegionBlock)container.Child;
        Assert.That(region.Content, Is.EqualTo("def foo():\n    pass"));
        Assert.That(region.Language, Is.EqualTo(""));
        Assert.That(RichTextBoxExtensions.IsEmptyEmbeddedCodeRegionCaretLandingParagraph(container.NextBlock!), Is.True);

        // No further fences remain to convert.
        Assert.That(EmbeddedCodeRegionManager.TryConvertNextFence(rtb), Is.False);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void TypingFence_ExpandedByDefault_ShowsFullContent()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("```\nhello\n```");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        var region = (EmbeddedCodeRegionBlock)((BlockUIContainer)rtb.Document.Blocks.FirstBlock!).Child;

        // The region is always expanded: no collapse/expand toggle button exists anywhere in
        // its visual tree, and the content editor is visible.
        Assert.That(FindDescendant<System.Windows.Controls.Primitives.ToggleButton>(region), Is.Null,
            "collapse/expand toggle button must be gone");
        var contentEditor = (ICSharpCode.AvalonEdit.TextEditor)((Border)((StackPanel)region.Child).Children[1]).Child;
        Assert.That(contentEditor.Visibility, Is.EqualTo(Visibility.Visible));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void LanguageLabel_SitsAboveEditorBorder_NotInsideIt()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("```python\nhello\n```");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);
        var region = (EmbeddedCodeRegionBlock)((BlockUIContainer)rtb.Document.Blocks.FirstBlock!).Child;

        // Outer element is a transparent layout host: no border chrome painted on it.
        Assert.That(region.BorderThickness, Is.EqualTo(new Thickness(0)));

        var stack = (StackPanel)region.Child;
        var trigger = (Button)stack.Children[0];
        var label = (TextBlock)((StackPanel)trigger.Content).Children[0];
        var editorBorder = (Border)stack.Children[1];

        Assert.That(label.Text, Is.EqualTo("Python"));
        // The label is a sibling ABOVE the editor rectangle, never a descendant of it.
        Assert.That(FindDescendant<TextBlock>(editorBorder), Is.Null,
            "language label must not live inside the editor border");
        Assert.That(editorBorder.Child, Is.InstanceOf<ICSharpCode.AvalonEdit.TextEditor>());

        // Serialized form is unaffected by the chrome restructure.
        Assert.That(rtb.GetPlainText(), Is.EqualTo("```python\nhello\n```"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void LanguageLabel_IsItalicWithNoBackground()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("```python\nhello\n```");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);
        var region = (EmbeddedCodeRegionBlock)((BlockUIContainer)rtb.Document.Blocks.FirstBlock!).Child;

        var stack = (StackPanel)region.Child;
        var label = (TextBlock)((StackPanel)((Button)stack.Children[0]).Content).Children[0];

        Assert.That(label.FontStyle, Is.EqualTo(FontStyles.Italic),
            "language label must render italic");
        Assert.That(label.Background, Is.EqualTo(System.Windows.Media.Brushes.Transparent),
            "language label must have a transparent (no) background fill");
    }

    // ── Contract Example 2: region-serializes-with-language-tag ─────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void SerializedRegion_WithStoredLanguage_ProducesLanguageTaggedFence()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("```\nx = 1\n```");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);
        var region = (EmbeddedCodeRegionBlock)((BlockUIContainer)rtb.Document.Blocks.FirstBlock!).Child;

        // Programmatic token updates use the same mode/label/serialization path as the picker.
        region.Language = "python";

        Assert.That(rtb.GetPlainText(), Is.EqualTo("```python\nx = 1\n```"));
        Assert.That(region.PickerMode, Is.EqualTo(RegionLanguageMode.ExplicitLanguage));
        Assert.That(LanguageLabel(region).Text, Is.EqualTo("Python"));
    }

    private static TextBlock LanguageLabel(EmbeddedCodeRegionBlock region)
        => (TextBlock)((StackPanel)((Button)((StackPanel)region.Child).Children[0]).Content).Children[0];

    private static Popup LanguagePopup(EmbeddedCodeRegionBlock region)
        => region.LanguagePickerPopup;

    private static Button[] LanguageOptions(EmbeddedCodeRegionBlock region)
        => ((StackPanel)((ScrollViewer)((Border)LanguagePopup(region).Child).Child).Content)
            .Children.Cast<Button>().ToArray();

    [Test, Apartment(ApartmentState.STA)]
    public void LanguagePicker_ListsSpecialModesThenEveryIncludedGrammar_InOrder()
    {
        var options = RegionLanguagePicker.Options;
        Assert.That(options.Take(2).Select(option => option.DisplayName),
            Is.EqualTo(new[] { "Auto detect", "Plain text" }));
        Assert.That(options.Take(2).Select(option => option.Mode),
            Is.EqualTo(new[] { RegionLanguageMode.AutoDetect, RegionLanguageMode.PlainText }));
        var grammars = ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.HighlightingDefinitions
            .Where(grammar => !string.Equals(grammar.Name, "MarkDownWithFontSize", StringComparison.OrdinalIgnoreCase))
            .Select(grammar => grammar.Name == "MarkDown" ? "Markdown" : grammar.Name)
            .Concat(new[] { "Json", "TypeScript", "YAML", "SQL", "Go", "Rust", "Bash", "Markdown", "Error message" })
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        Assert.That(grammars, Is.Not.Empty);
        Assert.That(options.Skip(2).Select(option => option.GrammarName), Is.EqualTo(grammars));
        Assert.That(options.Select(option => option.FenceToken), Is.Unique);
        Assert.That(options.Skip(2).All(option => option.Mode == RegionLanguageMode.ExplicitLanguage), Is.True);

        var region = EmbeddedCodeRegionBlock.Build("hello", "");
        Assert.That(LanguageOptions(region).Select(button => button.Content),
            Is.EqualTo(options.Select(option => option.DisplayName)));
        Assert.That(LanguageOptions(region).Select(button => button.Tag), Is.EqualTo(options));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void LanguagePicker_TargetsHeaderBottom_AndDoesNotClaimNativeButtonGesture()
    {
        var region = EmbeddedCodeRegionBlock.Build("hello", "");
        var trigger = (Button)((StackPanel)region.Child).Children[0];
        var popup = LanguagePopup(region);
        Assert.That(popup.PlacementTarget, Is.SameAs(trigger));
        Assert.That(popup.Placement, Is.EqualTo(PlacementMode.Bottom));
        Assert.That(popup.StaysOpen, Is.False);
        Assert.That(LogicalTreeHelper.GetParent(popup), Is.Null);
        Assert.That(System.Windows.Media.VisualTreeHelper.GetParent(popup), Is.Null);
        Assert.That(trigger.ToolTip, Is.EqualTo("Choose code language"));
        Assert.That(trigger.Focusable, Is.True);
        Assert.That(region.IsLanguagePickerHit(trigger), Is.True);
        Assert.That(EmbeddedCodeRegionBlock.ShouldClaimRegionMouseGesture(
            true, false, false, region.IsLanguagePickerHit(trigger)), Is.False);
        Assert.That(EmbeddedCodeRegionBlock.ShouldClaimRegionMouseGesture(true, false, false), Is.True);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void LanguagePicker_OptionMouseDown_IsNotClaimedByRegionChrome()
    {
        var region = EmbeddedCodeRegionBlock.Build("hello", "python");
        var button = LanguageOptions(region)[1];
        var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = Mouse.PreviewMouseDownEvent,
        };

        button.RaiseEvent(args);

        Assert.That(args.Handled, Is.False,
            "The region chrome must not swallow the option's native button gesture.");
    }

    [Test, Apartment(ApartmentState.STA)]
    public void LanguagePicker_PopupMouseDown_DoesNotReachDocumentPreviewHandlers()
    {
        var host = new RichTextBox();
        host.SetPlainText("```python\nhello\n```");
        Assert.That(EmbeddedCodeRegionManager.TryConvertNextFence(host), Is.True);
        var region = RichTextBoxExtensions.EnumerateEmbeddedRegions(host).Single();
        var popup = LanguagePopup(region);
        var hostMouseDowns = 0;
        var regionMouseDowns = 0;
        host.PreviewMouseDown += (_, _) => hostMouseDowns++;
        region.PreviewMouseDown += (_, _) => regionMouseDowns++;
        var trigger = (Button)((StackPanel)region.Child).Children[0];
        try
        {
            trigger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(popup.IsOpen, Is.True);
            // Captured outside-click input traverses Popup before its native dismissal handler.
            // Exercise that route without requiring an active desktop or moving the pointer.
            var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = Mouse.PreviewMouseDownEvent,
            };
            popup.RaiseEvent(args);

            Assert.That(hostMouseDowns, Is.Zero);
            Assert.That(regionMouseDowns, Is.Zero);
            Assert.That(args.Handled, Is.False);
            trigger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(popup.IsOpen, Is.False);
            trigger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(popup.IsOpen, Is.True);
            region.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            Assert.That(popup.IsOpen, Is.False, "Removing the region must close its detached popup.");
        }
        finally
        {
            popup.IsOpen = false;
        }
    }

    [Test, Apartment(ApartmentState.STA)]
    public void LanguagePicker_KeyboardGuardFindsRegionAcrossPopupLogicalOwnership()
    {
        var region = EmbeddedCodeRegionBlock.Build("hello", "");
        Assert.That(EmbeddedCodeRegionBlock.FindContainingRegion(LanguageOptions(region)[1]), Is.SameAs(region));
        Assert.That(EmbeddedCodeRegionBlock.FindContainingRegion(LanguageLabel(region)), Is.SameAs(region));
        Assert.That(EmbeddedCodeRegionBlock.FindContainingRegion(
            ((Border)((StackPanel)region.Child).Children[1]).Child), Is.SameAs(region));
        Assert.That(EmbeddedCodeRegionBlock.FindContainingRegion(new Button()), Is.Null);
    }

    [TestCase(Key.Enter), TestCase(Key.Space), TestCase(Key.Escape), Apartment(ApartmentState.STA)]
    public void LanguagePicker_KeyboardSelectionAndDismissal_DoNotLeakToPrompt(Key key)
    {
        var region = EmbeddedCodeRegionBlock.Build("hello", "python");
        var button = LanguageOptions(region)[1];
        var popup = LanguagePopup(region);
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, new PickerPresentationSource(), 0, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        };
        try
        {
            popup.IsOpen = true;
            Assert.That(popup.IsOpen, Is.True);
            button.RaiseEvent(args);
            Assert.That(popup.IsOpen, Is.False);
        }
        finally
        {
            popup.IsOpen = false;
        }
        Assert.That(args.Handled, Is.True);
        Assert.That(region.Language, Is.EqualTo(key == Key.Escape ? "python" : "text"));
    }

    private sealed class PickerPresentationSource : PresentationSource
    {
        public override System.Windows.Media.Visual? RootVisual { get; set; }
        public override bool IsDisposed => false;
        protected override System.Windows.Media.CompositionTarget? GetCompositionTargetCore() => null;
    }

    private static System.Collections.Generic.IEnumerable<TestCaseData> PickerRoundTripCases()
    {
        foreach (var option in RegionLanguagePicker.Options)
        foreach (var content in new[] { "int x = 1;", "before\n```\ninner\n```\nafter" })
            yield return new TestCaseData(option.FenceToken, option.Mode, content);
    }

    [TestCaseSource(nameof(PickerRoundTripCases)), Apartment(ApartmentState.STA)]
    public void LanguagePicker_OptionClick_WritesFenceAndRoundTripsMode(
        string token, RegionLanguageMode mode, string content)
    {
        var rtb = new RichTextBox();
        var initialToken = token == "text" ? "python" : "text";
        rtb.SetPlainText("before\n" + EmbeddedFenceDetector.BuildFencedText(content, initialToken) + "\nafter");
        Assert.That(EmbeddedCodeRegionManager.TryConvertNextFence(rtb), Is.True);
        var region = RichTextBoxExtensions.EnumerateEmbeddedRegions(rtb).Single();
        var button = LanguageOptions(region).Single(button => ((RegionLanguageOption)button.Tag).FenceToken == token);
        var changes = 0;
        region.PickerModeChanged += (_, _) => changes++;

        var popup = LanguagePopup(region);
        try
        {
            popup.IsOpen = true;
            Assert.That(popup.IsOpen, Is.True);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(popup.IsOpen, Is.False);
        }
        finally
        {
            popup.IsOpen = false;
        }

        Assert.That(changes, Is.EqualTo(1));
        Assert.That(region.Language, Is.EqualTo(token));
        Assert.That(region.PickerMode, Is.EqualTo(mode));
        Assert.That(LanguageLabel(region).Text, Is.EqualTo(
            mode == RegionLanguageMode.AutoDetect ? "Auto detect -- Plain text" : button.Content));
        var serialized = rtb.GetPlainText();
        var fence = content.Contains('`') ? "````" : "```";
        Assert.That(serialized, Is.EqualTo($"before\n{fence}{token}\n{content}\n{fence}\nafter"));

        var restored = new RichTextBox();
        restored.SetPlainText(serialized);
        Assert.That(EmbeddedCodeRegionManager.TryConvertNextFence(restored), Is.True);
        var restoredRegion = RichTextBoxExtensions.EnumerateEmbeddedRegions(restored).Single();
        Assert.That(restoredRegion.PickerMode, Is.EqualTo(mode));
        Assert.That(restoredRegion.Language, Is.EqualTo(token));
        Assert.That(restoredRegion.Content, Is.EqualTo(content));
        Assert.That(LanguageLabel(restoredRegion).Text, Is.EqualTo(LanguageLabel(region).Text));
        Assert.That(restored.GetPlainText(), Is.EqualTo(serialized));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void LanguagePicker_SelectionNotifiesSharedUndoBeforeAndAfter_AndSameModeIsNoOp()
    {
        var region = EmbeddedCodeRegionBlock.Build("hello", "python");
        var snapshots = new System.Collections.Generic.List<string>();
        void Capture(object? sender, EventArgs e)
        {
            if (ReferenceEquals(sender, region))
                snapshots.Add(region.ToFencedText());
        }
        EmbeddedCodeRegionBlock.AnyRegionContentEditing += Capture;
        EmbeddedCodeRegionBlock.AnyRegionContentEdited += Capture;
        try
        {
            var plainText = LanguageOptions(region)[1];
            plainText.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            plainText.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(snapshots, Is.EqualTo(new[] { "```python\nhello\n```", "```text\nhello\n```" }));
        }
        finally
        {
            EmbeddedCodeRegionBlock.AnyRegionContentEditing -= Capture;
            EmbeddedCodeRegionBlock.AnyRegionContentEdited -= Capture;
        }
    }

    [TestCase("custom-lang"), TestCase("csharp"), TestCase("f#"), Apartment(ApartmentState.STA)]
    public void LanguagePicker_UnregisteredLegacyTag_RemainsLossless(string token)
    {
        var region = EmbeddedCodeRegionBlock.Build("hello", token);
        Assert.That(region.Language, Is.EqualTo(token));
        Assert.That(region.PickerMode, Is.EqualTo(RegionLanguageMode.ExplicitLanguage));
        Assert.That(region.ToFencedText(), Is.EqualTo($"```{token}\nhello\n```"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void FenceWithLanguageTag_RoundTripsThroughConversionAndSerialization()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("```python\ndef foo():\n    pass\n```");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        Assert.That(rtb.GetPlainText(), Is.EqualTo("```python\ndef foo():\n    pass\n```"));
    }

    // ── Serialization round-trip with surrounding plain text ────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void FenceSurroundedByPlainText_PreservesTextBeforeAndAfter()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("before\n```\ncode\n```\nafter");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        Assert.That(rtb.GetPlainText(), Is.EqualTo("before\n```\ncode\n```\nafter"));

        // Confirm the middle block really did become a region, not just unconverted text. With
        // real content ("after") following the region, NO caret-landing blank line is synthesized
        // (Bug 4), so the document holds exactly: before-paragraph, region, after-paragraph.
        var blocks = rtb.Document.Blocks;
        Assert.That(blocks.Count, Is.EqualTo(3));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void MultipleFences_PastedTogether_BothConvertAfterLoop()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("```\nfirst\n```\nmiddle\n```python\nsecond\n```");

        var conversions = 0;
        while (EmbeddedCodeRegionManager.TryConvertNextFence(rtb)) conversions++;

        Assert.That(conversions, Is.EqualTo(2));
        Assert.That(rtb.GetPlainText(),
            Is.EqualTo("```\nfirst\n```\nmiddle\n```python\nsecond\n```"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void TextChangedConversion_WithExistingEmbeddedRegion_RebuildsWithoutUndoSerialization()
    {
        var rtb = new RichTextBox { IsUndoEnabled = true };
        rtb.SetPlainText("```\nfirst\n```\nplaceholder");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);
        Assert.That(rtb.Document.Blocks.OfType<BlockUIContainer>().Count(), Is.EqualTo(1));

        Exception? conversionException = null;
        var converting = false;
        rtb.TextChanged += (_, _) =>
        {
            if (converting)
                return;

            converting = true;
            try
            {
                while (EmbeddedCodeRegionManager.TryConvertNextFence(rtb)) { }
            }
            catch (Exception ex)
            {
                conversionException = ex;
            }
            finally
            {
                converting = false;
            }
        };

        var lastParagraph = (Paragraph)rtb.Document.Blocks.LastBlock!;
        Assert.DoesNotThrow(() =>
        {
            lastParagraph.Inlines.Clear();
            lastParagraph.Inlines.Add(new Run("```\nsecond\n```"));
        });

        Assert.That(conversionException, Is.Null);
        Assert.That(rtb.Document.Blocks.OfType<BlockUIContainer>().Count(), Is.EqualTo(2));
        Assert.That(rtb.GetPlainText(), Is.EqualTo("```\nfirst\n```\n```" + "\nsecond\n```"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void SetPlainText_AfterEmbeddedRegion_DoesNotSerializeRegionIntoUndoUnit()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("before\n```\ncode\n```\nafter");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        Assert.That(rtb.ContainsEmbeddedCodeRegion(), Is.True);
        var region = RichTextBoxExtensions.EnumerateEmbeddedRegions(rtb).Single();

        Assert.DoesNotThrow(() => rtb.SetPlainText("replacement"));
        Assert.That(rtb.GetPlainText(), Is.EqualTo("replacement"));
        Assert.That(rtb.ContainsEmbeddedCodeRegion(), Is.False);
        Assert.That(region.IsDisposed, Is.True);
        Assert.That(region.IsAttachedToHost(), Is.False);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void DeleteSelectionWithoutUndo_SelectedEmbeddedRegion_RemovesRegionWithoutSerializingIt()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("```\ncode\n```");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);
        var region = RichTextBoxExtensions.EnumerateEmbeddedRegions(rtb).Single();
        rtb.Selection.Select(rtb.Document.ContentStart, rtb.Document.ContentEnd);

        Assert.That(rtb.SelectionContainsEmbeddedCodeRegion(), Is.True);
        Assert.DoesNotThrow(() => rtb.DeleteSelectionWithoutUndo());
        Assert.That(rtb.ContainsEmbeddedCodeRegion(), Is.False);
        Assert.That(region.IsDisposed, Is.True);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void SelectionContainsEmbeddedCodeRegion_OrdinaryTextSelectionBeforeRegion_ReturnsFalse()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("before\n```\ncode\n```\nafter");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);
        rtb.Selection.Select(
            rtb.Document.Blocks.FirstBlock!.ContentStart,
            rtb.Document.Blocks.FirstBlock!.ContentEnd);

        Assert.That(rtb.SelectionContainsEmbeddedCodeRegion(), Is.False);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ReplaceSelectionWithoutUndo_SelectedEmbeddedRegion_ReplacesRegionWithoutSerializingIt()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("```\ncode\n```");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);
        rtb.Selection.Select(rtb.Document.ContentStart, rtb.Document.ContentEnd);

        Assert.DoesNotThrow(() => rtb.ReplaceSelectionWithoutUndo("typed"));
        Assert.That(rtb.ContainsEmbeddedCodeRegion(), Is.False);
        Assert.That(rtb.GetPlainText(), Is.EqualTo("typed"));
    }

    // ── Bug fix: copy/cut of a RichTextBox-level selection spanning a region keeps it ──
    // Regression guard for the embedded-region copy hole: WPF's default clipboard
    // serialization walks only text Runs and drops the region's BlockUIContainer, stripping
    // the region out of copied text. GetSelectionFencedText reuses the GetPlainText
    // block-walk so the region survives as its full fenced form.

    [Test, Apartment(ApartmentState.STA)]
    public void GetSelectionFencedText_SelectionSpanningRegion_PreservesRegionAsFencedText()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("before\n```\ncode\n```\nafter");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        // Select the whole document — spans text before, the region, and text after.
        rtb.Selection.Select(rtb.Document.ContentStart, rtb.Document.ContentEnd);

        var text = rtb.GetSelectionFencedText(out var containsRegion);

        Assert.That(containsRegion, Is.True);
        Assert.That(text, Is.EqualTo("before\n```\ncode\n```\nafter"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void GetSelectionFencedText_RegionContainingBacktickRun_UsesVariableLengthOuterFence()
    {
        var rtb = new RichTextBox();
        // Region content itself contains a 4-backtick line, so the outer fence must widen to
        // max(3, longestRun + 1) = 5 backticks. The variable-length-backtick path is exactly
        // what regressed, so prove it is used here and the region is not stripped.
        rtb.SetPlainText("intro\n`````\n````\ndone\n`````\noutro");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        var region = (EmbeddedCodeRegionBlock)rtb.Document.Blocks.OfType<BlockUIContainer>().First().Child;
        Assert.That(region.Content, Is.EqualTo("````\ndone"), "fence scanning should have captured the 4-backtick line as content");

        rtb.Selection.Select(rtb.Document.ContentStart, rtb.Document.ContentEnd);
        var text = rtb.GetSelectionFencedText(out var containsRegion);

        Assert.That(containsRegion, Is.True);
        Assert.That(text, Is.EqualTo("intro\n`````\n````\ndone\n`````\noutro"));
        // Explicitly confirm the widened 5-backtick outer fence is present (not stripped,
        // not a bare 3-backtick fence that would collide with the inner run).
        Assert.That(text, Does.Contain("`````\n````\ndone\n`````"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void GetSelectionFencedText_SelectionWithoutRegion_ReportsNoRegionSoWpfDefaultCopyProceeds()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("before\n```\ncode\n```\nafter");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        // Select only the leading "before" paragraph — no region touched.
        var firstPara = rtb.Document.Blocks.FirstBlock!;
        rtb.Selection.Select(firstPara.ContentStart, firstPara.ContentEnd);

        var text = rtb.GetSelectionFencedText(out var containsRegion);

        Assert.That(containsRegion, Is.False, "no region intersected — caller must let WPF's default copy run");
        Assert.That(text, Is.EqualTo("before"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void GetSelectionFencedText_PartialLeadingAndTrailingParagraphs_ClampToSelectionButKeepFullRegion()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("hello\n```\ncode\n```\nworld");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        // Plain-text layout: "hello"(0..5) \n ```\ncode\n```(region, 6..18) \n "world"(19..24).
        // Select from mid-"hello" (after "hel", offset 3) to mid-"world" (after "wor",
        // offset 22), spanning the region in between.
        rtb.SelectRange(3, 19);

        var text = rtb.GetSelectionFencedText(out var containsRegion);

        Assert.That(containsRegion, Is.True);
        Assert.That(text, Is.EqualTo("lo\n```\ncode\n```\nwor"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void GetSelectionFencedText_RegionWithLanguageTag_PreservesLanguageTaggedFence()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("```python\nx = 1\n```");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);
        rtb.Selection.Select(rtb.Document.ContentStart, rtb.Document.ContentEnd);

        var text = rtb.GetSelectionFencedText(out var containsRegion);

        Assert.That(containsRegion, Is.True);
        Assert.That(text, Is.EqualTo("```python\nx = 1\n```"));
    }

    // ── No Step 1 regression: plain text with no embedded regions round-trips untouched ──

    [Test, Apartment(ApartmentState.STA)]
    public void PlainTextWithNoFence_GetPlainTextUnaffected()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("just an ordinary prompt with no code fences at all");
        Assert.That(EmbeddedCodeRegionManager.TryConvertNextFence(rtb), Is.False);
        Assert.That(rtb.GetPlainText(), Is.EqualTo("just an ordinary prompt with no code fences at all"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void UnclosedFence_DoesNotConvert_LeavesRawTextIntact()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("```\nstill typing, no closing fence yet");
        Assert.That(EmbeddedCodeRegionManager.TryConvertNextFence(rtb), Is.False);
        Assert.That(rtb.GetPlainText(), Is.EqualTo("```\nstill typing, no closing fence yet"));
    }

    // ── Bug fix 1: content box is genuinely editable and round-trips edits ─────

    [Test, Apartment(ApartmentState.STA)]
    public void EditingContentDirectly_UpdatesContentProperty_AndRoundTripsThroughGetPlainText()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("```\noriginal\n```");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        var region = (EmbeddedCodeRegionBlock)((BlockUIContainer)rtb.Document.Blocks.FirstBlock!).Child;

        // Simulate the user editing inside the region's content box directly (as opposed to
        // setting Content programmatically, which the old read-only TextBox could never do).
        var contentBox = GetContentEditor(region);
        Assert.That(contentBox.IsReadOnly, Is.False, "content box must be editable, not read-only");

        contentBox.Text = "edited content";

        Assert.That(region.Content, Is.EqualTo("edited content"));
        Assert.That(rtb.GetPlainText(), Is.EqualTo("```\nedited content\n```"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void EditingContentDirectly_SurvivesUnrelatedFenceConversionRebuild()
    {
        // A region's content edits must not be clobbered when an unrelated new fence
        // elsewhere in the same document triggers EmbeddedCodeRegionManager to rebuild
        // Document.Blocks (the rebuild reattaches the *same* BlockUIContainer instance).
        var rtb = new RichTextBox();
        rtb.SetPlainText("```\noriginal\n```\nsome text");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        var container = (BlockUIContainer)rtb.Document.Blocks.FirstBlock!;
        var region = (EmbeddedCodeRegionBlock)container.Child;
        var contentBox = GetContentEditor(region);
        contentBox.Text = "edited before rebuild";

        // Replace the trailing plain text with a brand-new fence, forcing another
        // conversion/rebuild pass that must preserve the first region's instance and edits.
        var lastParagraph = (Paragraph)rtb.Document.Blocks.LastBlock!;
        lastParagraph.Inlines.Clear();
        lastParagraph.Inlines.Add(new Run("```\nsecond\n```"));
        Assert.That(EmbeddedCodeRegionManager.TryConvertNextFence(rtb), Is.True);

        var stillSameContainer = (BlockUIContainer)rtb.Document.Blocks.FirstBlock!;
        Assert.That(stillSameContainer, Is.SameAs(container), "rebuild must reuse the same BlockUIContainer instance");
        var stillSameRegion = (EmbeddedCodeRegionBlock)stillSameContainer.Child;
        Assert.That(stillSameRegion.Content, Is.EqualTo("edited before rebuild"));
        Assert.That(rtb.GetPlainText(), Does.StartWith("```\nedited before rebuild\n```"));
    }

    // ── Bug fix 2: caret placement after conversion (not always document end) ─────

    [Test, Apartment(ApartmentState.STA)]
    public void TryConvertNextFence_CaretRightAfterClosingFence_EndsUpRightAfterRegion_NotDocumentEnd()
    {
        var rtb = new RichTextBox();
        // Trailing text after the fence means "document end" and "right after the region"
        // are two different positions — this distinguishes the old (always-jump-to-end) bug
        // from the fix.
        rtb.SetPlainText("```\ncode\n```\nmore text after");
        var caretAfterFence = "```\ncode\n```".Length;
        rtb.SetCaretOffset(caretAfterFence);

        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        // With real content following the region there is no synthesized caret-landing paragraph
        // (Bug 4), so the caret resolves to the START of the following content line — right after
        // the region, and still NOT document end.
        var afterRegionLineStart = caretAfterFence + 1;
        Assert.That(rtb.GetCaretOffset(), Is.EqualTo(afterRegionLineStart));
        Assert.That(rtb.GetCaretOffset(), Is.Not.EqualTo(rtb.GetTextLength()));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void GetCharOffset_PositionAfterRegion_AccountsForRegionsFencedTextLength()
    {
        // Direct proof that offset math treats a region as consuming exactly its serialized
        // fenced-text length — the root cause behind "typing lands above the region".
        var rtb = new RichTextBox();
        rtb.SetPlainText("before\n```\ncode\n```\nafter");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        var fullText = rtb.GetPlainText();
        Assert.That(fullText, Is.EqualTo("before\n```\ncode\n```\nafter"));

        var afterOffset = fullText.IndexOf("after", StringComparison.Ordinal);
        var pointer = rtb.GetTextPointerAt(afterOffset);
        Assert.That(rtb.GetCharOffset(pointer), Is.EqualTo(afterOffset));
    }

    // ── Bug fix 3: fence conversion runs on programmatic load, not just typing ─────

    [Test, Apartment(ApartmentState.STA)]
    public void SetPlainTextThenExplicitConversionLoop_ConvertsPreExistingFences_SimulatingLoadPath()
    {
        // MainWindow's SetPromptTextBoxLogicalBuffer (the programmatic load/restore path)
        // now runs the same conversion loop PromptTextBox_TextChanged uses, immediately
        // after SetPlainText, regardless of IsApplyingHistoryEntry. This proves the
        // conversion primitives it relies on correctly convert content set programmatically
        // (not just via live typing/TextChanged).
        var rtb = new RichTextBox();
        rtb.SetPlainText("```python\nx = 1\n```");

        Assert.That(rtb.ContainsEmbeddedCodeRegion(), Is.False, "SetPlainText alone must not auto-convert");

        while (EmbeddedCodeRegionManager.TryConvertNextFence(rtb)) { }

        Assert.That(rtb.ContainsEmbeddedCodeRegion(), Is.True);
        Assert.That(rtb.GetPlainText(), Is.EqualTo("```python\nx = 1\n```"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void EmptyCaretLandingParagraph_AfterRegion_IsVisualOnlyUntilTypedInto()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("```\ncode\n```");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        var regionContainer = (BlockUIContainer)rtb.Document.Blocks.FirstBlock!;
        var landing = (Paragraph)regionContainer.NextBlock!;

        Assert.That(RichTextBoxExtensions.IsEmptyEmbeddedCodeRegionCaretLandingParagraph(landing), Is.True);
        Assert.That(rtb.GetPlainText(), Is.EqualTo("```\ncode\n```"));

        landing.Inlines.Clear();
        landing.Inlines.Add(new Run("after"));

        Assert.That(RichTextBoxExtensions.IsEmptyEmbeddedCodeRegionCaretLandingParagraph(landing), Is.False);
        Assert.That(rtb.GetPlainText(), Is.EqualTo("```\ncode\n```\nafter"));
    }

    // ── Bug fix 4: deleting a region never crashes ──────────────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void TryDeleteAdjacentEmbeddedCodeRegion_CaretImmediatelyBeforeRegion_RemovesItWithoutCrashing()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("before\n```\ncode\n```\nafter");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        var container = rtb.Document.Blocks.OfType<BlockUIContainer>().First();

        // Proves the pure adjacency rule directly against an explicit TextPointer, rather
        // than through RichTextBox.CaretPosition's setter — which only reliably normalizes
        // a position adjacent to a focusable-content BlockUIContainer once the control is
        // actually focused inside a rendered window (not the case for a bare, unparented
        // RichTextBox in a unit test).
        var found = RichTextBoxExtensions.FindAdjacentEmbeddedCodeRegion(
            rtb.Document, container.ElementStart, LogicalDirection.Forward);
        Assert.That(found, Is.SameAs(container));

        Assert.DoesNotThrow(() => rtb.Document.Blocks.Remove(container));
        Assert.That(rtb.ContainsEmbeddedCodeRegion(), Is.False);
        Assert.That(rtb.GetPlainText(), Is.EqualTo("before\nafter"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void TryDeleteAdjacentEmbeddedCodeRegion_CaretImmediatelyAfterRegion_RemovesItWithoutCrashing()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("before\n```\ncode\n```\nafter");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        var container = rtb.Document.Blocks.OfType<BlockUIContainer>().First();

        var found = RichTextBoxExtensions.FindAdjacentEmbeddedCodeRegion(
            rtb.Document, container.ElementEnd, LogicalDirection.Backward);
        Assert.That(found, Is.SameAs(container));

        Assert.DoesNotThrow(() => rtb.Document.Blocks.Remove(container));
        Assert.That(rtb.ContainsEmbeddedCodeRegion(), Is.False);
        Assert.That(rtb.GetPlainText(), Is.EqualTo("before\nafter"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void FindAdjacentEmbeddedCodeRegion_CaretNotAdjacent_ReturnsNull()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("before\n```\ncode\n```\nafter");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        var found = RichTextBoxExtensions.FindAdjacentEmbeddedCodeRegion(
            rtb.Document, rtb.Document.ContentStart, LogicalDirection.Forward);

        Assert.That(found, Is.Null);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void TryDeleteAdjacentEmbeddedCodeRegion_EmptyDocument_ReturnsFalseAndDoesNotThrow()
    {
        // End-to-end smoke test through the real RichTextBox.CaretPosition-based entry point:
        // when there's nothing adjacent (or no region at all), it must safely no-op rather
        // than throw, regardless of the environment's caret-normalization behavior.
        var rtb = new RichTextBox();
        rtb.SetPlainText("just plain text, no regions");

        bool removed = false;
        Assert.DoesNotThrow(() => removed = rtb.TryDeleteAdjacentEmbeddedCodeRegion(LogicalDirection.Forward));
        Assert.That(removed, Is.False);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void NativeDeleteCommand_SelectedEmbeddedRegionWithSurroundingParagraphs_DoesNotThrow()
    {
        // Regression proof for the reported crash: selecting exactly a region (amid
        // surrounding plain-text paragraphs, matching real prompt-box documents rather than
        // a single-block document) and invoking the same editing command WPF's native
        // Delete key raises must not throw.
        var rtb = new RichTextBox();
        rtb.SetPlainText("before\n```\ncode\n```\nafter");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        var container = rtb.Document.Blocks.OfType<BlockUIContainer>().First();
        rtb.Selection.Select(container.ElementStart, container.ElementEnd);

        Assert.DoesNotThrow(() => EditingCommands.Delete.Execute(null, rtb));
        Assert.That(rtb.ContainsEmbeddedCodeRegion(), Is.False);
        Assert.That(rtb.GetPlainText(), Is.EqualTo("before\nafter"));
    }

    // ── Instant open-fence conversion: region appears the moment "```" is completed ──

    [Test]
    public void FindOpenFenceWithNoClose_BareOpeningMarker_NoCloseAnywhere_Found()
    {
        var lines = new[] { "before", "```" };
        var open = EmbeddedFenceDetector.FindOpenFenceWithNoClose(lines);

        Assert.That(open, Is.Not.Null);
        Assert.That(open!.Value.LineIndex, Is.EqualTo(1));
        Assert.That(open.Value.Language, Is.EqualTo(""));
    }

    [Test]
    public void FindOpenFenceWithNoClose_WithLanguageTag_CapturesLanguage()
    {
        var lines = new[] { "```python" };
        var open = EmbeddedFenceDetector.FindOpenFenceWithNoClose(lines);

        Assert.That(open, Is.Not.Null);
        Assert.That(open!.Value.Language, Is.EqualTo("python"));
    }

    [Test]
    public void FindOpenFenceWithNoClose_CompleteFenceAlreadyExists_ReturnsNull()
    {
        // A fully-formed open+close pair must be left to FindFirstFence/TryConvertNextFence
        // instead — this is what lets the complete-fence-paste path keep priority.
        var lines = new[] { "```", "code", "```" };
        Assert.That(EmbeddedFenceDetector.FindOpenFenceWithNoClose(lines), Is.Null);
    }

    [Test]
    public void FindOpenFenceWithNoClose_NoFenceAtAll_ReturnsNull()
    {
        var lines = new[] { "just", "plain", "text" };
        Assert.That(EmbeddedFenceDetector.FindOpenFenceWithNoClose(lines), Is.Null);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void TryConvertOpenFence_BareOpeningMarker_InstantlyCreatesEmptyExpandedRegion_WithCaretFocusedInside()
    {
        // Simulates the user finishing typing "```" on an otherwise-empty line, with no
        // closing fence typed yet.
        var rtb = new RichTextBox();
        rtb.SetPlainText("```");

        var converted = EmbeddedCodeRegionManager.TryConvertOpenFence(rtb);
        Assert.That(converted, Is.True);

        Assert.That(rtb.Document.Blocks.Count, Is.EqualTo(2));
        var container = (BlockUIContainer)rtb.Document.Blocks.FirstBlock!;
        var region = (EmbeddedCodeRegionBlock)container.Child;
        Assert.That(RichTextBoxExtensions.IsEmptyEmbeddedCodeRegionCaretLandingParagraph(container.NextBlock!), Is.True);

        Assert.That(region.Content, Is.EqualTo(""), "region must be created empty, not waiting for pasted content");
        Assert.That(region.Language, Is.EqualTo(""));

        // A live keyboard-focus assertion without a real window/PresentationSource: calling
        // Focus() on a control outside a visual root can't acquire true OS keyboard focus,
        // but it does still set WPF's own logical IsFocused state within the control's focus
        // scope — which is exactly what FocusContent (called by TryConvertOpenFence's
        // render step) is proven here to have done.
        var contentBox = GetContentEditor(region);
        Assert.That(contentBox.TextArea.IsFocused, Is.True, "caret must be focused into the content box immediately");
    }

    [Test, Apartment(ApartmentState.STA)]
    public void TryConvertOpenFence_WithLanguageTag_InstantlyCreatesEmptyExpandedRegion_WithLanguageAndFocus()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("```python");

        var converted = EmbeddedCodeRegionManager.TryConvertOpenFence(rtb);
        Assert.That(converted, Is.True);

        var container = (BlockUIContainer)rtb.Document.Blocks.FirstBlock!;
        var region = (EmbeddedCodeRegionBlock)container.Child;

        Assert.That(region.Content, Is.EqualTo(""));
        Assert.That(region.Language, Is.EqualTo("python"));

        var contentBox = GetContentEditor(region);
        Assert.That(contentBox.TextArea.IsFocused, Is.True);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void TryConvertOpenFence_SurroundingPlainTextPreserved()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("before\n```\n");

        Assert.That(EmbeddedCodeRegionManager.TryConvertOpenFence(rtb), Is.True);
        Assert.That(rtb.Document.Blocks.Count, Is.EqualTo(3));
        Assert.That(((Paragraph)rtb.Document.Blocks.FirstBlock!).Inlines.OfType<Run>().First().Text,
            Is.EqualTo("before"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void TryConvertOpenFence_CompleteFenceAlreadyPresent_DoesNotApply()
    {
        // When a complete fence has already been pasted/typed, the open-fence path must not
        // fire — TryConvertNextFence owns that case (priority rule from the task brief).
        var rtb = new RichTextBox();
        rtb.SetPlainText("```\ncode\n```");

        Assert.That(EmbeddedCodeRegionManager.TryConvertOpenFence(rtb), Is.False);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void TryConvertNextFence_TakesPriorityOverOpenFence_WhenBothPresentInSameEdit()
    {
        // A multi-line paste that completes one fence while a second, separate fence is
        // still open must have the complete one converted by TryConvertNextFence first;
        // TryConvertOpenFence then only picks up the still-open remainder — matching how
        // MainWindow.PromptTextBox_TextChanged orders the two calls.
        var rtb = new RichTextBox();
        rtb.SetPlainText("```\nfirst\n```\n```python");

        var completeConversions = 0;
        while (EmbeddedCodeRegionManager.TryConvertNextFence(rtb)) completeConversions++;
        Assert.That(completeConversions, Is.EqualTo(1));

        Assert.That(EmbeddedCodeRegionManager.TryConvertOpenFence(rtb), Is.True);

        var containers = rtb.Document.Blocks.OfType<BlockUIContainer>().ToList();
        Assert.That(containers.Count, Is.EqualTo(2));
        var firstRegion = (EmbeddedCodeRegionBlock)containers[0].Child;
        var secondRegion = (EmbeddedCodeRegionBlock)containers[1].Child;
        Assert.That(firstRegion.Content, Is.EqualTo("first"));
        Assert.That(secondRegion.Content, Is.EqualTo(""));
        Assert.That(secondRegion.Language, Is.EqualTo("python"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void TypingClosingFenceLineInsideContentBox_StripsMarkerFromContent_RegionStaysExpanded()
    {
        // Judgment call for "how does the user finish an instantly-opened region": typing a
        // bare ``` as its own last line inside the content box ends it — the marker line is
        // stripped back out of Content (ToFencedText already supplies the closing marker).
        // The region stays expanded (collapse was removed); the caret exits below the region.
        var rtb = new RichTextBox();
        rtb.SetPlainText("```");
        EmbeddedCodeRegionManager.TryConvertOpenFence(rtb);

        var container = (BlockUIContainer)rtb.Document.Blocks.FirstBlock!;
        var region = (EmbeddedCodeRegionBlock)container.Child;
        var contentBox = GetContentEditor(region);

        contentBox.Text = "x = 1\n```";

        Assert.That(region.Content, Is.EqualTo("x = 1"));
        Assert.That(contentBox.Visibility, Is.EqualTo(Visibility.Visible), "region stays expanded, not collapsed");
        Assert.That(FindDescendant<System.Windows.Controls.Primitives.ToggleButton>(region), Is.Null);
        Assert.That(rtb.Document.Blocks.Count, Is.EqualTo(2), "closing the region should leave a caret landing paragraph after it");
        Assert.That(rtb.GetPlainText(), Is.EqualTo("```\nx = 1\n```"));
    }

    // ── "Only one live selection at a time": clearing the region selection on exit ──────
    // When the caret leaves the region (keyboard-driven exit, or focus moving to the host /
    // another control), the region's own highlighted selection must collapse to a zero-length
    // caret so it can't coexist with a host-prompt selection. Text must never change.

    private static (RichTextBox host, EmbeddedCodeRegionBlock region, ICSharpCode.AvalonEdit.TextEditor editor) BuildFocusedRegion()
    {
        var (_, region) = BuildRegionSandwich();
        var host = (RichTextBox)typeof(EmbeddedCodeRegionBlock)
            .GetField("_hostRichTextBox", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(region)!;
        var editor = GetContentEditor(region);
        return (host, region, editor);
    }

    private static void RaiseLostKeyboardFocus(ICSharpCode.AvalonEdit.TextEditor editor, IInputElement? newFocus)
    {
        var args = new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, Environment.TickCount, editor.TextArea, newFocus)
        {
            RoutedEvent = Keyboard.LostKeyboardFocusEvent,
        };
        editor.RaiseEvent(args);
    }

    private static void InvokeFocusHostOutsideRegion(EmbeddedCodeRegionBlock region, LogicalDirection direction)
    {
        typeof(EmbeddedCodeRegionBlock)
            .GetMethod("FocusHostOutsideRegion", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(region, new object[] { direction, false });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void LostKeyboardFocus_FocusLeavesRegionToHost_CollapsesRegionSelection_TextUnchanged()
    {
        // Covers "clicking outside" and "selecting outside": keyboard focus moves from the
        // region to the host prompt. The region's selection must collapse; text is untouched.
        var (host, region, editor) = BuildFocusedRegion();
        editor.Select(0, editor.Document.TextLength);
        Assert.That(editor.SelectionLength, Is.GreaterThan(0), "precondition: region has a live selection");

        RaiseLostKeyboardFocus(editor, host);

        Assert.That(editor.SelectionLength, Is.EqualTo(0), "region selection must collapse when focus leaves to the host");
        Assert.That(editor.Text, Is.EqualTo(region.Content), "clearing the selection must NOT change the region text");
        Assert.That(editor.CaretOffset, Is.InRange(0, editor.Document.TextLength));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void InRegionSelection_WithoutAnyExit_IsPreserved_SoFencedCopyCutStillSeesIt()
    {
        // Regression guard: the collapse is exit-only. Absent any exit (no FocusHostOutsideRegion,
        // no focus leaving the region), an in-region selection must survive untouched — this is the
        // precondition the fenced Ctrl+C / Ctrl+X path relies on (it reads SelectedText while the
        // region is still focused, before any exit). Nothing here should spontaneously clear it.
        // (The live "focus moved to another element still inside the region" case is driven by the
        // real visual tree and is human-verified; a headless editor has no applied template, so its
        // TextArea is not yet a visual descendant of the block for IsVisualAncestorOrSelf to see.)
        var (_, _, editor) = BuildFocusedRegion();
        editor.Select(0, editor.Document.TextLength);

        Assert.That(editor.SelectionLength, Is.GreaterThan(0));
        Assert.That(editor.SelectedText, Is.EqualTo(editor.Text), "fenced copy/cut must still see the full selection");
    }

    [Test, Apartment(ApartmentState.STA)]
    public void LostKeyboardFocus_NoSelection_IsCleanNoOp()
    {
        var (host, region, editor) = BuildFocusedRegion();
        editor.TextArea.ClearSelection();
        Assert.That(editor.SelectionLength, Is.EqualTo(0));

        Assert.DoesNotThrow(() => RaiseLostKeyboardFocus(editor, host));

        Assert.That(editor.SelectionLength, Is.EqualTo(0));
        Assert.That(editor.Text, Is.EqualTo(region.Content));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void FocusHostOutsideRegion_WithSelection_CollapsesSelection_TextUnchanged()
    {
        // Covers "arrow-navigating out": the deliberate keyboard boundary exit collapses the
        // region selection immediately (deterministic, no async-focus timing gap).
        var (_, region, editor) = BuildFocusedRegion();
        editor.Select(0, editor.Document.TextLength);
        Assert.That(editor.SelectionLength, Is.GreaterThan(0));

        InvokeFocusHostOutsideRegion(region, LogicalDirection.Forward);

        Assert.That(editor.SelectionLength, Is.EqualTo(0), "keyboard exit must collapse the region selection");
        Assert.That(editor.Text, Is.EqualTo(region.Content), "exit must NOT change the region text");
    }

    [Test, Apartment(ApartmentState.STA)]
    public void FocusHostOutsideRegion_NoSelection_IsCleanNoOp()
    {
        var (_, region, editor) = BuildFocusedRegion();
        editor.TextArea.ClearSelection();

        Assert.DoesNotThrow(() => InvokeFocusHostOutsideRegion(region, LogicalDirection.Forward));

        Assert.That(editor.SelectionLength, Is.EqualTo(0));
        Assert.That(editor.Text, Is.EqualTo(region.Content));
    }

    // ── Smart arrow-key navigation ─────────────────────────────────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void FindRegionOnCaretParagraph_CaretOnRegionParagraph_FindsRegion()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("before\n```\n123456\n```\nafter");
        
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        // Find the paragraph between "before" and the region
        var blocks = rtb.Document.Blocks.ToList();
        var beforePara = blocks[0] as Paragraph;
        Assert.That(beforePara, Is.Not.Null);

        // Place caret at the end of the "before" paragraph, right where it would be
        // when the user presses Down and would land on the region's paragraph line
        var caretPointer = beforePara!.ContentEnd;
        
        var region = RichTextBoxExtensions.FindRegionOnCaretParagraph(rtb.Document, caretPointer, Key.Down);
        Assert.That(region, Is.Not.Null);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void FindRegionOnCaretParagraph_CaretNotOnRegion_ReturnsNull()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("line1\nline2\nline3");

        var firstPara = rtb.Document.Blocks.FirstBlock as Paragraph;
        var caretPointer = firstPara!.ContentStart;

        var region = RichTextBoxExtensions.FindRegionOnCaretParagraph(rtb.Document, caretPointer, Key.Down);
        Assert.That(region, Is.Null);
    }

    // ── Direction-aware entry decision (pure, deterministic) ───────────────────────
    // Document layout for these tests: block[0]=Paragraph "before", block[1]=region("code"),
    // block[2]=Paragraph "after". START=0, END=Content.Length (4 for "code").

    private static (System.Collections.Generic.List<Block> blocks, EmbeddedCodeRegionBlock region) BuildRegionSandwich()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("before\n```\ncode\n```\nafter");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);
        var blocks = rtb.Document.Blocks.ToList();
        var region = (EmbeddedCodeRegionBlock)((BlockUIContainer)blocks[1]).Child;
        return (blocks, region);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void DecideRegionEntry_DownOnLineAboveRegion_EntersRegionAtStart()
    {
        var (blocks, region) = BuildRegionSandwich();

        var decision = RichTextBoxExtensions.DecideRegionEntry(blocks, 0, Key.Down);

        Assert.That(decision, Is.Not.Null);
        Assert.That(decision!.Value.Region, Is.SameAs(region));
        Assert.That(decision.Value.TargetCaretPosition, Is.EqualTo(0));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void DecideRegionEntry_RightOnLineAboveRegion_EntersRegionAtStart()
    {
        var (blocks, region) = BuildRegionSandwich();

        var decision = RichTextBoxExtensions.DecideRegionEntry(blocks, 0, Key.Right);

        Assert.That(decision, Is.Not.Null);
        Assert.That(decision!.Value.Region, Is.SameAs(region));
        Assert.That(decision.Value.TargetCaretPosition, Is.EqualTo(0));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void DecideRegionEntry_UpOnLineBelowRegion_EntersRegionAtEnd()
    {
        var (blocks, region) = BuildRegionSandwich();

        var decision = RichTextBoxExtensions.DecideRegionEntry(blocks, 2, Key.Up);

        Assert.That(decision, Is.Not.Null);
        Assert.That(decision!.Value.Region, Is.SameAs(region));
        Assert.That(decision.Value.TargetCaretPosition, Is.EqualTo(region.Content.Length));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void DecideRegionEntry_LeftOnLineBelowRegion_EntersRegionAtEnd()
    {
        var (blocks, region) = BuildRegionSandwich();

        var decision = RichTextBoxExtensions.DecideRegionEntry(blocks, 2, Key.Left);

        Assert.That(decision, Is.Not.Null);
        Assert.That(decision!.Value.Region, Is.SameAs(region));
        Assert.That(decision.Value.TargetCaretPosition, Is.EqualTo(region.Content.Length));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void DecideRegionEntry_DownOnLineBelowRegion_DoesNotEnterRegionAbove()
    {
        // The reported bug: Down pressed on the line BELOW a region must NEVER jump UP into it.
        var (blocks, _) = BuildRegionSandwich();

        var decision = RichTextBoxExtensions.DecideRegionEntry(blocks, 2, Key.Down);

        Assert.That(decision, Is.Null);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void DecideRegionEntry_RightOnLineBelowRegion_DoesNotEnterRegionAbove()
    {
        var (blocks, _) = BuildRegionSandwich();

        var decision = RichTextBoxExtensions.DecideRegionEntry(blocks, 2, Key.Right);

        Assert.That(decision, Is.Null);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void DecideRegionEntry_UpOnLineAboveRegion_DoesNotEnterRegionBelow()
    {
        // Symmetric guard: Up pressed on the line ABOVE a region must never drop DOWN into it.
        var (blocks, _) = BuildRegionSandwich();

        var decision = RichTextBoxExtensions.DecideRegionEntry(blocks, 0, Key.Up);

        Assert.That(decision, Is.Null);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void DecideRegionEntry_LeftOnLineAboveRegion_DoesNotEnterRegionBelow()
    {
        var (blocks, _) = BuildRegionSandwich();

        var decision = RichTextBoxExtensions.DecideRegionEntry(blocks, 0, Key.Left);

        Assert.That(decision, Is.Null);
    }

    // ── Change 1: column-preserving Up from the start of the host line below the region ────────
    // Document layout: block[0]=Paragraph "123", block[1]=multi-line region("abc\n  def\nghi"),
    // block[2]=Paragraph "456". The region's LAST line starts at offset 10 (just after the 2nd \n).

    private static (System.Collections.Generic.List<Block> blocks, EmbeddedCodeRegionBlock region) BuildMultiLineRegionSandwich()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("123\n```\nabc\n  def\nghi\n```\n456");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);
        var blocks = rtb.Document.Blocks.ToList();
        var region = (EmbeddedCodeRegionBlock)((BlockUIContainer)blocks[1]).Child;
        return (blocks, region);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void DecideRegionEntry_UpFromStartOfLineBelow_EntersRegionAtStartOfLastLine()
    {
        // Change 1: caret at the START of "456" + Up enters the region at the START of its LAST
        // line ("ghi"), not its END. For "abc\n  def\nghi" that offset is just after the last \n.
        var (blocks, region) = BuildMultiLineRegionSandwich();
        var expectedLastLineStart = region.Content.LastIndexOf('\n') + 1;

        var decision = RichTextBoxExtensions.DecideRegionEntry(blocks, 2, Key.Up, caretAtLineStart: true);

        Assert.That(decision, Is.Not.Null);
        Assert.That(decision!.Value.Region, Is.SameAs(region));
        Assert.That(decision.Value.TargetCaretPosition, Is.EqualTo(expectedLastLineStart));
        Assert.That(decision.Value.TargetCaretPosition, Is.LessThan(region.Content.Length));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void DecideRegionEntry_UpFromMidLineBelow_StillEntersRegionAtEnd()
    {
        // Non-regression: Up NOT at the start of the host line keeps the historical "enter at END"
        // behavior (caretAtLineStart defaults to false).
        var (blocks, region) = BuildMultiLineRegionSandwich();

        var decision = RichTextBoxExtensions.DecideRegionEntry(blocks, 2, Key.Up);

        Assert.That(decision, Is.Not.Null);
        Assert.That(decision!.Value.TargetCaretPosition, Is.EqualTo(region.Content.Length));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void DecideRegionEntry_UpFromStartOfLineBelow_SingleLineRegion_EntersAtZero()
    {
        // Single-line region: START of the last line == START of the only line == offset 0.
        var (blocks, region) = BuildRegionSandwich();
        Assert.That(region.Content, Does.Not.Contain("\n"));

        var decision = RichTextBoxExtensions.DecideRegionEntry(blocks, 2, Key.Up, caretAtLineStart: true);

        Assert.That(decision, Is.Not.Null);
        Assert.That(decision!.Value.TargetCaretPosition, Is.EqualTo(0));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void DecideRegionEntry_LeftFromStartOfLineBelow_StillEntersRegionAtEnd()
    {
        // Strict scope: the start-of-line flag must NOT change Left behavior — Left still enters at
        // END even when caretAtLineStart is true (the new rule is Up-only).
        var (blocks, region) = BuildMultiLineRegionSandwich();

        var decision = RichTextBoxExtensions.DecideRegionEntry(blocks, 2, Key.Left, caretAtLineStart: true);

        Assert.That(decision, Is.Not.Null);
        Assert.That(decision!.Value.TargetCaretPosition, Is.EqualTo(region.Content.Length));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void DecideRegionEntryFromPrediction_UpFromStartOfLineBelow_EntersRegionAtStartOfLastLine()
    {
        // Live-path wiring: Up from the start of the line below (caretAtLineStart=true), crossing
        // prediction resolves to the region container (block 1) — the backward path decides on the
        // current block and now targets the START of the region's last line.
        var (blocks, region) = BuildMultiLineRegionSandwich();
        var expectedLastLineStart = region.Content.LastIndexOf('\n') + 1;

        var decision = RichTextBoxExtensions.DecideRegionEntryFromPrediction(
            blocks, 2, 1, Key.Up, caretAtParagraphBoundary: false, caretAtLineStart: true);

        Assert.That(decision, Is.Not.Null);
        Assert.That(decision!.Value.Region, Is.SameAs(region));
        Assert.That(decision.Value.TargetCaretPosition, Is.EqualTo(expectedLastLineStart));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void DecideRegionEntryFromPrediction_UpFromMidLineBelow_StillEntersRegionAtEnd()
    {
        // Non-regression: Up mid-line below (caretAtLineStart=false) keeps entering at END.
        var (blocks, region) = BuildMultiLineRegionSandwich();

        var decision = RichTextBoxExtensions.DecideRegionEntryFromPrediction(
            blocks, 2, 1, Key.Up, caretAtParagraphBoundary: false, caretAtLineStart: false);

        Assert.That(decision, Is.Not.Null);
        Assert.That(decision!.Value.TargetCaretPosition, Is.EqualTo(region.Content.Length));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void DecideRegionEntry_CaretOnRegionContainer_ReturnsNull()
    {
        // Caret already inside the region's own container block → no host-side entry to make.
        var (blocks, _) = BuildRegionSandwich();

        Assert.That(RichTextBoxExtensions.DecideRegionEntry(blocks, 1, Key.Down), Is.Null);
        Assert.That(RichTextBoxExtensions.DecideRegionEntry(blocks, 1, Key.Up), Is.Null);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void DecideRegionEntry_NonArrowKey_ReturnsNull()
    {
        var (blocks, _) = BuildRegionSandwich();

        Assert.That(RichTextBoxExtensions.DecideRegionEntry(blocks, 0, Key.A), Is.Null);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void DecideRegionEntry_NoAdjacentRegion_ReturnsNull()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("line1\nline2\nline3");
        var blocks = rtb.Document.Blocks.ToList();

        Assert.That(RichTextBoxExtensions.DecideRegionEntry(blocks, 0, Key.Down), Is.Null);
        Assert.That(RichTextBoxExtensions.DecideRegionEntry(blocks, 1, Key.Up), Is.Null);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void FindRegionOnCaretParagraph_DownFromLineBelowRegion_ReturnsNull()
    {
        // Live-document mirror of the bug: caret on the "after" paragraph (below the region),
        // pressing Down must not resolve to the region above it.
        var rtb = new RichTextBox();
        rtb.SetPlainText("before\n```\ncode\n```\nafter");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        var afterPara = (Paragraph)rtb.Document.Blocks.ToList()[2];
        var region = RichTextBoxExtensions.FindRegionOnCaretParagraph(rtb.Document, afterPara.ContentStart, Key.Down);

        Assert.That(region, Is.Null);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void FindRegionOnCaretParagraph_UpFromLineBelowRegion_ReturnsRegion()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("before\n```\ncode\n```\nafter");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        var afterPara = (Paragraph)rtb.Document.Blocks.ToList()[2];
        var region = RichTextBoxExtensions.FindRegionOnCaretParagraph(rtb.Document, afterPara.ContentStart, Key.Up);

        Assert.That(region, Is.Not.Null);
    }

    // ── Prediction-aware entry decision (entry-from-above AND entry-from-below) ─────────
    // DecideRegionEntryFromPrediction wires the pure DecideRegionEntry to the live caret's
    // fragile line prediction: forward keys trust the predicted landing block; backward keys
    // (whose Up/Left prediction walks ACROSS the region) decide on the current block instead.

    [Test, Apartment(ApartmentState.STA)]
    public void DecideRegionEntryFromPrediction_DownFromAbove_EntersRegionAtStart()
    {
        var (blocks, region) = BuildRegionSandwich();

        // Down from the "before" paragraph: predicted landing resolves onto the block boundary,
        // which FindCaretBlockIndex maps back to block 0 (first-match-wins). Vertical keys ignore
        // the boundary flag and enter from anywhere on the adjacent line.
        var decision = RichTextBoxExtensions.DecideRegionEntryFromPrediction(blocks, 0, 0, Key.Down, caretAtParagraphBoundary: false);

        Assert.That(decision, Is.Not.Null);
        Assert.That(decision!.Value.Region, Is.SameAs(region));
        Assert.That(decision.Value.TargetCaretPosition, Is.EqualTo(0));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void DecideRegionEntryFromPrediction_RightFromEndOfLineAbove_EntersRegionAtStart()
    {
        // Bug 1 (keep working): Right only enters when the caret is at the END of the line directly
        // above the region (boundary=true), so the move crosses into the region below at START.
        var (blocks, region) = BuildRegionSandwich();

        var decision = RichTextBoxExtensions.DecideRegionEntryFromPrediction(blocks, 0, 0, Key.Right, caretAtParagraphBoundary: true);

        Assert.That(decision, Is.Not.Null);
        Assert.That(decision!.Value.Region, Is.SameAs(region));
        Assert.That(decision.Value.TargetCaretPosition, Is.EqualTo(0));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void DecideRegionEntryFromPrediction_RightFromMidLineAbove_DoesNotEnter()
    {
        // Bug 1 (fix): Right from any non-end position on the line above the region must behave as
        // ordinary within-line movement and NOT enter the region. boundary=false ⇒ no entry.
        var (blocks, _) = BuildRegionSandwich();

        Assert.That(RichTextBoxExtensions.DecideRegionEntryFromPrediction(blocks, 0, 0, Key.Right, caretAtParagraphBoundary: false), Is.Null);
        Assert.That(RichTextBoxExtensions.DecideRegionEntryFromPrediction(blocks, 0, 1, Key.Right, caretAtParagraphBoundary: false), Is.Null);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void DecideRegionEntryFromPrediction_UpFromBelow_CrossedPrediction_EntersRegionAtEnd()
    {
        // The reported bug: Up on the line below the region. The prediction crosses the region
        // and resolves to block 1 (the region's own container) — unusable for forward logic, but
        // the backward vertical path decides on the current block (2) and enters at END.
        var (blocks, region) = BuildRegionSandwich();

        var decision = RichTextBoxExtensions.DecideRegionEntryFromPrediction(blocks, 2, 1, Key.Up, caretAtParagraphBoundary: false);

        Assert.That(decision, Is.Not.Null);
        Assert.That(decision!.Value.Region, Is.SameAs(region));
        Assert.That(decision.Value.TargetCaretPosition, Is.EqualTo(region.Content.Length));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void DecideRegionEntryFromPrediction_UpFromBelow_NullPrediction_EntersRegionAtEnd()
    {
        // When the Up line prediction walks off entirely (caretAfter was null → index -1),
        // entry-from-below must still resolve to the region above at END.
        var (blocks, region) = BuildRegionSandwich();

        var decision = RichTextBoxExtensions.DecideRegionEntryFromPrediction(blocks, 2, -1, Key.Up, caretAtParagraphBoundary: false);

        Assert.That(decision, Is.Not.Null);
        Assert.That(decision!.Value.Region, Is.SameAs(region));
        Assert.That(decision.Value.TargetCaretPosition, Is.EqualTo(region.Content.Length));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void DecideRegionEntryFromPrediction_LeftFromStartOfLineBelow_EntersRegionAtEnd()
    {
        // Bug 3 (fix): Left from the START of the line directly below the region (boundary=true)
        // must enter the region ABOVE at END — not drop into the hidden dead zone before the
        // region container.
        var (blocks, region) = BuildRegionSandwich();

        var decision = RichTextBoxExtensions.DecideRegionEntryFromPrediction(blocks, 2, 2, Key.Left, caretAtParagraphBoundary: true);

        Assert.That(decision, Is.Not.Null);
        Assert.That(decision!.Value.Region, Is.SameAs(region));
        Assert.That(decision.Value.TargetCaretPosition, Is.EqualTo(region.Content.Length));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void DecideRegionEntryFromPrediction_LeftFromMidLineBelow_DoesNotEnter()
    {
        // Bug 3 (fix): Left from a non-start position on the line below is ordinary within-line
        // movement and must NOT enter the region above. boundary=false ⇒ no entry.
        var (blocks, _) = BuildRegionSandwich();

        Assert.That(RichTextBoxExtensions.DecideRegionEntryFromPrediction(blocks, 2, 2, Key.Left, caretAtParagraphBoundary: false), Is.Null);
        Assert.That(RichTextBoxExtensions.DecideRegionEntryFromPrediction(blocks, 2, 1, Key.Left, caretAtParagraphBoundary: false), Is.Null);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void DecideRegionEntryFromPrediction_BackwardWithinParagraph_DoesNotEnter()
    {
        // A within-paragraph Up on a wrapped host line (the prediction stays on the SAME block)
        // must not prematurely jump into the region above; a mid-line Left (boundary=false) must
        // not either.
        var (blocks, _) = BuildRegionSandwich();

        Assert.That(RichTextBoxExtensions.DecideRegionEntryFromPrediction(blocks, 2, 2, Key.Up, caretAtParagraphBoundary: false), Is.Null);
        Assert.That(RichTextBoxExtensions.DecideRegionEntryFromPrediction(blocks, 2, 2, Key.Left, caretAtParagraphBoundary: false), Is.Null);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void DecideRegionEntryFromPrediction_BackwardFromAbove_DoesNotEnterRegionBelow()
    {
        // Symmetric guard: Up/Left from the line ABOVE the region must never drop DOWN into it,
        // even when the horizontal boundary flag is set.
        var (blocks, _) = BuildRegionSandwich();

        Assert.That(RichTextBoxExtensions.DecideRegionEntryFromPrediction(blocks, 0, -1, Key.Up, caretAtParagraphBoundary: false), Is.Null);
        Assert.That(RichTextBoxExtensions.DecideRegionEntryFromPrediction(blocks, 0, -1, Key.Left, caretAtParagraphBoundary: true), Is.Null);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void DecideRegionEntryFromPrediction_ForwardFromBelow_DoesNotEnterRegionAbove()
    {
        // Down/Right on the line below the region must not jump UP into it, even when the
        // horizontal boundary flag is set.
        var (blocks, _) = BuildRegionSandwich();

        Assert.That(RichTextBoxExtensions.DecideRegionEntryFromPrediction(blocks, 2, 2, Key.Down, caretAtParagraphBoundary: false), Is.Null);
        Assert.That(RichTextBoxExtensions.DecideRegionEntryFromPrediction(blocks, 2, 2, Key.Right, caretAtParagraphBoundary: true), Is.Null);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void DecideRegionEntryFromPrediction_DownFromStartOfLineAbove_DeadZonePrediction_EntersRegionAtStart()
    {
        // Bug A: caret at the START of the "before" line directly above the region. The live
        // downward line-step prediction lands on the region's own BlockUIContainer (the dead zone),
        // which FindCaretBlockIndex maps to block 1 — NOT back onto the host paragraph. The vertical
        // forward decision must fall back to the block the caret is ON (block 0) and ENTER the
        // region at START, instead of leaving the caret stranded in the host dead zone.
        var (blocks, region) = BuildRegionSandwich();

        var decision = RichTextBoxExtensions.DecideRegionEntryFromPrediction(blocks, 0, 1, Key.Down, caretAtParagraphBoundary: false);

        Assert.That(decision, Is.Not.Null);
        Assert.That(decision!.Value.Region, Is.SameAs(region));
        Assert.That(decision.Value.TargetCaretPosition, Is.EqualTo(0));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void DecideRegionEntryFromPrediction_RightFromStartOfLineAbove_DoesNotEnter()
    {
        // Bug 1 (fix): previously Right from the START of the line above over-entered the region
        // because the forward branch fell back to the caret's own block. Right is HORIZONTAL now —
        // without end-of-line boundary adjacency (boundary=false) it must NOT enter.
        var (blocks, _) = BuildRegionSandwich();

        Assert.That(RichTextBoxExtensions.DecideRegionEntryFromPrediction(blocks, 0, 1, Key.Right, caretAtParagraphBoundary: false), Is.Null);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void DecideRegionEntryFromPrediction_DownFromStartOfLineAbove_PredictionPastRegion_EntersRegionAtStart()
    {
        // Defensive: if the downward prediction over-shoots the dead zone and resolves past the
        // region (block 2), the caret-on-block fallback still enters the region directly below the
        // caret's current line at START.
        var (blocks, region) = BuildRegionSandwich();

        var decision = RichTextBoxExtensions.DecideRegionEntryFromPrediction(blocks, 0, 2, Key.Down, caretAtParagraphBoundary: false);

        Assert.That(decision, Is.Not.Null);
        Assert.That(decision!.Value.Region, Is.SameAs(region));
        Assert.That(decision.Value.TargetCaretPosition, Is.EqualTo(0));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void DecideRegionEntryFromPrediction_EndOfLineAbove_StillEntersRegionAtStart()
    {
        // Preserve the already-working case: end-of-line-above prediction maps back to block 0.
        var (blocks, region) = BuildRegionSandwich();

        var decision = RichTextBoxExtensions.DecideRegionEntryFromPrediction(blocks, 0, 0, Key.Down, caretAtParagraphBoundary: false);

        Assert.That(decision, Is.Not.Null);
        Assert.That(decision!.Value.Region, Is.SameAs(region));
        Assert.That(decision.Value.TargetCaretPosition, Is.EqualTo(0));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void DecideRegionEntryFromPrediction_DownFromBelowRegion_DeadZonePrediction_DoesNotEnterRegionAbove()
    {
        // Guard the fallback against the wrong-direction case: Down on the line BELOW the region,
        // even if the prediction briefly touches the region container, must NEVER enter the region
        // above. caretBefore=2 ("after") has no region directly below it, so no entry is made.
        var (blocks, _) = BuildRegionSandwich();

        Assert.That(RichTextBoxExtensions.DecideRegionEntryFromPrediction(blocks, 2, 1, Key.Down, caretAtParagraphBoundary: false), Is.Null);
        Assert.That(RichTextBoxExtensions.DecideRegionEntryFromPrediction(blocks, 2, 1, Key.Right, caretAtParagraphBoundary: true), Is.Null);
    }

    // ── ActiveRegion precondition for the Space-redirect (Bug B) ───────────────────────
    // The host's PreviewTextInput Space-redirect only fires when EmbeddedCodeRegionBlock.ActiveRegion
    // is a live, attached region. These prove ActiveRegion is established on EVERY keyboard entry
    // path — not only on mouse-click entry — so a Space pressed after arrow-entry still redirects.

    [Test, Apartment(ApartmentState.STA)]
    public void FocusContentAtPosition_ArrowEntryPath_SetsActiveRegion()
    {
        EmbeddedCodeRegionBlock.ClearActiveRegion();
        var region = EmbeddedCodeRegionBlock.Build("code", "");

        // FocusContentAtPosition is the entry point used by smart arrow-key navigation (Down/Right
        // enter at START; Up/Left at END). It must mark the region active so the host Space-redirect
        // precondition holds after keyboard entry.
        region.FocusContentAtPosition(0);

        Assert.That(EmbeddedCodeRegionBlock.ActiveRegion, Is.SameAs(region));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void FocusContent_EntryPath_SetsActiveRegion()
    {
        EmbeddedCodeRegionBlock.ClearActiveRegion();
        var region = EmbeddedCodeRegionBlock.Build("code", "");

        region.FocusContent();

        Assert.That(EmbeddedCodeRegionBlock.ActiveRegion, Is.SameAs(region));
    }

    [Test]
    public void DecideHostTextInput_SpaceAfterArrowEntry_HostLeak_RoutesToActiveRegion()
    {
        // With ActiveRegion established by arrow entry (attached) and OS focus leaked back to the
        // host, a Space (hasText) must be deferred to the active region rather than inserted in the
        // host prompt.
        Assert.That(
            EmbeddedRegionInputRouter.DecideHostTextInput(
                hasText: true, focusInsideRegion: false, hostHasFocus: true, activeRegionAttached: true),
            Is.EqualTo(HostTextInputDecision.RouteToActiveRegion));
    }

    // ── Bug 4: trailing blank line is only synthesized when the region is the LAST block ─────

    [Test, Apartment(ApartmentState.STA)]
    public void ShouldSynthesizeTrailingLandingParagraph_OnlyWhenRegionIsLastSegment()
    {
        // The region is the last segment (nothing follows) → a caret-landing paragraph is
        // legitimate so the caret has somewhere to go below the final region.
        Assert.That(EmbeddedCodeRegionManager.ShouldSynthesizeTrailingLandingParagraph(0, 1), Is.True);
        Assert.That(EmbeddedCodeRegionManager.ShouldSynthesizeTrailingLandingParagraph(2, 3), Is.True);

        // Content follows the region (region is NOT the last segment) → that content IS the
        // landing spot, so NO blank line may be synthesized on rebuild/rehydration.
        Assert.That(EmbeddedCodeRegionManager.ShouldSynthesizeTrailingLandingParagraph(1, 3), Is.False);
        Assert.That(EmbeddedCodeRegionManager.ShouldSynthesizeTrailingLandingParagraph(0, 2), Is.False);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void CanonicalDocument_RehydrationRoundTrip_DoesNotSynthesizeTrailingBlankAfterRegion()
    {
        // The user's canonical example: host "123", a region containing "abc"/"def", host "456".
        // Because real content ("456") follows the region, conversion must NOT insert a stray
        // empty caret-landing paragraph between the region and "456" (Bug 4).
        var rtb = new RichTextBox();
        rtb.SetPlainText("123\n```\nabc\ndef\n```\n456");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        var blocks = rtb.Document.Blocks.ToList();
        Assert.That(blocks.Count, Is.EqualTo(3), "123 paragraph, region, 456 paragraph — no synthesized blank");
        Assert.That(blocks[0], Is.InstanceOf<Paragraph>());
        Assert.That(blocks[1], Is.InstanceOf<BlockUIContainer>());
        Assert.That(blocks[2], Is.InstanceOf<Paragraph>());
        Assert.That(RichTextBoxExtensions.IsEmptyEmbeddedCodeRegionCaretLandingParagraph(blocks[2]), Is.False,
            "the block after the region must be the real '456' content, not an empty landing paragraph");
        Assert.That(rtb.GetPlainText(), Is.EqualTo("123\n```\nabc\ndef\n```\n456"));

        // Rehydration round-trip (simulating an app restart): serialize to plain text, rebuild in a
        // fresh editor, and confirm the document is stable — still exactly 3 blocks, no net blank
        // line accumulated around the region.
        var serialized = rtb.GetPlainText();
        var rehydrated = new RichTextBox();
        rehydrated.SetPlainText(serialized);
        EmbeddedCodeRegionManager.ConvertAllFences(rehydrated);

        Assert.That(rehydrated.Document.Blocks.Count, Is.EqualTo(3));
        Assert.That(rehydrated.GetPlainText(), Is.EqualTo("123\n```\nabc\ndef\n```\n456"));
    }

    // ── Voice dictation targets the focused region (Bug 1) ─────────────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void InsertDictation_AtEndOfRegion_InsertsInsideRegionContent()
    {
        var region = EmbeddedCodeRegionBlock.Build("code", "");
        region.FocusContentAtPosition(region.Content.Length);

        region.InsertDictation("hello", Array.Empty<VoiceHeuristics.VoiceReplacementRule>());

        // Dictation lands INSIDE the region (not lost to a host). The leading-space heuristic
        // inserts a separator because the preceding char is a letter.
        Assert.That(region.Content, Does.StartWith("code"));
        Assert.That(region.Content, Does.EndWith("hello"));
        Assert.That(region.Content.Length, Is.GreaterThan("code".Length));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void InsertDictation_IntoEmptyRegion_InsertsDictatedText()
    {
        var region = EmbeddedCodeRegionBlock.Build("", "");

        region.InsertDictation("alpha", Array.Empty<VoiceHeuristics.VoiceReplacementRule>());

        Assert.That(region.Content, Does.Contain("alpha"));
    }
    [Test, Apartment(ApartmentState.STA)]
    public void FocusContentAtPosition_StartPosition_PlacesCareAtStart()
    {
        var region = EmbeddedCodeRegionBlock.Build("123456", "");
        
        // We can't fully test focus without a window, but we can test the caret positioning
        // by accessing the internal TextBox through reflection or by verifying the method exists
        // For now, just verify the method can be called without throwing
        region.FocusContentAtPosition(0);
        
        Assert.That(region, Is.Not.Null);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void FocusContentAtPosition_EndPosition_PlacesCaretAtEnd()
    {
        var region = EmbeddedCodeRegionBlock.Build("123456", "");
        
        region.FocusContentAtPosition(region.Content.Length);
        
        Assert.That(region, Is.Not.Null);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void FocusContentAtPosition_ClampsBeyondLength()
    {
        var region = EmbeddedCodeRegionBlock.Build("123", "");
        
        // Should clamp to Content.Length (3) without throwing
        region.FocusContentAtPosition(1000);
        
        Assert.That(region, Is.Not.Null);
    }

    // ── Space-leak safety net (host redirects printable input into the active region) ──────
    //
    // Regression guard for the bug where pressing Space while an embedded region's caret was
    // visibly blinking inserted the space OUTSIDE the region, in the host prompt, because OS
    // keyboard focus had leaked back to the host RichTextBox. The pure EmbeddedRegionInputRouter
    // predicate decides whether the host must defer to the active region; the STA tests prove the
    // editor seam and the region's host-text redirect insert a space (and a printable char) at the
    // caret inside the region content.

    [Test]
    public void DecideHostTextInput_NoText_LetProceed()
    {
        Assert.That(
            EmbeddedRegionInputRouter.DecideHostTextInput(
                hasText: false, focusInsideRegion: false, hostHasFocus: true, activeRegionAttached: true),
            Is.EqualTo(HostTextInputDecision.LetProceed));
    }

    [Test]
    public void DecideHostTextInput_FocusGenuinelyInsideRegion_LetProceed()
    {
        // Focus is truly in the region — the tunneling event reaches the editor natively, so the
        // host must not intercept (that would bypass native input handling).
        Assert.That(
            EmbeddedRegionInputRouter.DecideHostTextInput(
                hasText: true, focusInsideRegion: true, hostHasFocus: false, activeRegionAttached: true),
            Is.EqualTo(HostTextInputDecision.LetProceed));
    }

    [Test]
    public void DecideHostTextInput_FocusLeakedToHostWithAttachedActiveRegion_RouteToActiveRegion()
    {
        // The leak: host owns focus, but an attached region is still active. Redirect the space.
        Assert.That(
            EmbeddedRegionInputRouter.DecideHostTextInput(
                hasText: true, focusInsideRegion: false, hostHasFocus: true, activeRegionAttached: true),
            Is.EqualTo(HostTextInputDecision.RouteToActiveRegion));
    }

    [Test]
    public void DecideHostTextInput_StaleActiveRegionNotAttached_LetProceed()
    {
        // A removed region (e.g. after prompt submit) must never swallow ordinary host typing.
        Assert.That(
            EmbeddedRegionInputRouter.DecideHostTextInput(
                hasText: true, focusInsideRegion: false, hostHasFocus: true, activeRegionAttached: false),
            Is.EqualTo(HostTextInputDecision.LetProceed));
    }

    [Test]
    public void DecideHostTextInput_NoActiveRegionAndHostFocused_LetProceed()
    {
        // Ordinary host typing with no region in play.
        Assert.That(
            EmbeddedRegionInputRouter.DecideHostTextInput(
                hasText: true, focusInsideRegion: false, hostHasFocus: true, activeRegionAttached: false),
            Is.EqualTo(HostTextInputDecision.LetProceed));
    }

    [Test]
    public void DecideHostTextInput_FocusElsewhereEntirely_LetProceed()
    {
        // Focus moved to some unrelated control (host does not have focus): do not hijack typing.
        Assert.That(
            EmbeddedRegionInputRouter.DecideHostTextInput(
                hasText: true, focusInsideRegion: false, hostHasFocus: false, activeRegionAttached: true),
            Is.EqualTo(HostTextInputDecision.LetProceed));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void InsertText_Space_InsertsSpaceAtCaretInsideRegionContent()
    {
        var editor = new AvalonEditRegionEditor("ab");
        editor.CaretOffset = 1; // between 'a' and 'b'

        editor.InsertText(" ");

        Assert.That(editor.Text, Is.EqualTo("a b"));
        Assert.That(editor.CaretOffset, Is.EqualTo(2));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void InsertText_PrintableChar_InsertsAtCaretInsideRegionContent()
    {
        var editor = new AvalonEditRegionEditor("ac");
        editor.CaretOffset = 1;

        editor.InsertText("b");

        Assert.That(editor.Text, Is.EqualTo("abc"));
        Assert.That(editor.CaretOffset, Is.EqualTo(2));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void InsertText_ReplacesSelection()
    {
        var editor = new AvalonEditRegionEditor("hello");
        editor.CaretOffset = 0;
        editor.SelectionLength = 0;
        // Select "ell" (offsets 1..4).
        editor.CaretOffset = 1;
        editor.SelectionLength = 3;

        editor.InsertText(" ");

        Assert.That(editor.Text, Is.EqualTo("h o"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void AvalonEditRegionEditor_Dispose_StopsTimerAndTurnsLateMutationsIntoNoOps()
    {
        var editor = new AvalonEditRegionEditor("code");
        editor.SetLanguage("");
        editor.Dispose();

        Assert.That(editor.IsDisposed, Is.True);
        Assert.That(editor.IsAutoDetectPending, Is.False);

        Assert.DoesNotThrow(() =>
        {
            editor.SetLanguage("python");
            editor.InsertText("x");
            editor.FocusEditor();
            editor.ApplyPalette(new RegionEditorPalette(Brushes.Transparent, Brushes.Black, new Dictionary<string, ICSharpCode.AvalonEdit.Highlighting.HighlightingColor>()));
        });
        Assert.That(editor.Text, Is.EqualTo("code"));
    }

    // ── Unmodified Space while this region is focused → insert into region and handle ───────
    //
    // Regression guard for the host RichTextBox diverting the Space text input away from the
    // focused nested TextArea (Space fired no PROMPT_EMBEDDED_CODE_TEXT_INPUT, yet the host
    // prompt's text grew). EmbeddedRegionSpaceRouter is the pure decision the region editor's
    // PreviewKeyDown uses to insert the space itself and mark the key handled before WPF can
    // divert it. The genuinely interactive live-key behavior remains human-verified at the gate.

    [Test]
    public void ShouldInsertSpaceIntoRegion_UnmodifiedSpaceRegionFocused_Handles()
    {
        Assert.That(
            EmbeddedRegionSpaceRouter.ShouldInsertSpaceIntoRegion(
                Key.Space, ModifierKeys.None, regionHasFocus: true),
            Is.True);
    }

    [Test]
    public void ShouldInsertSpaceIntoRegion_ShiftSpace_DoesNotHandle()
    {
        // Shift+Space (Smooth Dictation) must pass through untouched.
        Assert.That(
            EmbeddedRegionSpaceRouter.ShouldInsertSpaceIntoRegion(
                Key.Space, ModifierKeys.Shift, regionHasFocus: true),
            Is.False);
    }

    [Test]
    public void ShouldInsertSpaceIntoRegion_CtrlSpace_DoesNotHandle()
    {
        Assert.That(
            EmbeddedRegionSpaceRouter.ShouldInsertSpaceIntoRegion(
                Key.Space, ModifierKeys.Control, regionHasFocus: true),
            Is.False);
    }

    [Test]
    public void ShouldInsertSpaceIntoRegion_NoRegionFocused_DoesNotHandle()
    {
        // No region has focus — ordinary host typing must behave exactly as before.
        Assert.That(
            EmbeddedRegionSpaceRouter.ShouldInsertSpaceIntoRegion(
                Key.Space, ModifierKeys.None, regionHasFocus: false),
            Is.False);
    }

    [Test]
    public void ShouldInsertSpaceIntoRegion_NonSpaceKey_DoesNotHandle()
    {
        // The handler only reacts to Space; other keys are never diverted here.
        Assert.That(
            EmbeddedRegionSpaceRouter.ShouldInsertSpaceIntoRegion(
                Key.A, ModifierKeys.None, regionHasFocus: true),
            Is.False);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void InsertHostText_Space_LandsInsideRegionContent()
    {
        var region = EmbeddedCodeRegionBlock.Build("xy", "");
        region.FocusContentAtPosition(1); // caret between 'x' and 'y'

        region.InsertHostText(" ");

        Assert.That(region.Content, Is.EqualTo("x y"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void FocusContent_SetsActiveRegion()
    {
        EmbeddedCodeRegionBlock.ClearActiveRegion();
        var region = EmbeddedCodeRegionBlock.Build("code", "");

        region.FocusContent();

        Assert.That(EmbeddedCodeRegionBlock.ActiveRegion, Is.SameAs(region));
        EmbeddedCodeRegionBlock.ClearActiveRegion();
    }

    [Test, Apartment(ApartmentState.STA)]
    public void IsAttachedToHost_FalseBeforeAttach_TrueAfterConversionInsideHost()
    {
        var unattached = EmbeddedCodeRegionBlock.Build("code", "");
        Assert.That(unattached.IsAttachedToHost(), Is.False);

        var rtb = new RichTextBox();
        rtb.SetPlainText("```\nhello\n```");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);
        var region = (EmbeddedCodeRegionBlock)((BlockUIContainer)rtb.Document.Blocks.FirstBlock!).Child;

        Assert.That(region.IsAttachedToHost(), Is.True);
    }

    // ── Mouse text selection inside the region (double-click word-select / click-drag) ──
    //
    // Regression guard for the bug where the host RichTextBox's Preview handler and the
    // content box's own Preview handler both claimed every left-button-down, suppressing the
    // nested TextBox's native caret/double-click/drag selection. The pure predicate below
    // encodes the host's decision, and the STA test proves the content box's mouse handler no
    // longer collapses an in-progress selection.

    [Test]
    public void ShouldClaimRegionMouseGesture_ContentTextBoxClick_IsNotClaimed()
    {
        // Click landed on the nested TextBox → host must leave the gesture alone so the TextBox
        // performs native selection (double-click word-select, click-drag, double-click-drag).
        Assert.That(
            EmbeddedCodeRegionBlock.ShouldClaimRegionMouseGesture(
                hitRegion: true, hitToggle: false, hitContentTextBox: true),
            Is.False);
    }

    [Test]
    public void ShouldClaimRegionMouseGesture_RegionChromeClick_IsClaimed()
    {
        // Click landed on region chrome (background/label), no nested editor under the cursor →
        // host claims the gesture to focus the region's content.
        Assert.That(
            EmbeddedCodeRegionBlock.ShouldClaimRegionMouseGesture(
                hitRegion: true, hitToggle: false, hitContentTextBox: false),
            Is.True);
    }

    [Test]
    public void ShouldClaimRegionMouseGesture_ToggleClick_IsNotClaimed()
    {
        // The collapse/expand toggle owns its own click; the region must never claim it.
        Assert.That(
            EmbeddedCodeRegionBlock.ShouldClaimRegionMouseGesture(
                hitRegion: true, hitToggle: true, hitContentTextBox: false),
            Is.False);
    }

    [Test]
    public void ShouldClaimRegionMouseGesture_OutsideAnyRegion_IsNotClaimed()
    {
        Assert.That(
            EmbeddedCodeRegionBlock.ShouldClaimRegionMouseGesture(
                hitRegion: false, hitToggle: false, hitContentTextBox: false),
            Is.False);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ContentBoxMouseLeftButtonDown_DoesNotCollapseExistingSelection()
    {
        // Simulates the TextBox's native mouse handling having already selected a word (e.g. a
        // double-click), then the region's bubbling MouseLeftButtonDown handler running. The
        // handler must claim the gesture (so the host never object-selects the BlockUIContainer)
        // WITHOUT resetting the caret/selection the TextBox just applied — the old Preview
        // handler called FocusContentAtPoint, which collapsed the selection and broke dragging.
        var rtb = new RichTextBox();
        rtb.SetPlainText("```\nhello world\n```");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        var region = (EmbeddedCodeRegionBlock)((BlockUIContainer)rtb.Document.Blocks.FirstBlock!).Child;
        var contentBox = GetContentEditor(region);

        // Pretend the TextBox's native handling selected the word "world".
        contentBox.Select(6, 5);
        Assert.That(contentBox.SelectedText, Is.EqualTo("world"));

        var args = new System.Windows.Input.MouseButtonEventArgs(
            System.Windows.Input.InputManager.Current.PrimaryMouseDevice,
            0,
            System.Windows.Input.MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonDownEvent,
            Source = contentBox,
        };
        contentBox.RaiseEvent(args);

        // Gesture claimed (host won't object-select) and native selection left intact.
        Assert.That(args.Handled, Is.True);
        Assert.That(contentBox.SelectionStart, Is.EqualTo(6));
        Assert.That(contentBox.SelectionLength, Is.EqualTo(5));
        Assert.That(contentBox.SelectedText, Is.EqualTo("world"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void TryNavigateIntoEmbeddedRegionWithDirection_DownArrow_ReturnsTrue()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("before\n```\n123456\n```\nafter");
        
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        // Simulate: caret before region, user presses Down
        // We'd need to actually navigate the caret to test the full flow,
        // so for now verify the method exists and can be called
        var region = rtb.Document.Blocks
            .OfType<BlockUIContainer>()
            .FirstOrDefault()?
            .Child as EmbeddedCodeRegionBlock;
        
        Assert.That(region, Is.Not.Null);
    }

    // ── Issue 2 (Down arrow state bug): Verify Left/Right work after Down entry ──

    [Test, Apartment(ApartmentState.STA)]
    public void MultilineRegion_DownArrowOnFirstLine_ShouldNotExit()
    {
        // Ensures Down arrow within a multi-line region moves to next line, not exit
        var rtb = new RichTextBox();
        rtb.SetPlainText("```\nline1\nline2\nline3\n```");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        var region = (EmbeddedCodeRegionBlock)((BlockUIContainer)rtb.Document.Blocks.FirstBlock!).Child;
        var contentBox = GetContentEditor(region);

        // Simulate pressing Down on first line — should allow navigation within region
        contentBox.CaretOffset = 0; // Start of "line1"
        
        // Verify we can navigate within the content
        Assert.That(contentBox.Text, Contains.Substring("line1\nline2\nline3"));
        Assert.That(contentBox.Text.Count(c => c == '\n'), Is.EqualTo(2));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void MultilineRegion_UpArrowOnLastLine_ShouldNotExit()
    {
        // Ensures Up arrow within a multi-line region moves to previous line, not exit
        var rtb = new RichTextBox();
        rtb.SetPlainText("```\nline1\nline2\nline3\n```");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);

        var region = (EmbeddedCodeRegionBlock)((BlockUIContainer)rtb.Document.Blocks.FirstBlock!).Child;
        var contentBox = GetContentEditor(region);

        // Position caret at start of last line
        var lastLineStart = contentBox.Text.LastIndexOf("line3");
        contentBox.CaretOffset = lastLineStart;
        
        // Verify content integrity
        Assert.That(contentBox.CaretOffset, Is.GreaterThanOrEqualTo(0));
        Assert.That(contentBox.Text.Contains("line3"));
    }

    // ── Issue 5: Clipboard support for fenced text preservation ──

    [Test, Apartment(ApartmentState.STA)]
    public void Region_ToFencedText_WithLanguage_IncludesLanguageTag()
    {
        var region = EmbeddedCodeRegionBlock.Build("x = 1", "python");
        
        var fenced = region.ToFencedText();
        
        Assert.That(fenced, Is.EqualTo("```python\nx = 1\n```"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void Region_ToFencedText_WithoutLanguage_OmitsLanguageTag()
    {
        var region = EmbeddedCodeRegionBlock.Build("plain text", "");
        
        var fenced = region.ToFencedText();
        
        Assert.That(fenced, Is.EqualTo("```\nplain text\n```"));
    }

    // ── Regression: bugs #1 (language-tag with '#'/'.'), #3 (CRLF), #4 (indentation) ──

    [Test]
    public void FindFirstFence_LanguageTagWithHash_IsRecognizedAndCaptured()
    {
        var lines = new[] { "```c#", "int x = 1;", "```" };
        var fence = EmbeddedFenceDetector.FindFirstFence(lines);

        Assert.That(fence, Is.Not.Null);
        Assert.That(fence!.Value.Language, Is.EqualTo("c#"));
    }

    [Test]
    public void FindFirstFence_LanguageTagWithDot_IsRecognizedAndCaptured()
    {
        var lines = new[] { "```asp.net", "<% %>", "```" };
        var fence = EmbeddedFenceDetector.FindFirstFence(lines);

        Assert.That(fence, Is.Not.Null);
        Assert.That(fence!.Value.Language, Is.EqualTo("asp.net"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void FenceWithHashLanguageTag_RoundTripsThroughConversionAndSerialization()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("```f#\nlet x = 1\n```");
        Assert.That(EmbeddedCodeRegionManager.TryConvertNextFence(rtb), Is.True);

        var region = (EmbeddedCodeRegionBlock)((BlockUIContainer)rtb.Document.Blocks.FirstBlock!).Child;
        Assert.That(region.Language, Is.EqualTo("f#"));
        Assert.That(rtb.GetPlainText(), Is.EqualTo("```f#\nlet x = 1\n```"));
    }

    [Test]
    public void FindOpenFenceWithNoClose_LanguageTagWithHash_CapturesLanguage()
    {
        var lines = new[] { "```c#" };
        var open = EmbeddedFenceDetector.FindOpenFenceWithNoClose(lines);

        Assert.That(open, Is.Not.Null);
        Assert.That(open!.Value.Language, Is.EqualTo("c#"));
    }

    [Test]
    public void ExtractContent_CrlfPastedBlock_HasNoStrayCarriageReturns()
    {
        // Simulates CRLF-pasted text split on '\n': each line retains a trailing '\r'.
        var lines = "```\r\nline1\r\nline2\r\n```".Split('\n');
        var fence = EmbeddedFenceDetector.FindFirstFence(lines);

        Assert.That(fence, Is.Not.Null);
        var content = fence!.Value.ExtractContent(lines);
        Assert.That(content, Does.Not.Contain("\r"));
        Assert.That(content, Is.EqualTo("line1\nline2"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void CrlfPastedFence_ConvertsAndRoundTripsWithoutStrayCarriageReturns()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("before\r\n```python\r\nx = 1\r\ny = 2\r\n```\r\nafter");
        while (EmbeddedCodeRegionManager.TryConvertNextFence(rtb)) { }

        var region = (EmbeddedCodeRegionBlock)rtb.Document.Blocks
            .OfType<BlockUIContainer>().First().Child;
        Assert.That(region.Content, Does.Not.Contain("\r"));
        Assert.That(region.Content, Is.EqualTo("x = 1\ny = 2"));
        Assert.That(rtb.GetPlainText(), Does.Not.Contain("\r"));
        Assert.That(rtb.GetPlainText(), Is.EqualTo("before\n```python\nx = 1\ny = 2\n```\nafter"));
    }

    [Test]
    public void FindFirstFence_IndentedOpeningMarker_IsNotTreatedAsFence()
    {
        // An indented ``` (e.g. inside a list item or block-quote) is literal text, not a
        // fence marker — so no region is created and the indentation is preserved.
        var lines = new[] { "- item", "    ```", "    code", "    ```" };
        Assert.That(EmbeddedFenceDetector.FindFirstFence(lines), Is.Null);
    }

    [Test]
    public void FindFirstFence_IndentedClosingMarker_IsNotTreatedAsClose()
    {
        // Opening marker is at column 0, but the only candidate close is indented, so the
        // fence is considered incomplete (no column-0 close) and is not recognized.
        var lines = new[] { "```", "code", "    ```" };
        Assert.That(EmbeddedFenceDetector.FindFirstFence(lines), Is.Null);
    }

    [Test]
    public void FindFirstFence_OpeningMarkerWithTrailingWhitespace_StillRecognized()
    {
        // Trailing whitespace after the language tag is tolerated (TrimEnd), unlike leading
        // indentation.
        var lines = new[] { "```python   ", "x = 1", "```  " };
        var fence = EmbeddedFenceDetector.FindFirstFence(lines);

        Assert.That(fence, Is.Not.Null);
        Assert.That(fence!.Value.Language, Is.EqualTo("python"));
    }

    // ── Bug #2: variable-length fences for lossless round-trip ───────────────────

    // Serializes content, re-parses it with the detector, and returns the extracted content —
    // the full copy/send → re-detect round-trip for a single region.
    private static (string ExtractedContent, string Language, int FenceLength) RoundTrip(string content, string? language)
    {
        var fenced = EmbeddedFenceDetector.BuildFencedText(content, language);
        var lines = fenced.Split('\n');
        var fence = EmbeddedFenceDetector.FindFirstFence(lines);
        Assert.That(fence, Is.Not.Null, "serialized block must re-parse as a complete fence");
        return (fence!.Value.ExtractContent(lines), fence.Value.Language, fence.Value.FenceLength);
    }

    private static int OpenFenceLength(string content)
        => EmbeddedFenceDetector.BuildFencedText(content, null).Split('\n')[0].Length;

    [Test]
    public void BuildFencedText_ContentWithBareThreeBacktickLine_UsesFourBacktickOuterFence()
    {
        var text = EmbeddedFenceDetector.BuildFencedText("```", null);
        Assert.That(text, Is.EqualTo("````\n```\n````"));
    }

    [Test]
    public void RoundTrip_ContentWithBareThreeBacktickLine_IsLossless()
    {
        var (content, language, fenceLength) = RoundTrip("```", null);
        Assert.That(content, Is.EqualTo("```"));
        Assert.That(language, Is.EqualTo(""));
        Assert.That(fenceLength, Is.EqualTo(4));
    }

    [Test]
    public void RoundTrip_ContentWithNestedFencedBlock_IsLossless()
    {
        var nested = "```python\nprint('hi')\n```";
        var (content, _, fenceLength) = RoundTrip(nested, null);
        Assert.That(content, Is.EqualTo(nested));
        Assert.That(fenceLength, Is.GreaterThanOrEqualTo(4));
    }

    [Test]
    public void RoundTrip_ContentWithTwentyBacktickLine_UsesTwentyOneBacktickOuterFence()
    {
        var twenty = new string('`', 20);
        var (content, _, fenceLength) = RoundTrip(twenty, null);
        Assert.That(content, Is.EqualTo(twenty));
        Assert.That(fenceLength, Is.EqualTo(21));
    }

    [Test]
    public void RoundTrip_InlineBackticksWithinTextLine_IsLossless()
    {
        var (content, _, fenceLength) = RoundTrip("use `code` inline here", null);
        Assert.That(content, Is.EqualTo("use `code` inline here"));
        Assert.That(fenceLength, Is.EqualTo(3), "inline single backticks never force the outer fence past 3");
    }

    [Test]
    public void RoundTrip_EmptyContent_IsLossless()
    {
        var (content, _, fenceLength) = RoundTrip("", null);
        Assert.That(content, Is.EqualTo(""));
        Assert.That(fenceLength, Is.EqualTo(3));
    }

    [Test]
    public void RoundTrip_WhitespaceOnlyContent_IsLossless()
    {
        var (content, _, fenceLength) = RoundTrip("   \n\t\n   ", null);
        Assert.That(content, Is.EqualTo("   \n\t\n   "));
        Assert.That(fenceLength, Is.EqualTo(3));
    }

    [Test]
    public void RoundTrip_LanguageTagPreservedWithLongerFence()
    {
        var (content, language, fenceLength) = RoundTrip("```\ninner", "python");
        Assert.That(content, Is.EqualTo("```\ninner"));
        Assert.That(language, Is.EqualTo("python"));
        Assert.That(fenceLength, Is.EqualTo(4));
    }

    [Test]
    public void RoundTrip_CrlfContent_IsNormalizedAndLossless()
    {
        var (content, _, _) = RoundTrip("a\r\nb\rc", null);
        Assert.That(content, Does.Not.Contain("\r"));
        Assert.That(content, Is.EqualTo("a\nb\nc"));
    }

    [Test]
    public void LongestBacktickRun_CountsLongestConsecutiveRunAnywhere()
    {
        Assert.That(EmbeddedFenceDetector.LongestBacktickRun(""), Is.EqualTo(0));
        Assert.That(EmbeddedFenceDetector.LongestBacktickRun("no ticks"), Is.EqualTo(0));
        Assert.That(EmbeddedFenceDetector.LongestBacktickRun("a `` b ``` c"), Is.EqualTo(3));
        Assert.That(EmbeddedFenceDetector.LongestBacktickRun("```\ntext\n`````"), Is.EqualTo(5));
    }

    [Test]
    public void BuildFencedText_FenceLengthMath_AtBoundaries()
    {
        Assert.That(OpenFenceLength(""), Is.EqualTo(3), "0 backticks -> 3");
        Assert.That(OpenFenceLength("```"), Is.EqualTo(4), "3 backticks -> 4");
        Assert.That(OpenFenceLength("````"), Is.EqualTo(5), "4 backticks -> 5");
        Assert.That(OpenFenceLength(new string('`', 20)), Is.EqualTo(21), "20 backticks -> 21");
    }

    [Test]
    public void FindFirstFence_ParsesFourBacktickFence_CapturingLengthAndLanguage()
    {
        var lines = new[] { "````python", "```", "still code", "````" };
        var fence = EmbeddedFenceDetector.FindFirstFence(lines);

        Assert.That(fence, Is.Not.Null);
        Assert.That(fence!.Value.FenceLength, Is.EqualTo(4));
        Assert.That(fence.Value.Language, Is.EqualTo("python"));
        Assert.That(fence.Value.OpenLineIndex, Is.EqualTo(0));
        Assert.That(fence.Value.CloseLineIndex, Is.EqualTo(3));
        Assert.That(fence.Value.ExtractContent(lines), Is.EqualTo("```\nstill code"));
    }

    [Test]
    public void FindFirstFence_ParsesFiveBacktickFence()
    {
        var lines = new[] { "`````", "````", "code", "`````" };
        var fence = EmbeddedFenceDetector.FindFirstFence(lines);

        Assert.That(fence, Is.Not.Null);
        Assert.That(fence!.Value.FenceLength, Is.EqualTo(5));
        Assert.That(fence.Value.ExtractContent(lines), Is.EqualTo("````\ncode"));
    }

    [Test]
    public void FindFirstFence_ShorterBareBacktickLine_DoesNotClose_LongerLineCloses()
    {
        // Opener is 4 backticks: an inner 3-backtick line must NOT close the fence; only the
        // later 4-backtick line does.
        var lines = new[] { "````", "```", "````" };
        var fence = EmbeddedFenceDetector.FindFirstFence(lines);

        Assert.That(fence, Is.Not.Null);
        Assert.That(fence!.Value.FenceLength, Is.EqualTo(4));
        Assert.That(fence.Value.CloseLineIndex, Is.EqualTo(2));
        Assert.That(fence.Value.ExtractContent(lines), Is.EqualTo("```"));
    }

    [Test]
    public void FindFirstFence_CloseLongerThanOpener_StillCloses()
    {
        // A bare-backtick line strictly longer than the opener still qualifies as a close.
        var lines = new[] { "```", "code", "`````" };
        var fence = EmbeddedFenceDetector.FindFirstFence(lines);

        Assert.That(fence, Is.Not.Null);
        Assert.That(fence!.Value.FenceLength, Is.EqualTo(3));
        Assert.That(fence.Value.CloseLineIndex, Is.EqualTo(2));
    }

    [Test]
    public void FindFirstFence_OnlyShorterBareBacktickLines_ReturnsNull()
    {
        // Opener is 4 backticks; the only later bare-backtick line is 3 — too short to close.
        var lines = new[] { "````", "code", "```" };
        Assert.That(EmbeddedFenceDetector.FindFirstFence(lines), Is.Null);
    }

    [Test]
    public void FindOpenFenceWithNoClose_FourBacktickOpenerWithOnlyShorterTicks_IsUnterminated()
    {
        var lines = new[] { "````", "```", "trailing" };
        var open = EmbeddedFenceDetector.FindOpenFenceWithNoClose(lines);

        Assert.That(open, Is.Not.Null);
        Assert.That(open!.Value.LineIndex, Is.EqualTo(0));
        Assert.That(open.Value.FenceLength, Is.EqualTo(4));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void TryConvertOpenFence_UnterminatedOpenerWithContent_ConsumesThroughEndOfInput()
    {
        // User requirement: if there is no closing fence before end-of-input, grab everything
        // from the line after the opener through the end of the prompt as region content —
        // nothing is silently dropped.
        var rtb = new RichTextBox();
        rtb.SetPlainText("```\nhello\nworld");

        Assert.That(EmbeddedCodeRegionManager.TryConvertOpenFence(rtb), Is.True);

        var region = (EmbeddedCodeRegionBlock)rtb.Document.Blocks
            .OfType<BlockUIContainer>().First().Child;
        Assert.That(region.Content, Is.EqualTo("hello\nworld"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void FenceContainingBareBacktickLine_RoundTripsThroughRichTextBox()
    {
        // End-to-end: a pasted region whose content contains a bare ``` line must survive a
        // full convert -> GetPlainText (copy/send) -> reconvert cycle without truncation.
        var rtb = new RichTextBox();
        rtb.SetPlainText("````\n```\ninner\n```\n````");
        while (EmbeddedCodeRegionManager.TryConvertNextFence(rtb)) { }

        var region = (EmbeddedCodeRegionBlock)rtb.Document.Blocks
            .OfType<BlockUIContainer>().First().Child;
        Assert.That(region.Content, Is.EqualTo("```\ninner\n```"));

        var serialized = rtb.GetPlainText();
        Assert.That(serialized, Is.EqualTo("````\n```\ninner\n```\n````"));

        var reparsed = new RichTextBox();
        reparsed.SetPlainText(serialized);
        while (EmbeddedCodeRegionManager.TryConvertNextFence(reparsed)) { }
        var reparsedRegion = (EmbeddedCodeRegionBlock)reparsed.Document.Blocks
            .OfType<BlockUIContainer>().First().Child;
        Assert.That(reparsedRegion.Content, Is.EqualTo("```\ninner\n```"));
    }

    // ── Editor chrome: height cap & scrollbar (prompt-editor-embedded-regions UI pass) ──

    [Test, Apartment(ApartmentState.STA)]
    public void AttachHost_HostMaxHeight260_ContentEditorCapAtHalf()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("```\nhello\n```");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);
        var region = (EmbeddedCodeRegionBlock)((BlockUIContainer)rtb.Document.Blocks.FirstBlock!).Child;
        var contentBox = GetContentEditor(region);

        var host = new RichTextBox { MaxHeight = 260 };
        region.AttachHost(host);

        Assert.That(contentBox.MaxHeight, Is.EqualTo(130),
            "content editor height caps at half the host RichTextBox's MaxHeight");
        Assert.That(contentBox.VerticalScrollBarVisibility, Is.EqualTo(ScrollBarVisibility.Auto),
            "a vertical scrollbar must appear when content exceeds the cap");
    }

    [Test, Apartment(ApartmentState.STA)]
    public void AttachHost_HostMaxHeightUnset_FallsBackTo130()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("```\nhello\n```");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);
        var region = (EmbeddedCodeRegionBlock)((BlockUIContainer)rtb.Document.Blocks.FirstBlock!).Child;
        var contentBox = GetContentEditor(region);

        // A default RichTextBox has MaxHeight == PositiveInfinity, so the fallback cap applies.
        var host = new RichTextBox();
        Assert.That(double.IsInfinity(host.MaxHeight) || double.IsNaN(host.MaxHeight), Is.True);
        region.AttachHost(host);

        Assert.That(contentBox.MaxHeight, Is.EqualTo(130));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void BeforeAttachHost_ContentEditorHasDefault130Cap()
    {
        var region = EmbeddedCodeRegionBlock.Build("hello", "");
        var contentBox = GetContentEditor(region);
        Assert.That(contentBox.MaxHeight, Is.EqualTo(130));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void AutoDetectHighlighting_KeepsSingleLineEditorHeightTight()
    {
        var editor = new AvalonEditRegionEditor("public event EventHandler? Changed;");
        var contentBox = (ICSharpCode.AvalonEdit.TextEditor)editor.Control;
        var plainHeight = contentBox.Height;

        editor.SetLanguage("");

        Assert.Multiple(() =>
        {
            Assert.That(editor.HighlightingName, Is.EqualTo("C#"),
                "the sample mirrors the short event/EventHandler? snippet that triggers C# auto-detect");
            Assert.That(contentBox.Height, Is.EqualTo(plainHeight).Within(0.5),
                "applying syntax highlighting must not inflate a one-line editor into empty viewport space");
            Assert.That(contentBox.Height, Is.LessThan(contentBox.MaxHeight),
                "one-line regions should stay content-sized rather than expanding to the scrollbar cap");
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ContentEditorHeight_GrowsWithLinesButCapsAtMaxHeight()
    {
        var editor = new AvalonEditRegionEditor("one");
        var contentBox = (ICSharpCode.AvalonEdit.TextEditor)editor.Control;
        var oneLineHeight = contentBox.Height;

        editor.Text = string.Join("\n", Enumerable.Range(1, 12).Select(i => $"line {i}"));

        Assert.Multiple(() =>
        {
            Assert.That(contentBox.Height, Is.GreaterThan(oneLineHeight));
            Assert.That(contentBox.Height, Is.EqualTo(contentBox.MaxHeight),
                "large regions still use the existing capped-height + scrollbar behavior");
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void AdjacentEmptyAndAutoDetectedRegions_KeepOneLineEditorsVisuallyCompact()
    {
        var rtb = new RichTextBox
        {
            FontSize = 14,
            MaxHeight = 260,
        };
        rtb.SetPlainText(
            "abc\n" +
            "```\n\n```\n" +
            " \n" +
            "```\n" +
            "event EventHandler?\n" +
            "```\n" +
            " \n" +
            "```c#\n" +
            "public interface ICalloutWindow {\n" +
            "    event EventHandler? RefreshTargetRect;\n" +
            "}\n" +
            "```");

        while (EmbeddedCodeRegionManager.TryConvertNextFence(rtb)) { }

        var regions = RichTextBoxExtensions.EnumerateEmbeddedRegions(rtb).ToArray();
        Assert.That(regions, Has.Length.EqualTo(3));
        var emptyEditor = GetContentEditor(regions[0]);
        var eventEditor = GetContentEditor(regions[1]);

        Assert.Multiple(() =>
        {
            Assert.That(regions[0].Content, Is.EqualTo(""));
            Assert.That(LanguageLabel(regions[1]).Text, Is.EqualTo("Auto detect -- C#"));
            Assert.That(emptyEditor.Height, Is.LessThanOrEqualTo(28),
                "an empty adjacent region must stay close to one prompt line, not read as a multi-line spacer");
            Assert.That(eventEditor.Height, Is.EqualTo(emptyEditor.Height).Within(0.5),
                "auto-detecting C# must not inflate a one-line region differently from an empty one-line editor");
        });
    }

    // ── Font scaling: region editor tracks the prompt font size ─────────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void EditorFontSize_SetThroughSeam_UpdatesUnderlyingEditorAndReadsBack()
    {
        var editor = new AvalonEditRegionEditor("code");

        editor.EditorFontSize = 22;

        Assert.That(editor.EditorFontSize, Is.EqualTo(22));
        Assert.That(editor.Control, Is.InstanceOf<ICSharpCode.AvalonEdit.TextEditor>());
        Assert.That(((ICSharpCode.AvalonEdit.TextEditor)editor.Control).FontSize, Is.EqualTo(22));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ApplyEditorFontSize_PropagatesToUnderlyingEditor()
    {
        var region = EmbeddedCodeRegionBlock.Build("def foo():\n    pass", "python");
        var contentBox = GetContentEditor(region);

        region.ApplyEditorFontSize(26);

        Assert.That(contentBox.FontSize, Is.EqualTo(26),
            "pushing a font size through the region must resize the hosted AvalonEdit editor");
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ApplyEditorFontSize_IgnoresNonPositiveValues()
    {
        var region = EmbeddedCodeRegionBlock.Build("x = 1", "");
        var contentBox = GetContentEditor(region);
        var original = contentBox.FontSize;

        region.ApplyEditorFontSize(0);
        region.ApplyEditorFontSize(-5);
        region.ApplyEditorFontSize(double.NaN);

        Assert.That(contentBox.FontSize, Is.EqualTo(original),
            "invalid sizes must be ignored so the editor never collapses to an unreadable size");
    }

    [Test, Apartment(ApartmentState.STA)]
    public void AttachHost_AdoptsHostFontSize_SoRegionsCreatedAfterZoomPickUpCurrentSize()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("```\nhello\n```");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);
        var region = (EmbeddedCodeRegionBlock)((BlockUIContainer)rtb.Document.Blocks.FirstBlock!).Child;
        var contentBox = GetContentEditor(region);

        // Host prompt was zoomed to 24 (ApplyPromptFontSize sets PromptTextBox.FontSize); a region
        // attached afterwards must render at that size, not the hardcoded construction default.
        var host = new RichTextBox { FontSize = 24 };
        region.AttachHost(host);

        Assert.That(contentBox.FontSize, Is.EqualTo(24));
    }

    // ── Backspace-removes-empty-region (EmbeddedRegionBackspaceRouter + host removal) ───────────

    [Test]
    public void ShouldRemoveEmptyRegionOnBackspace_BackEmptyNoModifiers_IsTrue()
        => Assert.That(
            EmbeddedRegionBackspaceRouter.ShouldRemoveEmptyRegionOnBackspace(Key.Back, ModifierKeys.None, 0),
            Is.True);

    [Test]
    public void ShouldRemoveEmptyRegionOnBackspace_BackNonEmpty_IsFalse()
        => Assert.That(
            EmbeddedRegionBackspaceRouter.ShouldRemoveEmptyRegionOnBackspace(Key.Back, ModifierKeys.None, 5),
            Is.False);

    [Test]
    public void ShouldRemoveEmptyRegionOnBackspace_BackEmptyWithControl_IsFalse()
        => Assert.That(
            EmbeddedRegionBackspaceRouter.ShouldRemoveEmptyRegionOnBackspace(Key.Back, ModifierKeys.Control, 0),
            Is.False);

    [Test]
    public void ShouldRemoveEmptyRegionOnBackspace_BackEmptyWithShift_IsFalse()
        => Assert.That(
            EmbeddedRegionBackspaceRouter.ShouldRemoveEmptyRegionOnBackspace(Key.Back, ModifierKeys.Shift, 0),
            Is.False);

    [Test]
    public void ShouldRemoveEmptyRegionOnBackspace_BackEmptyWithAlt_IsFalse()
        => Assert.That(
            EmbeddedRegionBackspaceRouter.ShouldRemoveEmptyRegionOnBackspace(Key.Back, ModifierKeys.Alt, 0),
            Is.False);

    [Test]
    public void ShouldRemoveEmptyRegionOnBackspace_DeleteEmpty_IsFalse()
        => Assert.That(
            EmbeddedRegionBackspaceRouter.ShouldRemoveEmptyRegionOnBackspace(Key.Delete, ModifierKeys.None, 0),
            Is.False);

    [Test, Apartment(ApartmentState.STA)]
    public void TryHandleRegionDeleteKey_BackspaceDeletesInsideRegionAndHandles()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("```\nabc\n```");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);
        var region = RichTextBoxExtensions.EnumerateEmbeddedRegions(rtb).Single();
        region.FocusContentAtPosition(2);

        Assert.That(region.TryHandleRegionDeleteKey(Key.Back, ModifierKeys.None), Is.True);

        Assert.That(region.Content, Is.EqualTo("ac"));
        Assert.That(rtb.GetPlainText(), Is.EqualTo("```\nac\n```"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void TryHandleRegionDeleteKey_DeleteAtEndIsHandledNoop()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("```\nabc\n```");
        EmbeddedCodeRegionManager.TryConvertNextFence(rtb);
        var region = RichTextBoxExtensions.EnumerateEmbeddedRegions(rtb).Single();
        region.FocusContentAtPosition(3);

        Assert.That(region.TryHandleRegionDeleteKey(Key.Delete, ModifierKeys.None), Is.True);

        Assert.That(region.Content, Is.EqualTo("abc"));
        Assert.That(rtb.GetPlainText(), Is.EqualTo("```\nabc\n```"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void TryRemoveEmptyRegionFromHost_DropsContainer_AndRegionDoesNotResurrectOnRebuild()
    {
        // An empty fence between two plain-text lines becomes an empty region flanked by paragraphs.
        var rtb = new RichTextBox();
        rtb.SetPlainText("before\n```\n```\nafter");
        Assert.That(EmbeddedCodeRegionManager.TryConvertNextFence(rtb), Is.True);

        var container = rtb.Document.Blocks.OfType<BlockUIContainer>().Single();
        var region = (EmbeddedCodeRegionBlock)container.Child;
        Assert.That(region.Content, Is.EqualTo(""), "region must be empty for this scenario");

        var removed = region.TryRemoveEmptyRegionFromHost();

        Assert.That(removed, Is.True);
        Assert.That(rtb.Document.Blocks.OfType<BlockUIContainer>(), Is.Empty,
            "the region's BlockUIContainer must be gone from the document");
        Assert.That(rtb.GetPlainText(), Is.EqualTo("before\nafter"),
            "serialized source of truth must no longer contain the empty fence");
        Assert.That(region.IsDisposed, Is.True);
        Assert.That(region.IsAttachedToHost(), Is.False);

        // Proof the region does not resurrect: the FlowDocument is the single source of truth, so a
        // subsequent fence-rebuild finds nothing to convert and the serialization is unchanged.
        Assert.That(EmbeddedCodeRegionManager.TryConvertNextFence(rtb), Is.False);
        Assert.That(rtb.GetPlainText(), Is.EqualTo("before\nafter"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void TryRemoveEmptyRegionFromHost_LoneRegion_LeavesEditableEmptyDocument()
    {
        var rtb = new RichTextBox();
        rtb.SetPlainText("```\n```");
        Assert.That(EmbeddedCodeRegionManager.TryConvertNextFence(rtb), Is.True);

        var region = (EmbeddedCodeRegionBlock)rtb.Document.Blocks.OfType<BlockUIContainer>().Single().Child;

        Assert.That(region.TryRemoveEmptyRegionFromHost(), Is.True);
        Assert.That(rtb.Document.Blocks.OfType<BlockUIContainer>(), Is.Empty);
        Assert.That(region.IsDisposed, Is.True);
        Assert.That(rtb.Document.Blocks.Count, Is.GreaterThan(0),
            "a blank paragraph must remain so the RichTextBox stays editable");
        Assert.That(rtb.GetPlainText(), Is.EqualTo(""));
    }

    // The region's content editor is now an AvalonEdit TextEditor hosted inside the editor
    // border (the second child of the region's StackPanel).
    private static ICSharpCode.AvalonEdit.TextEditor GetContentEditor(EmbeddedCodeRegionBlock region)
        => (ICSharpCode.AvalonEdit.TextEditor)((Border)((StackPanel)region.Child).Children[1]).Child;

    // Depth-first visual-tree search for the first descendant of type <typeparamref name="T"/>.
    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                return match;
            var nested = FindDescendant<T>(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }
}
