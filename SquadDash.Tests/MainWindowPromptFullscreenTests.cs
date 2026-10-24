using System.IO;
using System.Xml.Linq;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class MainWindowPromptFullscreenTests
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Test]
    public void ViewMenu_PromptFullscreenImmediatelyFollowsTranscript()
    {
        var xaml = XDocument.Parse(ReadSource("MainWindow.xaml"));
        var transcript = NamedElement(xaml, "FullScreenTranscriptMenuItem");
        var prompt = transcript.ElementsAfterSelf().First();
        var reveal = xaml.Descendants().Single(e => (string?)e.Attribute("Header") == "_UI Reveal");

        Assert.Multiple(() =>
        {
            Assert.That((string?)prompt.Attribute(XamlNamespace + "Name"), Is.EqualTo("FullScreenPromptMenuItem"));
            Assert.That(((string?)prompt.Attribute("Header"))?.Replace("_", ""), Is.EqualTo("Full Screen Prompt"));
            Assert.That((string?)prompt.Attribute("IsCheckable"), Is.EqualTo("True"));
            Assert.That((string?)prompt.Attribute("InputGestureText"), Is.EqualTo("F12"));
            Assert.That((string?)prompt.Attribute("Click"), Is.EqualTo("FullScreenPromptMenuItem_Click"));
            Assert.That((string?)reveal.Attribute("InputGestureText"), Is.EqualTo("Shift+F12"));
        });
    }

    [Test]
    public void GlobalShortcut_BareF12TogglesPromptAndReturnsBeforeReveal()
    {
        var handler = Method("private void OnGlobalPreProcessInput(");
        var start = handler.IndexOf("if (key == Key.F12 && noMods)", StringComparison.Ordinal);
        var end = handler.IndexOf("return;", start, StringComparison.Ordinal);
        var branch = handler[start..end];

        Assert.Multiple(() =>
        {
            Assert.That(branch, Does.Contain("if (!keyArgs.IsRepeat)"));
            Assert.That(branch, Does.Contain("SetPromptFullScreen(!_promptFullScreenEnabled)"));
            Assert.That(branch, Does.Contain("keyArgs.Handled = true;"));
            Assert.That(branch, Does.Not.Contain("ToggleUiReveal"));
            Assert.That(end, Is.LessThan(handler.IndexOf("ToggleUiReveal(target)", StringComparison.Ordinal)));
        });
    }

    [Test]
    public void GlobalShortcut_OnlyShiftF12ReachesRevealWithoutTogglingPrompt()
    {
        var handler = Method("private void OnGlobalPreProcessInput(");
        var start = handler.IndexOf("else if (key == Key.F12 && Keyboard.Modifiers == ModifierKeys.Shift)", StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0));
        var revealBranch = handler[start..];

        Assert.Multiple(() =>
        {
            Assert.That(handler, Does.Contain("&& !(key == Key.F12 && Keyboard.Modifiers == ModifierKeys.Shift)) return;"));
            Assert.That(revealBranch, Does.Contain("if (!keyArgs.IsRepeat)"));
            Assert.That(revealBranch, Does.Contain("ToggleUiReveal(target);"));
            Assert.That(revealBranch, Does.Contain("keyArgs.Handled = true;"));
            Assert.That(revealBranch, Does.Not.Contain("SetPromptFullScreen("));
        });
    }

    [Test]
    public void SendAndEnter_ExitThroughSameHandlerBeforeAnyDispatchOrQueueBranch()
    {
        var send = Method("private async void RunButton_Click(");
        var keyHandler = Method("private void PromptTextBox_KeyDown(");
        var start = keyHandler.IndexOf("case PromptInputAction.SubmitPrompt:", StringComparison.Ordinal);
        var end = keyHandler.IndexOf("break;", start, StringComparison.Ordinal);
        var submit = keyHandler[start..end];

        Assert.Multiple(() =>
        {
            Assert.That(send, Does.Contain("SetPromptFullScreen(false);"));
            Assert.That(send.IndexOf("SetPromptFullScreen(false);", StringComparison.Ordinal),
                Is.LessThan(send.IndexOf("if (_steeringPromptInProgress", StringComparison.Ordinal)));
            Assert.That(submit, Does.Contain("RunButton_Click(sender, new RoutedEventArgs());"));
            Assert.That(submit, Does.Not.Contain("SetPromptFullScreen"));
            Assert.That((string?)NamedElement(XDocument.Parse(ReadSource("MainWindow.xaml")), "RunButton").Attribute("Click"),
                Is.EqualTo("RunButton_Click"));
        });
    }

    [Test]
    public void QueueEditingAndTextChanges_DoNotExitPromptFullscreen()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Method("private void OnQueueTabClicked("), Does.Not.Contain("SetPromptFullScreen("));
            Assert.That(Method("private void OnQueueTabClickedCore("), Does.Not.Contain("SetPromptFullScreen("));
            Assert.That(Method("private void PromptTextBox_TextChanged("), Does.Not.Contain("SetPromptFullScreen("));
        });
    }

    [Test]
    public void FullscreenTransitions_ReuseWindowLayoutAndExcludeOtherModes()
    {
        var prompt = Method("private void SetPromptFullScreen(");
        var transcript = Method("private void SetTranscriptFullScreen(");
        var focus = Method("private void SetAgentsPanelFocusMode(");

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("SetTranscriptFullScreen(false);"));
            Assert.That(prompt, Does.Contain("SetAgentsPanelFocusMode(false);"));
            Assert.That(prompt, Does.Contain("_fullScreenPromptVisible = false;"));
            Assert.That(prompt, Does.Contain("ApplyFullScreenWindowLayout(enabled);"));
            Assert.That(transcript, Does.Contain("ApplyFullScreenWindowLayout(enabled);"));
            Assert.That(transcript, Does.Contain("if (enabled)\n            SetPromptFullScreen(false);"));
            Assert.That(focus, Does.Contain("if (enabled)\n            SetPromptFullScreen(false);"));
            Assert.That(prompt, Does.Not.Contain("ClearPromptTextBox"));
            Assert.That(prompt, Does.Not.Contain("HideFullScreenPrompt("));
            Assert.That(prompt, Does.Not.Contain("SaveDocsPanelState"));
        });
    }

    [Test]
    public void PromptFullscreen_ExpandsEditorAndRestoresConstraintsWithoutClearingDraft()
    {
        var setter = Method("private void SetPromptFullScreen(");
        var view = Method("private void ApplyViewMode(");

        Assert.Multiple(() =>
        {
            Assert.That(setter, Does.Contain("_prePromptFullScreenMaxHeight = PromptTextBox.MaxHeight;"));
            Assert.That(setter, Does.Contain("_prePromptFullScreenMargin = PromptBorder.Margin;"));
            Assert.That(setter, Does.Contain("PromptTextBox.MaxHeight = _prePromptFullScreenMaxHeight;"));
            Assert.That(setter, Does.Contain("PromptBorder.Margin = _prePromptFullScreenMargin;"));
            Assert.That(view, Does.Contain("PromptTextBox.MaxHeight = double.PositiveInfinity;"));
            Assert.That(view, Does.Contain("PromptEditorRow.Height = _promptFullScreenEnabled ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;"));
            Assert.That(view, Does.Contain("MainGrid.RowDefinitions[Grid.GetRow(PromptBorder)].Height = _promptFullScreenEnabled\n                ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;"));
            Assert.That(view, Does.Contain("TranscriptPanelsGrid.Visibility = _promptFullScreenEnabled ? Visibility.Collapsed : Visibility.Visible;"));
            Assert.That(view, Does.Contain("(_transcriptFullScreenEnabled || _promptFullScreenEnabled || _agentsPanelFocusModeEnabled)"));
            Assert.That(view, Does.Contain("var docsVisible = _documentationModeEnabled && !_promptFullScreenEnabled;"));
        });
    }

    [Test]
    public void PromptPanel_ContainsQueueAndSubmissionControlsInExpandableRow()
    {
        var xaml = XDocument.Parse(ReadSource("MainWindow.xaml"));
        var prompt = NamedElement(xaml, "PromptBorder");
        var names = prompt.Descendants().Select(e => (string?)e.Attribute(XamlNamespace + "Name")).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(names, Does.Contain("QueueTabStrip"));
            Assert.That(names, Does.Contain("QueuePlayPauseButton"));
            Assert.That(names, Does.Contain("RunButton"));
            Assert.That(names, Does.Contain("AbortButton"));
            Assert.That(names, Does.Contain("PromptTextBox"));
            Assert.That(names, Does.Contain("PromptEditorRow"));
            Assert.That((string?)NamedElement(xaml, "PromptTextBox").Attribute("MaxHeight"), Is.EqualTo("260"));
            Assert.That((string?)NamedElement(xaml, "PromptEditorRow").Attribute("Height"), Is.EqualTo("Auto"));
        });
    }

    [Test]
    public void PromptOnTop_UsesActualMainGridRowsAndReappliesFullscreenLayout()
    {
        var xaml = XDocument.Parse(ReadSource("MainWindow.xaml"));
        var onTop = Method("private void SetPromptPanelOnTop(");

        Assert.Multiple(() =>
        {
            Assert.That((string?)NamedElement(xaml, "TranscriptPanelsGrid").Attribute("Grid.Row"), Is.EqualTo("4"));
            Assert.That((string?)NamedElement(xaml, "PromptBorder").Attribute("Grid.Row"), Is.EqualTo("5"));
            Assert.That(onTop, Does.Contain("MainGrid.RowDefinitions[4]"));
            Assert.That(onTop, Does.Contain("MainGrid.RowDefinitions[5]"));
            Assert.That(onTop, Does.Not.Contain("MainGrid.RowDefinitions[3]"));
            Assert.That(onTop, Does.Contain("if (_promptFullScreenEnabled)"));
            Assert.That(onTop, Does.Contain("ApplyViewMode();"));
        });
    }

    [Test]
    public void MenuChecks_UseSharedViewStateForMenuOpenAndLayout()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Method("private void ViewMenuItem_SubmenuOpened("), Does.Contain("UpdateViewModeMenuChecks();"));
            Assert.That(Method("private void ApplyViewMode("), Does.Contain("UpdateViewModeMenuChecks();"));
            Assert.That(Method("private void UpdateViewModeMenuChecks("),
                Does.Contain("FullScreenPromptMenuItem.IsChecked = _promptFullScreenEnabled;"));
            Assert.That(Method("private void FullScreenPromptMenuItem_Click("),
                Does.Contain("SetPromptFullScreen(!_promptFullScreenEnabled);"));
        });
    }

    private static XElement NamedElement(XDocument xaml, string name) =>
        xaml.Descendants().Single(e => (string?)e.Attribute(XamlNamespace + "Name") == name);

    private static string Method(string marker)
    {
        var source = ReadSource("MainWindow.xaml.cs");
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"Missing method: {marker}");
        var end = source.IndexOf("\n    private ", start + marker.Length, StringComparison.Ordinal);
        Assert.That(end, Is.GreaterThan(start), $"Missing next member after: {marker}");
        return source[start..end];
    }

    private static string ReadSource(string name)
    {
        var root = TestContext.CurrentContext.TestDirectory;
        while (!string.IsNullOrEmpty(root))
        {
            var path = Path.Combine(root, "SquadDash", name);
            if (File.Exists(path))
                return File.ReadAllText(path).Replace("\r\n", "\n");
            root = Path.GetDirectoryName(root);
        }
        throw new FileNotFoundException($"Could not locate SquadDash source file {name}.");
    }
}
