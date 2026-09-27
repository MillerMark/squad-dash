using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace SquadDash.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class CommitViewerWindowTests
{
    [TestCase(null)]
    [TestCase("null")]
    [TestCase(ToolTranscriptFormatterTests.AbsolutePlanPath)]
    public void TranscriptFileLink_AbsolutePlanAdditionOpensNamedPatchViewer(string? displayText) =>
        WpfTestContext.Run(() =>
        {
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var fonts = new ResourceDictionary
            {
                ["FontSizeBody"] = 13.0, ["FontSizeNormal"] = 13.0,
                ["FontSizeSmall"] = 12.0, ["FontSizeSubtitle"] = 18.0
            };
            app.Resources.MergedDictionaries.Add(fonts);
            CommitViewerWindow? viewer = null;
            try
            {
                var message = new TextBlock();
                Assert.That(ToolEditLinkPresenter.TryRender(message,
                    new ToolTranscriptDescriptor("apply_patch", DisplayText: displayText),
                    ToolTranscriptFormatterTests.AbsolutePlanOutput,
                    JsonSerializer.Serialize(ToolTranscriptFormatterTests.AbsolutePlanInput),
                    null,
                    async edit => viewer = await CommitViewerWindow.CreateFromPatchAsync(
                        edit.DisplayName, edit.UnifiedDiff)), Is.True);
                var link = message.Inlines.OfType<Hyperlink>().Single();
                Assert.That(link.Inlines.OfType<Run>().Single().Text, Is.EqualTo("plan.md"));
                link.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
                Assert.That(viewer, Is.Not.Null);
                Assert.Multiple(() =>
                {
                    Assert.That(viewer!.Title, Is.EqualTo("Changes - plan.md"));
                    Assert.That(ViewerField<string>(viewer, "_suppliedPatch"),
                        Is.EqualTo(ToolTranscriptFormatterTests.AbsolutePlanOutput));
                    Assert.That(ViewerField<StackPanel>(viewer, "_diffPanel").Children.OfType<TextBlock>()
                        .Count(line => Equals(line.Tag, DiffLineKind.Added)), Is.EqualTo(17));
                    Assert.That(viewer.Width, Is.EqualTo(1680));
                    Assert.That(viewer.Height, Is.EqualTo(1140));
                });
            }
            finally
            {
                viewer?.Close();
                app.Resources.MergedDictionaries.Remove(fonts);
            }
        });

    [TestCaseSource(typeof(ToolTranscriptFormatterTests), nameof(ToolTranscriptFormatterTests.FileEditCaptures))]
    public void TranscriptFileLink_ClickOpensCapturedPatchViewer(
        string tool, string output, string? args, int added, int removed, bool isNew) =>
        WpfTestContext.Run(() =>
        {
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var resources = new ResourceDictionary
            {
                ["FontSizeBody"] = 13.0, ["FontSizeNormal"] = 13.0,
                ["FontSizeSmall"] = 12.0, ["FontSizeSubtitle"] = 18.0
            };
            app.Resources.MergedDictionaries.Add(resources);
            CommitViewerWindow? viewer = null;
            try
            {
                var message = new TextBlock();
                message.Resources["TaskLinkText"] = Brushes.CornflowerBlue;
                var rendered = ToolEditLinkPresenter.TryRender(message, new ToolTranscriptDescriptor(tool),
                    output, args, null, async edit => viewer = await CommitViewerWindow.CreateFromPatchAsync(
                        edit.DisplayName, edit.UnifiedDiff));
                Assert.That(rendered, Is.True);
                message.Measure(new Size(800, 100));
                message.Arrange(new Rect(0, 0, 800, 100));
                var link = message.Inlines.OfType<Hyperlink>().Single();
                Assert.Multiple(() =>
                {
                    Assert.That(link.Inlines.OfType<Run>().Single().Text, Is.EqualTo("file.cs"));
                    Assert.That(link.Cursor, Is.SameAs(Cursors.Hand));
                    Assert.That(link.Foreground, Is.SameAs(message.Resources["TaskLinkText"]));
                    Assert.That(link.ToolTip, Is.TypeOf<ToolTip>());
                    // TextRange omits nested Hyperlink text in TextBlock; inspect its runs separately.
                    Assert.That(string.Concat(message.Inlines.OfType<Run>().Select(run => run.Text)),
                        Is.EqualTo((isNew ? " ➕" : "") + $" +{added} -{removed}" +
                            (tool is "write" or "write_file" ? " (captured content)" : "")));
                });
                message.Resources["TaskLinkText"] = Brushes.DarkBlue;
                Assert.That(link.Foreground, Is.SameAs(message.Resources["TaskLinkText"]), "Link follows theme changes.");
                var click = new RoutedEventArgs(Hyperlink.ClickEvent);
                link.RaiseEvent(click);
                Assert.That(click.Handled, Is.True, "The click must not toggle the surrounding expander.");
                Assert.That(viewer, Is.Not.Null);
                Assert.That(viewer!.Title, Is.EqualTo("Changes - file.cs"));
                if (tool is "write" or "write_file")
                {
                    Assert.That(ViewerField<TextBlock>(viewer, "_status").Text,
                        Is.EqualTo("Captured file content (previous content unavailable)"));
                    var file = (ListBoxItem)ViewerField<ListBox>(viewer, "_fileList").Items[0];
                    Assert.That(((TextBlock)((Grid)file.Content).Children[1]).Text, Is.EqualTo("Content"));
                }
                var diff = ViewerField<StackPanel>(viewer, "_diffPanel");
                Assert.That(diff.Children.OfType<TextBlock>().Count(line => Equals(line.Tag, DiffLineKind.Added)),
                    Is.EqualTo(added));
                Assert.That(diff.Children.OfType<TextBlock>().Count(line => Equals(line.Tag, DiffLineKind.Removed)),
                    Is.EqualTo(removed));
            }
            finally
            {
                viewer?.Close();
                app.Resources.MergedDictionaries.Remove(resources);
            }
        });

    [Test]
    public void TranscriptFileLink_DeletedFileRetainsStrikethroughAndFallbackHasNoLink() =>
        WpfTestContext.Run(() =>
        {
            var descriptor = new ToolTranscriptDescriptor("edit", Path: "file.cs");
            var message = new TextBlock();
            ToolEditLinkPresenter.TryRender(message, descriptor,
                "--- a/file.cs\n+++ /dev/null\n@@ -1 +0,0 @@\n-old", null, null, _ => Task.CompletedTask);
            Assert.That(message.Inlines.OfType<Hyperlink>().Single().Inlines.OfType<Run>().Single().TextDecorations,
                Is.EqualTo(TextDecorations.Strikethrough));
            message.Inlines.Clear();
            ToolEditLinkPresenter.TryRender(message, descriptor, "File updated.", null, null, _ => Task.CompletedTask);
            Assert.That(message.Inlines.OfType<Hyperlink>(), Is.Empty);
        });

    [Test]
    public void TranscriptFileLink_MultiFileEditRendersEveryFileAndPerFileNewMarkers() =>
        WpfTestContext.Run(() =>
        {
            var output = """
                diff --git a/src/old.cs b/src/old.cs
                --- a/src/old.cs
                +++ b/src/old.cs
                @@ -1 +1 @@
                -old
                +new
                diff --git a/src/new-one.cs b/src/new-one.cs
                new file mode 100644
                --- /dev/null
                +++ b/src/new-one.cs
                @@ -0,0 +1 @@
                +one
                diff --git a/src/new-two.cs b/src/new-two.cs
                new file mode 100644
                --- /dev/null
                +++ b/src/new-two.cs
                @@ -0,0 +1 @@
                +two
                """;
            var descriptor = new ToolTranscriptDescriptor("apply_patch", DisplayText: "src/old.cs");
            var edits = ToolTranscriptFormatter.TryExtractCapturedEdits(descriptor, output);
            var message = new TextBlock();
            CapturedFileEdit? clicked = null;

            Assert.That(ToolEditLinkPresenter.TryRender(
                message, descriptor, output, null, edits, edit => {
                    clicked = edit;
                    return Task.CompletedTask;
                }), Is.True);

            var links = message.Inlines.OfType<Hyperlink>().ToArray();
            Assert.Multiple(() => {
                Assert.That(links.Select(link => link.Inlines.OfType<Run>().Single().Text),
                    Is.EqualTo(new[] { "old.cs", "new-one.cs", "new-two.cs" }));
                Assert.That(string.Concat(message.Inlines.OfType<Run>().Select(run => run.Text)),
                    Does.Contain("+1 -1").And.Contain("➕ +1 -0"));
            });
            links[2].RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
            Assert.That(clicked?.DisplayName, Is.EqualTo("new-two.cs"));
        });

    [Test]
    public void CapturedEditViewer_EntireTurnShowsEveryFileAndKeepsClickedFileSelected() =>
        WpfTestContext.Run(() =>
        {
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var resources = new ResourceDictionary {
                ["FontSizeBody"] = 13.0, ["FontSizeNormal"] = 13.0,
                ["FontSizeSmall"] = 12.0, ["FontSizeSubtitle"] = 18.0
            };
            app.Resources.MergedDictionaries.Add(resources);
            CommitViewerWindow? viewer = null;
            try {
                static CapturedFileEdit Edit(string path, string before, string after) => new(
                    path,
                    $"--- a/{path}\n+++ b/{path}\n@@ -1 +1 @@\n-{before}\n+{after}",
                    1, 1, false, false);
                var origin = new CapturedEditCall("call-1", DateTimeOffset.UtcNow.AddMinutes(-2), [
                    Edit("src/first.cs", "old", "one"),
                    Edit("src/second.cs", "old", "two")
                ]);
                var later = new CapturedEditCall("call-2", DateTimeOffset.UtcNow.AddMinutes(-1), [
                    Edit("C:/repo/src/first.cs", "one", "three"),
                    Edit("src/third.cs", "old", "four")
                ]);
                viewer = CommitViewerWindow.CreateFromCapturedEditsAsync(
                    origin, "src/second.cs", () => [origin, later]).Result;

                var fileList = ViewerField<ListBox>(viewer, "_fileList");
                Assert.That(fileList.Items, Has.Count.EqualTo(2));
                ViewerField<Button>(viewer, "_entireTurnButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                Assert.Multiple(() => {
                    Assert.That(fileList.Items, Has.Count.EqualTo(3));
                    Assert.That(((ListBoxItem)fileList.SelectedItem).ToolTip,
                        Is.EqualTo("src/second.cs"));
                    Assert.That(ViewerField<TextBlock>(viewer, "_status").Text,
                        Does.StartWith("Entire turn · 2 edit calls · 3 changed files"));
                });
            }
            finally {
                viewer?.Close();
                app.Resources.MergedDictionaries.Remove(resources);
            }
        });

    [Test]
    public void TranscriptRendering_AllThreadsUseCapturedArgumentsAndNormalTimingTooltip()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "SquadDash", "MainWindow.xaml.cs")))
            directory = directory.Parent;
        Assert.That(directory, Is.Not.Null);
        var source = File.ReadAllText(Path.Combine(directory!.FullName, "SquadDash", "MainWindow.xaml.cs"));
        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("expander.Content = detailPanel;\n\n        AttachToolTimingTooltip(headerPanel, entry);")
                .Or.Contain("expander.Content = detailPanel;\r\n\r\n        AttachToolTimingTooltip(headerPanel, entry);"));
            Assert.That(source, Does.Not.Contain("DiffHoverPopup"));
            Assert.That(source, Does.Contain("ToolEditLinkPresenter.TryRender("));
            Assert.That(source, Does.Contain("_agentThreadRegistry.SetToolEntry(toolCallId, entry);"));
            Assert.That(source, Does.Contain("entry.IsCompleted &&\n            entry.Success &&")
                .Or.Contain("entry.IsCompleted &&\r\n            entry.Success &&"));
        });
    }

    [Test]
    public void EveryLaunch_ConnectsNavigationToTheExistingEnvironmentZoomHandler()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "SquadDash", "MainWindow.xaml.cs")))
            directory = directory.Parent;

        Assert.That(directory, Is.Not.Null);
        var source = File.ReadAllText(Path.Combine(directory!.FullName, "SquadDash", "MainWindow.xaml.cs"));
        var launches = Regex.Matches(source,
            @"var viewer = await CommitViewerWindow\.Create(?:Range|FromPatch)?Async\([\s\S]*?\);(?<wiring>[\s\S]*?)viewer\.Show\(\);");

        Assert.That(launches, Is.Not.Empty);
        foreach (Match launch in launches)
            Assert.That(launch.Groups["wiring"].Value,
                Does.Contain("viewer.ConfigureEnvironmentFontZoom(Window_PreviewMouseWheel);"));
    }

    [TestCase("--- a/file.cs\n+++ b/file.cs\n@@ -1 +1 @@\n---old\n+++new", 1, 1)]
    [TestCase("--- /dev/null\n+++ b/file.cs\n@@ -0,0 +1,2 @@\n+first\n+second", 2, 0)]
    [TestCase("--- a/file.cs\n+++ /dev/null\n@@ -1,2 +0,0 @@\n-first\n-second", 0, 2)]
    [TestCase("", 0, 0)]
    [TestCase(" \r\n ", 0, 0)]
    public void CreateFromPatchAsync_RendersSuppliedContentWithoutRepositoryOrCommits(
        string patch, int added, int removed) =>
        WpfTestContext.Run(() =>
        {
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var fonts = new ResourceDictionary
            {
                ["FontSizeBody"] = 13.0, ["FontSizeNormal"] = 13.0,
                ["FontSizeSmall"] = 12.0, ["FontSizeSubtitle"] = 18.0
            };
            app.Resources.MergedDictionaries.Add(fonts);
            CommitViewerWindow? window = null;
            try
            {
                var creation = CommitViewerWindow.CreateFromPatchAsync("file.cs", patch);
                Assert.That(creation.IsCompletedSuccessfully, Is.True, "No asynchronous git loading is needed.");
                window = creation.GetAwaiter().GetResult();
                var files = ViewerField<ListBox>(window, "_fileList");
                var commits = ViewerField<ListBox>(window, "_commitList");
                var diff = ViewerField<StackPanel>(window, "_diffPanel");
                Assert.Multiple(() =>
                {
                    Assert.That(ViewerField<string?>(window, "_repositoryPath"), Is.Null);
                    Assert.That(ViewerField<string>(window, "_commitSha"), Is.Empty);
                    Assert.That(ViewerField<IReadOnlyList<CommitViewerEntry>>(window, "_commits"), Is.Empty);
                    Assert.That(commits.Items.Count, Is.Zero);
                    Assert.That(commits.Parent, Is.Null);
                    Assert.That(files.Items.Count, Is.EqualTo(1));
                    Assert.That(files.SelectedIndex, Is.Zero);
                    Assert.That(ViewerField<TextBlock>(window, "_heading").Text, Is.EqualTo("file.cs"));
                    Assert.That(window.Title, Is.EqualTo("Changes - file.cs"));
                    var row = (Grid)((ListBoxItem)files.Items[0]).Content;
                    Assert.That(((TextBlock)row.Children[0]).Text, Is.EqualTo("file.cs"));
                    var summary = (TextBlock)row.Children[1];
                    Assert.That(new TextRange(summary.ContentStart, summary.ContentEnd).Text,
                        Is.EqualTo($"+{added}  -{removed}"));
                });

                void AssertDiff()
                {
                    var lines = diff.Children.OfType<TextBlock>().ToArray();
                    if (string.IsNullOrWhiteSpace(patch))
                    {
                        Assert.That(lines.Single().Text, Is.EqualTo("No text diff is available for this file."));
                        return;
                    }
                    Assert.That(lines.Select(line => new TextRange(line.ContentStart, line.ContentEnd).Text),
                        Is.EqualTo(patch.Split('\n')));
                    Assert.That(lines.Count(line => Equals(line.Tag, DiffLineKind.Added)), Is.EqualTo(added));
                    Assert.That(lines.Count(line => Equals(line.Tag, DiffLineKind.Removed)), Is.EqualTo(removed));
                    foreach (var line in lines.Where(line => Equals(line.Tag, DiffLineKind.Added) ||
                                                            Equals(line.Tag, DiffLineKind.Removed)))
                        Assert.That(line.FontSize, Is.EqualTo(ViewerField<double>(window, "_codeFontSize")));
                }

                AssertDiff();
                files.SelectedIndex = -1;
                files.SelectedIndex = 0;
                AssertDiff();
                var ex = Assert.Throws<TargetInvocationException>(() =>
                    InvokeViewer(window, "RunGitAsync", (object)new[] { "--version" }));
                Assert.That(ex!.InnerException, Is.TypeOf<InvalidOperationException>(),
                    "Patch mode must reject git before starting any process.");
            }
            finally
            {
                window?.Close();
                app.Resources.MergedDictionaries.Remove(fonts);
            }
        });

    [Test]
    public void Constructors_UseLargerDefaultWindowSizesAcrossViewerModes() =>
        WpfTestContext.Run(() =>
        {
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var fonts = new ResourceDictionary
            {
                ["FontSizeBody"] = 13.0, ["FontSizeNormal"] = 13.0,
                ["FontSizeSmall"] = 12.0, ["FontSizeSubtitle"] = 18.0
            };
            app.Resources.MergedDictionaries.Add(fonts);
            CommitViewerWindow? patch = null;
            CommitViewerWindow? single = null;
            CommitViewerWindow? range = null;
            CommitViewerWindow? multi = null;
            try
            {
                patch = CommitViewerWindow.CreateFromPatchAsync("file.cs",
                    "--- a/file.cs\n+++ b/file.cs\n@@ -1 +1 @@\n-old\n+new").GetAwaiter().GetResult();
                single = (CommitViewerWindow)Activator.CreateInstance(typeof(CommitViewerWindow),
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    binder: null,
                    args: [".", new CommitViewerEntry[] { new("abcdef12") }, null, null],
                    culture: null)!;
                range = (CommitViewerWindow)Activator.CreateInstance(typeof(CommitViewerWindow),
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    binder: null,
                    args: [".", new CommitViewerEntry[] { new("abcdef12") }, "12345678", "file.cs"],
                    culture: null)!;
                multi = (CommitViewerWindow)Activator.CreateInstance(typeof(CommitViewerWindow),
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    binder: null,
                    args: [".", new CommitViewerEntry[] { new("abcdef12"), new("fedcba98") }, null, null],
                    culture: null)!;

                Assert.Multiple(() =>
                {
                    Assert.That(CommitViewerLayout.DefaultSingleCommitWidth, Is.EqualTo(1120 * 1.5));
                    Assert.That(CommitViewerLayout.DefaultMultiCommitWidth, Is.EqualTo(1400 * 1.5));
                    Assert.That(CommitViewerLayout.DefaultWindowHeight, Is.EqualTo(760 * 1.5));
                    Assert.That(patch.Width, Is.EqualTo(CommitViewerLayout.DefaultSingleCommitWidth));
                    Assert.That(single.Width, Is.EqualTo(CommitViewerLayout.DefaultSingleCommitWidth));
                    Assert.That(range.Width, Is.EqualTo(CommitViewerLayout.DefaultSingleCommitWidth));
                    Assert.That(multi.Width, Is.EqualTo(CommitViewerLayout.DefaultMultiCommitWidth));
                    Assert.That(patch.Height, Is.EqualTo(CommitViewerLayout.DefaultWindowHeight));
                    Assert.That(single.Height, Is.EqualTo(CommitViewerLayout.DefaultWindowHeight));
                    Assert.That(multi.Height, Is.EqualTo(CommitViewerLayout.DefaultWindowHeight));
                    Assert.That(single.MinWidth, Is.EqualTo(720));
                    Assert.That(single.MinHeight, Is.EqualTo(440));
                    Assert.That(single.ResizeMode, Is.EqualTo(ResizeMode.CanResizeWithGrip));
                    Assert.That(patch.ResizeMode, Is.EqualTo(ResizeMode.CanResizeWithGrip));
                });
            }
            finally
            {
                patch?.Close();
                single?.Close();
                range?.Close();
                multi?.Close();
                app.Resources.MergedDictionaries.Remove(fonts);
            }
        });

    [TestCase(1500, 900)]
    [TestCase(640, 300)]
    public void ApplyWorkAreaSizing_ClampsFirstOpenBoundsForPatchAndRepositoryPaths(
        double workingWidth, double workingHeight) =>
        WpfTestContext.Run(() =>
        {
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var fonts = new ResourceDictionary
            {
                ["FontSizeBody"] = 13.0, ["FontSizeNormal"] = 13.0,
                ["FontSizeSmall"] = 12.0, ["FontSizeSubtitle"] = 18.0
            };
            app.Resources.MergedDictionaries.Add(fonts);
            CommitViewerWindow? patch = null;
            CommitViewerWindow? repo = null;
            try
            {
                var workArea = new Rect(100, 50, workingWidth, workingHeight);
                patch = (CommitViewerWindow)Activator.CreateInstance(typeof(CommitViewerWindow),
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    binder: null,
                    args: ["file.cs", "@@ -1 +1 @@\n-old\n+new"],
                    culture: null)!;
                repo = (CommitViewerWindow)Activator.CreateInstance(typeof(CommitViewerWindow),
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    binder: null,
                    args: [".", new CommitViewerEntry[] { new("abcdef12"), new("fedcba98") }, null, null],
                    culture: null)!;

                InvokeViewer(patch, "ApplyWorkAreaSizing", workArea);
                InvokeViewer(repo, "ApplyWorkAreaSizing", workArea);

                Assert.Multiple(() =>
                {
                    Assert.That(patch.Width, Is.EqualTo(workArea.Width));
                    Assert.That(patch.Height, Is.EqualTo(workArea.Height));
                    Assert.That(patch.Left, Is.EqualTo(workArea.Left));
                    Assert.That(patch.Top, Is.EqualTo(workArea.Top));
                    Assert.That(patch.WindowStartupLocation, Is.EqualTo(WindowStartupLocation.Manual));
                    Assert.That(patch.MinWidth, Is.LessThanOrEqualTo(workArea.Width));
                    Assert.That(patch.MinHeight, Is.LessThanOrEqualTo(workArea.Height));

                    Assert.That(repo.Width, Is.EqualTo(workArea.Width));
                    Assert.That(repo.Height, Is.EqualTo(workArea.Height));
                    Assert.That(repo.Left, Is.EqualTo(workArea.Left));
                    Assert.That(repo.Top, Is.EqualTo(workArea.Top));
                    Assert.That(repo.WindowStartupLocation, Is.EqualTo(WindowStartupLocation.Manual));
                    Assert.That(repo.MinWidth, Is.LessThanOrEqualTo(workArea.Width));
                    Assert.That(repo.MinHeight, Is.LessThanOrEqualTo(workArea.Height));
                });
            }
            finally
            {
                patch?.Close();
                repo?.Close();
                app.Resources.MergedDictionaries.Remove(fonts);
            }
        });

    [Test]
    public void ApplyWorkAreaSizing_PreservesRestoredGeometryAndClampsWhenNeeded() =>
        WpfTestContext.Run(() =>
        {
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var fonts = new ResourceDictionary
            {
                ["FontSizeBody"] = 13.0, ["FontSizeNormal"] = 13.0,
                ["FontSizeSmall"] = 12.0, ["FontSizeSubtitle"] = 18.0
            };
            app.Resources.MergedDictionaries.Add(fonts);
            CommitViewerWindow? window = null;
            try
            {
                var workArea = new Rect(100, 50, 1800, 900);
                window = (CommitViewerWindow)Activator.CreateInstance(typeof(CommitViewerWindow),
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    binder: null,
                    args: [".", new CommitViewerEntry[] { new("abcdef12") }, null, null],
                    culture: null)!;
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = 140;
                window.Top = 80;
                window.Width = 1200;
                window.Height = 700;

                InvokeViewer(window, "ApplyWorkAreaSizing", workArea);
                Assert.Multiple(() =>
                {
                    Assert.That(window.Left, Is.EqualTo(140));
                    Assert.That(window.Top, Is.EqualTo(80));
                    Assert.That(window.Width, Is.EqualTo(1200));
                    Assert.That(window.Height, Is.EqualTo(700));
                });

                window.Left = -300;
                window.Top = -200;
                window.Width = 2600;
                window.Height = 1600;
                InvokeViewer(window, "ApplyWorkAreaSizing", workArea);
                Assert.Multiple(() =>
                {
                    Assert.That(window.Left, Is.EqualTo(workArea.Left));
                    Assert.That(window.Top, Is.EqualTo(workArea.Top));
                    Assert.That(window.Width, Is.EqualTo(workArea.Width));
                    Assert.That(window.Height, Is.EqualTo(workArea.Height));
                });
            }
            finally
            {
                window?.Close();
                app.Resources.MergedDictionaries.Remove(fonts);
            }
        });

    [TestCase(0.75, false)]
    [TestCase(1.0, false)]
    [TestCase(1.6, false)]
    [TestCase(0.75, true)]
    [TestCase(1.0, true)]
    [TestCase(1.6, true)]
    public void Navigation_UsesEnvironmentScaleAndRoutesZoomWithoutChangingDiff(double initialScale, bool binary) =>
        WpfTestContext.Run(() =>
        {
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var keys = new[] { "FontSizeBody", "FontSizeNormal", "FontSizeSmall", "FontSizeSubtitle" };
            var saved = keys.Where(app.Resources.Contains).ToDictionary(key => key, key => app.Resources[key]);
            foreach (var key in saved.Keys)
                app.Resources.Remove(key);

            static ResourceDictionary FontResources(double scale) => new()
            {
                ["FontSizeBody"] = Math.Round(13 * scale, 1),
                ["FontSizeNormal"] = Math.Round(13 * scale, 1),
                ["FontSizeSmall"] = Math.Round(12 * scale, 1),
                ["FontSizeSubtitle"] = Math.Round(18 * scale, 1)
            };

            var fonts = FontResources(initialScale);
            app.Resources.MergedDictionaries.Add(fonts);
            CommitViewerWindow? window = null;
            try
            {
                window = (CommitViewerWindow)Activator.CreateInstance(typeof(CommitViewerWindow),
                    BindingFlags.Instance | BindingFlags.NonPublic, binder: null,
                    args: [".", new CommitViewerEntry[] { new("abcdef12"), new("abcdef34") }, null, null],
                    culture: null)!;
                var fileType = typeof(CommitViewerWindow).GetNestedType("CommitFile", BindingFlags.NonPublic)!;
                var file = Activator.CreateInstance(fileType, "SquadDash/CommitViewerWindow.cs", 12, 3, binary)!;
                var item = (ListBoxItem)InvokeViewer(window, "CreateFileItem", file)!;
                var fileList = ViewerField<ListBox>(window, "_fileList");
                fileList.Items.Add(item);
                var row = (Grid)item.Content;
                var path = (TextBlock)row.Children[0];
                var summary = (TextBlock)row.Children[1];
                var commitList = ViewerField<ListBox>(window, "_commitList");
                var commitLabel = new TextBlock { Text = "Commit subject" };
                commitList.Items.Add(new ListBoxItem { Content = commitLabel });
                InvokeViewer(window, "RenderDiff", "diff --git a/file b/file\n@@ -1 +1 @@\n-old\n+new");
                var diffPanel = ViewerField<StackPanel>(window, "_diffPanel");
                var diffScroll = (ScrollViewer)diffPanel.Parent;
                var diffSizes = diffPanel.Children.OfType<TextBlock>().Select(line => line.FontSize).ToArray();

                void AssertScale(double scale)
                {
                    window.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
                    Assert.Multiple(() =>
                    {
                        Assert.That(path.FontSize, Is.EqualTo(Math.Round(13 * scale, 1)));
                        Assert.That(summary.FontSize, Is.EqualTo(path.FontSize));
                        foreach (var run in summary.Inlines.OfType<Run>())
                            Assert.That(run.FontSize, Is.EqualTo(path.FontSize));
                        Assert.That(commitLabel.FontSize, Is.EqualTo(path.FontSize));
                        Assert.That(ViewerField<TextBlock>(window, "_status").FontSize, Is.EqualTo(path.FontSize));
                        Assert.That(ViewerField<TextBlock>(window, "_heading").FontSize, Is.EqualTo(Math.Round(18 * scale, 1)));
                        Assert.That(diffPanel.Children.OfType<TextBlock>().Select(line => line.FontSize), Is.EqualTo(diffSizes));
                        Assert.That(fileList.Items[0], Is.SameAs(item), "Existing rows must update without rebuilding.");
                    });
                }

                AssertScale(initialScale);
                var calls = 0;
                window.ConfigureEnvironmentFontZoom((_, e) =>
                {
                    calls++;
                    var next = FontResources(e.Delta > 0 ? 1.6 : 0.75);
                    app.Resources.MergedDictionaries[app.Resources.MergedDictionaries.IndexOf(fonts)] = next;
                    fonts = next;
                    e.Handled = true;
                });

                static MouseWheelEventArgs Wheel(int delta) => new(Mouse.PrimaryDevice, Environment.TickCount, delta)
                {
                    RoutedEvent = UIElement.PreviewMouseWheelEvent
                };

                var fileWheel = Wheel(120);
                path.RaiseEvent(fileWheel);
                Assert.That(fileWheel.Handled, Is.True);
                Assert.That(calls, Is.EqualTo(1));
                AssertScale(1.6);
                commitLabel.RaiseEvent(Wheel(-120));
                Assert.That(calls, Is.EqualTo(2));
                AssertScale(0.75);

                // A zero-delta event cannot change the persisted code zoom, even if Ctrl is physically held.
                diffScroll.RaiseEvent(Wheel(0));
                Assert.That(calls, Is.EqualTo(2), "The environment handler must not receive diff-pane input.");
                AssertScale(0.75);
            }
            finally
            {
                window?.Close();
                app.Resources.MergedDictionaries.Remove(fonts);
                foreach (var (key, value) in saved)
                    app.Resources[key] = value;
            }
        });

    private static T ViewerField<T>(CommitViewerWindow window, string name) =>
        (T)typeof(CommitViewerWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;

    private static object? InvokeViewer(CommitViewerWindow window, string name, params object[] arguments) =>
        typeof(CommitViewerWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, arguments);

    [Test]
    public void FileListAutoFit_WiresHardenedSplitterWithGuardsAndWidthAssignment()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "SquadDash", "CommitViewerWindow.cs")))
            directory = directory.Parent;

        Assert.That(directory, Is.Not.Null, "Could not find CommitViewerWindow.cs.");
        var source = File.ReadAllText(Path.Combine(directory!.FullName, "SquadDash", "CommitViewerWindow.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("CommitViewerLayout.CreateFileListSplitter((sender, e) =>"));
            Assert.That(source, Does.Match(
                @"if \(e\.ClickCount != 2\)\s*\{\s*return;\s*\}\s*" +
                @"if \(_fileList\.Items\.Count == 0\)\s*\{\s*return;\s*\}\s*e\.Handled = true;"));
            Assert.That(source, Does.Contain("var fileColumn = split.ColumnDefinitions[offset];"));
            Assert.That(source, Does.Contain(".Select(CommitViewerLayout.MeasureFileListItemWidth).ToArray()"));
            Assert.That(source, Does.Match(
                @"var fittedWidth = new GridLength\(CommitViewerLayout\.CalculateFileListWidth\(\s*" +
                @"itemWidths,\s*chromeWidth, availableWidth, fileColumn\.ActualWidth\)\);\s*" +
                @"fileColumn\.Width = fittedWidth;"));
            Assert.That(source.IndexOf("split.Children.Add(splitter)", StringComparison.Ordinal),
                Is.GreaterThan(source.IndexOf("split.Children.Add(diffScroll)", StringComparison.Ordinal)));
            Assert.That(source, Does.Not.Contain("splitter.PreviewMouseDoubleClick +="));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    [Apartment(ApartmentState.STA)]
    public void FileListSplitter_ReceivesPreviewInputEvenWhenAlreadyHandled(bool handled)
    {
        MouseButtonEventArgs? received = null;
        var splitter = CommitViewerLayout.CreateFileListSplitter((_, e) => received = e);
        var input = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
            Handled = handled
        };

        splitter.RaiseEvent(input);

        Assert.Multiple(() =>
        {
            Assert.That(received, Is.SameAs(input));
            Assert.That(input.Handled, Is.EqualTo(handled), "Registration must not consume ordinary drag input.");
        });
    }

    [TestCase(0)]
    [TestCase(2)]
    [Apartment(ApartmentState.STA)]
    public void FileListSplitter_HitTargetFillsItsColumnAboveOverlappingSiblings(int offset)
    {
        var split = new Grid();
        for (var column = 0; column < offset; column++)
            split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CommitViewerLayout.FileListSplitterWidth) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var splitter = CommitViewerLayout.CreateFileListSplitter((_, _) => { });
        Grid.SetColumn(splitter, offset + 1);
        split.Children.Add(splitter);
        var overlappingSibling = new Border { Background = Brushes.Red };
        Grid.SetColumnSpan(overlappingSibling, split.ColumnDefinitions.Count);
        split.Children.Add(overlappingSibling);
        split.Measure(new Size(1100, 600));
        split.Arrange(new Rect(0, 0, 1100, 600));
        split.UpdateLayout();

        Assert.Multiple(() =>
        {
            Assert.That(splitter.ActualWidth, Is.EqualTo(8));
            Assert.That(splitter.ActualHeight, Is.EqualTo(600));
            Assert.That(splitter.ResizeDirection, Is.EqualTo(GridResizeDirection.Columns));
            Assert.That(splitter.ResizeBehavior, Is.EqualTo(GridResizeBehavior.PreviousAndNext));
            foreach (var x in new[] { 0.5, 4.0, 7.5 })
            foreach (var y in new[] { 1.0, 300.0, 599.0 })
            {
                var point = splitter.TranslatePoint(new Point(x, y), split);
                var hit = VisualTreeHelper.HitTest(split, point)?.VisualHit;
                Assert.That(hit is not null && (hit == splitter || splitter.IsAncestorOf(hit)), Is.True,
                    $"Divider must receive input at ({x}, {y}) even without a LineColor resource.");
            }
        });
    }

    [Test]
    public async Task ResolveRepositoryRootAsync_FromNestedWorkspace_ReturnsTopLevelDirectory()
    {
        var repositoryRoot = Path.Combine(
            Path.GetTempPath(),
            "SquadDashCommitViewer",
            Guid.NewGuid().ToString("N"));
        var nestedWorkspace = Path.Combine(repositoryRoot, "Package");
        Directory.CreateDirectory(nestedWorkspace);

        try
        {
            await CommitViewerGit.RunAsync(repositoryRoot, "init", "--quiet");

            var resolved = await CommitViewerGit.ResolveRepositoryRootAsync(nestedWorkspace);

            Assert.That(resolved, Is.EqualTo(Path.GetFullPath(repositoryRoot)).IgnoreCase);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    [TestCase(1920, 1680, 720, 1680)]
    [TestCase(1600, 1680, 720, 1600)]
    [TestCase(640, 1680, 720, 640)]
    public void CalculateWindowWidth_UsesPreferredDefaultClampedToWorkArea(
        double workingWidth,
        double preferredWidth,
        double minimumWidth,
        double expected)
    {
        Assert.That(
            CommitViewerLayout.CalculateWindowWidth(workingWidth, preferredWidth, minimumWidth),
            Is.EqualTo(expected));
    }

    [TestCase(1040, 1140, 440, 1040)]
    [TestCase(900, 700, 440, 700)]
    [TestCase(300, 1140, 440, 300)]
    public void CalculateWindowHeight_UsesPreferredDefaultClampedToWorkArea(
        double workingHeight,
        double preferredHeight,
        double minimumHeight,
        double expected)
    {
        Assert.That(
            CommitViewerLayout.CalculateWindowHeight(workingHeight, preferredHeight, minimumHeight),
            Is.EqualTo(expected));
    }

    [Test]
    public void ClampWindowBounds_ClampsBothSizeAndPositionToWorkArea()
    {
        var clamped = CommitViewerLayout.ClampWindowBounds(
            new Rect(-300, -200, 2600, 1600),
            new Rect(100, 50, 1800, 900),
            minimumWidth: 720,
            minimumHeight: 440);

        Assert.That(clamped, Is.EqualTo(new Rect(100, 50, 1800, 900)));
    }

    [Test]
    public void CalculateFileListWidth_FitsWidestCompleteRowPlusListChrome()
    {
        var width = CommitViewerLayout.CalculateFileListWidth([120, 501.25, 400], 27, 1100, 330);

        Assert.That(width, Is.EqualTo(529));
    }

    [TestCase(100, 1100, 330)]
    [TestCase(2000, 1100, 860)]
    [TestCase(2000, 1600, 1280)]
    [TestCase(2000, 700, 460)]
    [TestCase(2000, 500, 260)]
    [TestCase(2000, 200, 0)]
    public void CalculateFileListWidth_PreservesDiffSpaceWithinRemainingPaneWidth(
        double itemWidth, double availableWidth, double expected)
    {
        Assert.That(
            CommitViewerLayout.CalculateFileListWidth([itemWidth], 27, availableWidth, 330),
            Is.EqualTo(expected));
    }

    [Test]
    public void CalculateFileListWidth_EmptyListKeepsCurrentWidth()
    {
        Assert.That(
            CommitViewerLayout.CalculateFileListWidth([], 27, 1100, 415),
            Is.EqualTo(415));
    }

    [TestCase(12, false)]
    [TestCase(24, false)]
    [TestCase(24, true)]
    [Apartment(ApartmentState.STA)]
    public void MeasureFileListItemWidth_IncludesUntrimmedPathSummaryAndChrome(double fontSize, bool binary)
    {
        var path = new TextBlock
        {
            Text = "SquadDash/VeryLongDirectory/NestedDirectory/CommitViewerWindow.cs",
            FontSize = fontSize,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(2, 0, 8, 0)
        };
        var summary = new TextBlock { FontSize = fontSize };
        if (binary)
            summary.Text = "binary";
        else
        {
            summary.Inlines.Add(new Run("+12345") { FontWeight = FontWeights.SemiBold });
            summary.Inlines.Add(new Run("  -67890") { FontWeight = FontWeights.SemiBold });
        }

        var row = new Grid { Margin = new Thickness(3, 5, 3, 5) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(summary, 1);
        row.Children.Add(path);
        row.Children.Add(summary);
        var item = new ListBoxItem { Content = row, Padding = new Thickness(4), BorderThickness = new Thickness(1) };
        item.Measure(new Size(200, double.PositiveInfinity));

        var width = CommitViewerLayout.MeasureFileListItemWidth(item);
        var dpi = VisualTreeHelper.GetDpi(path).PixelsPerDip;
        var pathWidth = new FormattedText(path.Text, CultureInfo.CurrentCulture, path.FlowDirection,
            new Typeface(path.FontFamily, path.FontStyle, path.FontWeight, path.FontStretch),
            fontSize, Brushes.Black, dpi).WidthIncludingTrailingWhitespace;
        var summaryWidth = new FormattedText(binary ? "binary" : "+12345  -67890",
            CultureInfo.CurrentCulture, summary.FlowDirection,
            new Typeface(summary.FontFamily, summary.FontStyle,
                binary ? summary.FontWeight : FontWeights.SemiBold, summary.FontStretch),
            fontSize, Brushes.Black, dpi).WidthIncludingTrailingWhitespace;

        Assert.Multiple(() =>
        {
            Assert.That(width, Is.GreaterThanOrEqualTo(pathWidth + summaryWidth + 26 - 1));
            Assert.That(width, Is.LessThan(pathWidth + summaryWidth + 30));
            Assert.That(path.TextTrimming, Is.EqualTo(TextTrimming.CharacterEllipsis));
            Assert.That(item.IsMeasureValid, Is.False, "Normal constrained layout must run again.");
        });
    }

    [Test]
    public void UncertainCommitToolTip_IsConciseAndDoesNotRepeatCommitDetails()
    {
        var text = CommitViewerLayout.BuildUncertainCommitToolTip(
            "The commit predates the captured task baseline.");

        Assert.Multiple(() =>
        {
            Assert.That(text, Is.EqualTo(
                "SquadDash included this commit as evidence, but could not confirm that it belongs to this step."));
            Assert.That(text, Does.Not.Contain("Why it is uncertain"));
            Assert.That(text, Does.Not.Contain("predates"));
        });
    }

    [TestCase(PlanRecoveryCommitRelation.Unknown, "Unclear.", true)]
    [TestCase(PlanRecoveryCommitRelation.Task, "This task commit predates the baseline.", false)]
    [TestCase(PlanRecoveryCommitRelation.Task, "This task commit predates baseline.", false)]
    [TestCase(PlanRecoveryCommitRelation.Task, "Confirmed inside the captured range.", false)]
    [TestCase(PlanRecoveryCommitRelation.Unrelated, "Confirmed recovery infrastructure.", true)]
    public void AttributionUncertainty_MarksCommitsNotConfirmedAsPartOfTheStep(
        string relation,
        string explanation,
        bool expected)
    {
        Assert.That(
            CommitViewerLayout.IsCommitAttributionUncertain(relation, explanation),
            Is.EqualTo(expected));
    }

    [Test]
    public void TaskCommitBeforeRecoveryBaseline_UsesPlainLanguageHint()
    {
        var hint = CommitViewerLayout.BuildCommitToolTip(
            false,
            "This is the primary implementation commit; it predates the baseline.");

        Assert.That(hint, Is.EqualTo(
            "This commit appears to implement this step, but an interruption prevented SquadDash " +
            "from automatically confirming the step as complete."));
    }
}
