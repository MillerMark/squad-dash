using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class TranscriptTitleContextScannerTests
{
    [Test]
    public void Scan_VisibleQuickReply_ReplacesDayOldIntentAboveViewport()
    {
        WpfTestContext.Run(() =>
        {
            using var host = new ScannerHost();
            var now = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
            var oldIntent = host.AddToolEntry("Yesterday's work", now.AddDays(-1));
            host.AddFillerLines(30, "history");
            var prompt = host.AddPrompt("Yes - implement it", now, "Yes - implement it");
            host.AddFillerLines(120, "tail");
            host.UpdateLayout();
            host.AssertScrollable();
            host.ScrollToPrompt(prompt, desiredTop: 80);

            Assert.That(host.GetElementRect(oldIntent.Expander).Bottom, Is.LessThan(0));
            Assert.That(host.GetPromptRect(prompt).Top, Is.InRange(0, host.ScrollViewer.ViewportHeight));
            Assert.That(TranscriptTitleContextScanner.Scan(host.Thread, host.ToolEntries, host.ScrollViewer),
                Is.EqualTo(("Yes - implement it", (DateTimeOffset?)now)));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Scan_ClosestAboveFallback_ComparesBothCandidateKinds(bool promptIsLast)
    {
        WpfTestContext.Run(() =>
        {
            using var host = new ScannerHost();
            var now = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
            Paragraph prompt;
            ToolTranscriptEntry intent;
            if (promptIsLast)
            {
                intent = host.AddToolEntry("intent", now.AddDays(-1));
                prompt = host.AddPrompt("prompt", now, "prompt");
            }
            else
            {
                prompt = host.AddPrompt("prompt", now.AddDays(-1), "prompt");
                intent = host.AddToolEntry("intent", now);
            }
            host.AddFillerLines(120, "tail");
            host.UpdateLayout();
            host.AssertScrollable();
            host.ScrollViewer.ScrollToBottom();
            host.PumpLayout();

            Assert.That(host.GetPromptRect(prompt).Bottom, Is.LessThan(0));
            Assert.That(host.GetElementRect(intent.Expander).Bottom, Is.LessThan(0));
            Assert.That(TranscriptTitleContextScanner.Scan(host.Thread, host.ToolEntries, host.ScrollViewer),
                Is.EqualTo((promptIsLast ? "prompt" : "intent", (DateTimeOffset?)now)));
        });
    }

    [Test]
    public void Scan_VisiblePromptBelowOlderVisibleIntent_SelectsPrompt()
    {
        WpfTestContext.Run(() =>
        {
            using var host = new ScannerHost();
            var t0 = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

            host.AddFillerLines(30, "top");
            var oldIntent = host.AddToolEntry("day-old intent", t0.AddMinutes(1), showEntryExpander: true);
            host.AddFillerLines(4, "between");
            var quickReply = host.AddPrompt("Quick reply body", t0.AddMinutes(2), "Quick reply body");
            host.AddFillerLines(120, "tail");

            host.UpdateLayout();
            host.AssertScrollable();
            host.ScrollToElement(oldIntent.Expander, desiredTop: 20);

            var oldIntentRect = host.GetElementRect(oldIntent.Expander);
            var quickReplyRect = host.GetPromptRect(quickReply);
            Assert.Multiple(() =>
            {
                Assert.That(oldIntentRect.Top, Is.LessThan(quickReplyRect.Top), "Setup failed: old intent must be above quick reply.");
                Assert.That(oldIntentRect.Top, Is.LessThan(host.ScrollViewer.ViewportHeight), "Setup failed: old intent should be visible.");
                Assert.That(oldIntentRect.Bottom, Is.GreaterThan(0), "Setup failed: old intent should be visible.");
                Assert.That(quickReplyRect.Top, Is.LessThan(host.ScrollViewer.ViewportHeight), "Setup failed: quick reply should be visible.");
                Assert.That(quickReplyRect.Bottom, Is.GreaterThan(0), "Setup failed: quick reply should be visible.");
            });

            var result = TranscriptTitleContextScanner.Scan(host.Thread, host.ToolEntries, host.ScrollViewer);
            Assert.Multiple(() =>
            {
                Assert.That(result.Intent, Is.EqualTo("Quick reply body"));
                Assert.That(result.Timestamp, Is.EqualTo(t0.AddMinutes(2)));
            });
        });
    }

    [Test]
    public void Scan_VisibleIntentBelowPrompt_SelectsIntent()
    {
        WpfTestContext.Run(() =>
        {
            using var host = new ScannerHost();
            var t0 = new DateTimeOffset(2026, 10, 10, 13, 0, 0, TimeSpan.Zero);

            host.AddFillerLines(30, "top");
            var prompt = host.AddPrompt("Prompt body", t0.AddMinutes(2), "Prompt title");
            host.AddFillerLines(4, "between");
            var laterIntent = host.AddToolEntry("later visible intent", t0.AddMinutes(3), showEntryExpander: true);
            host.AddFillerLines(120, "tail");

            host.UpdateLayout();
            host.AssertScrollable();
            host.ScrollToPrompt(prompt, desiredTop: 80);

            var promptRect = host.GetPromptRect(prompt);
            var intentRect = host.GetElementRect(laterIntent.Expander);
            Assert.Multiple(() =>
            {
                Assert.That(promptRect.Top, Is.LessThan(intentRect.Top), "Setup failed: prompt should be above later intent.");
                Assert.That(promptRect.Bottom, Is.GreaterThan(0), "Setup failed: prompt should be visible.");
                Assert.That(promptRect.Top, Is.LessThan(host.ScrollViewer.ViewportHeight), "Setup failed: prompt should be visible.");
                Assert.That(intentRect.Bottom, Is.GreaterThan(0), "Setup failed: intent should be visible.");
                Assert.That(intentRect.Top, Is.LessThan(host.ScrollViewer.ViewportHeight), "Setup failed: intent should be visible.");
            });

            var result = TranscriptTitleContextScanner.Scan(host.Thread, host.ToolEntries, host.ScrollViewer);
            Assert.Multiple(() =>
            {
                Assert.That(result.Intent, Is.EqualTo("later visible intent"));
                Assert.That(result.Timestamp, Is.EqualTo(t0.AddMinutes(3)));
            });
        });
    }

    [Test]
    public void Scan_WhenScrolledToHistory_IgnoresLaterOffscreenCandidates()
    {
        WpfTestContext.Run(() =>
        {
            using var host = new ScannerHost();
            var t0 = new DateTimeOffset(2026, 10, 10, 14, 0, 0, TimeSpan.Zero);

            host.AddFillerLines(30, "top");
            var historicalPrompt = host.AddPrompt("Older prompt body", t0.AddMinutes(1), "Older prompt title");
            host.AddFillerLines(120, "middle");
            var laterIntent = host.AddToolEntry("later offscreen intent", t0.AddMinutes(2), showEntryExpander: true);
            host.AddFillerLines(120, "tail");

            host.UpdateLayout();
            host.AssertScrollable();
            host.ScrollToPrompt(historicalPrompt, desiredTop: 80);

            var historicalRect = host.GetPromptRect(historicalPrompt);
            var laterRect = host.GetElementRect(laterIntent.Expander);
            Assert.Multiple(() =>
            {
                Assert.That(historicalRect.Bottom, Is.GreaterThan(0), "Setup failed: historical prompt should be visible.");
                Assert.That(historicalRect.Top, Is.LessThan(host.ScrollViewer.ViewportHeight), "Setup failed: historical prompt should be visible.");
                Assert.That(laterRect.Top, Is.GreaterThanOrEqualTo(host.ScrollViewer.ViewportHeight),
                    "Setup failed: later candidate must be below viewport.");
            });

            var result = TranscriptTitleContextScanner.Scan(host.Thread, host.ToolEntries, host.ScrollViewer);
            Assert.Multiple(() =>
            {
                Assert.That(result.Intent, Is.EqualTo("Older prompt title"));
                Assert.That(result.Timestamp, Is.EqualTo(t0.AddMinutes(1)));
            });
        });
    }

    [Test]
    public void Scan_ClosestAboveFallback_UsesPromptWhenNoCandidateVisible()
    {
        WpfTestContext.Run(() =>
        {
            using var host = new ScannerHost();
            var t0 = new DateTimeOffset(2026, 10, 10, 15, 0, 0, TimeSpan.Zero);

            host.AddFillerLines(10, "top");
            var prompt = host.AddPrompt("Prompt above viewport", t0.AddMinutes(1), "Prompt above viewport");
            host.AddFillerLines(220, "tail");

            host.UpdateLayout();
            host.AssertScrollable();
            host.ScrollViewer.ScrollToBottom();
            host.PumpLayout();

            var rect = host.GetPromptRect(prompt);
            Assert.That(rect.Bottom, Is.LessThanOrEqualTo(0), "Setup failed: prompt must be above viewport.");

            var result = TranscriptTitleContextScanner.Scan(host.Thread, host.ToolEntries, host.ScrollViewer);
            Assert.Multiple(() =>
            {
                Assert.That(result.Intent, Is.EqualTo("Prompt above viewport"));
                Assert.That(result.Timestamp, Is.EqualTo(t0.AddMinutes(1)));
            });
        });
    }

    [Test]
    public void Scan_ClosestAboveFallback_UsesIntentWhenNoCandidateVisible()
    {
        WpfTestContext.Run(() =>
        {
            using var host = new ScannerHost();
            var t0 = new DateTimeOffset(2026, 10, 10, 16, 0, 0, TimeSpan.Zero);

            host.AddFillerLines(12, "top");
            var intent = host.AddToolEntry("intent above viewport", t0.AddMinutes(1), showEntryExpander: true);
            host.AddFillerLines(240, "tail");

            host.UpdateLayout();
            host.AssertScrollable();
            host.ScrollViewer.ScrollToBottom();
            host.PumpLayout();

            var rect = host.GetElementRect(intent.Expander);
            Assert.That(rect.Bottom, Is.LessThanOrEqualTo(0), "Setup failed: intent must be above viewport.");

            var result = TranscriptTitleContextScanner.Scan(host.Thread, host.ToolEntries, host.ScrollViewer);
            Assert.Multiple(() =>
            {
                Assert.That(result.Intent, Is.EqualTo("intent above viewport"));
                Assert.That(result.Timestamp, Is.EqualTo(t0.AddMinutes(1)));
            });
        });
    }

    [Test]
    public void Scan_PartiallyVisibleMultilinePrompt_TopOffscreen_StillCountsAsVisible()
    {
        WpfTestContext.Run(() =>
        {
            using var host = new ScannerHost();
            var t0 = new DateTimeOffset(2026, 10, 10, 17, 0, 0, TimeSpan.Zero);

            host.AddFillerLines(25, "top");
            var multilinePrompt = host.AddMultilinePrompt(t0.AddMinutes(1), "Multiline prompt title", lineCount: 10);
            host.AddFillerLines(200, "tail");

            host.UpdateLayout();
            host.AssertScrollable();

            var contentY = host.GetPromptContentTop(multilinePrompt);
            host.ScrollViewer.ScrollToVerticalOffset(Math.Max(0, contentY + 8));
            host.PumpLayout();

            var rect = host.GetPromptRect(multilinePrompt);
            Assert.Multiple(() =>
            {
                Assert.That(rect.Top, Is.LessThan(0), "Setup failed: prompt top should be above viewport.");
                Assert.That(rect.Bottom, Is.GreaterThan(0), "Setup failed: prompt should still be partially visible.");
            });

            var result = TranscriptTitleContextScanner.Scan(host.Thread, host.ToolEntries, host.ScrollViewer);
            Assert.Multiple(() =>
            {
                Assert.That(result.Intent, Is.EqualTo("Multiline prompt title"));
                Assert.That(result.Timestamp, Is.EqualTo(t0.AddMinutes(1)));
            });
        });
    }

    [Test]
    public void Scan_UsesCollapsedToolGroupHeader_WhenToolRowIsCollapsed()
    {
        WpfTestContext.Run(() =>
        {
            using var host = new ScannerHost();
            var t0 = new DateTimeOffset(2026, 10, 10, 18, 0, 0, TimeSpan.Zero);

            host.AddFillerLines(30, "top");
            var collapsed = host.AddToolEntry(
                "intent from collapsed row",
                t0.AddMinutes(1),
                showEntryExpander: false,
                showThinkingExpander: true,
                displayText: "collapsed header intent");
            host.AddFillerLines(160, "tail");

            host.UpdateLayout();
            host.AssertScrollable();
            host.ScrollToElement(collapsed.ThinkingBlock.Expander, desiredTop: 90);

            var rect = host.GetElementRect(collapsed.ThinkingBlock.Expander);
            Assert.Multiple(() =>
            {
                Assert.That(rect.Top, Is.LessThan(host.ScrollViewer.ViewportHeight), "Setup failed: header expander should be visible.");
                Assert.That(rect.Bottom, Is.GreaterThan(0), "Setup failed: header expander should be visible.");
            });

            var result = TranscriptTitleContextScanner.Scan(host.Thread, host.ToolEntries, host.ScrollViewer);
            Assert.Multiple(() =>
            {
                Assert.That(result.Intent, Is.EqualTo("collapsed header intent"));
                Assert.That(result.Timestamp, Is.EqualTo(t0.AddMinutes(1)));
            });
        });
    }

    [Test]
    public void Scan_IgnoresOtherThreadAndNonIntentTools()
    {
        WpfTestContext.Run(() =>
        {
            using var host = new ScannerHost();
            var t0 = new DateTimeOffset(2026, 10, 10, 19, 0, 0, TimeSpan.Zero);
            var otherThread = new TranscriptThreadState("other-thread", TranscriptThreadKind.Agent, "Other", t0);

            host.AddFillerLines(25, "top");
            var valid = host.AddToolEntry("valid intent", t0.AddMinutes(1), showEntryExpander: true);
            host.AddFillerLines(4, "between-a");
            var nonIntent = host.AddToolEntry("should-ignore-non-intent", t0.AddMinutes(2), showEntryExpander: true, toolName: "powershell");
            host.AddFillerLines(4, "between-b");
            var otherThreadIntent = host.AddToolEntry("should-ignore-other-thread", t0.AddMinutes(3), ownerThread: otherThread, showEntryExpander: true);
            host.AddFillerLines(130, "tail");

            host.UpdateLayout();
            host.AssertScrollable();
            host.ScrollToElement(valid.Expander, desiredTop: 70);

            var validRect = host.GetElementRect(valid.Expander);
            var nonIntentRect = host.GetElementRect(nonIntent.Expander);
            var otherThreadRect = host.GetElementRect(otherThreadIntent.Expander);
            Assert.Multiple(() =>
            {
                Assert.That(validRect.Bottom, Is.GreaterThan(0), "Setup failed: valid entry should be visible.");
                Assert.That(validRect.Top, Is.LessThan(host.ScrollViewer.ViewportHeight), "Setup failed: valid entry should be visible.");
                Assert.That(nonIntentRect.Top, Is.GreaterThan(validRect.Top), "Setup failed: non-intent should be lower and would win if not filtered.");
                Assert.That(otherThreadRect.Top, Is.GreaterThan(validRect.Top), "Setup failed: other-thread candidate should be lower and would win if not filtered.");
            });

            var result = TranscriptTitleContextScanner.Scan(host.Thread, host.ToolEntries, host.ScrollViewer);
            Assert.Multiple(() =>
            {
                Assert.That(result.Intent, Is.EqualTo("valid intent"));
                Assert.That(result.Timestamp, Is.EqualTo(t0.AddMinutes(1)));
            });
        });
    }

    [Test]
    public void Scan_IgnoresNullTitleSystemPrompt()
    {
        WpfTestContext.Run(() =>
        {
            using var host = new ScannerHost();
            var t0 = new DateTimeOffset(2026, 10, 10, 20, 0, 0, TimeSpan.Zero);

            host.AddFillerLines(25, "top");
            var titled = host.AddPrompt("User prompt body", t0.AddMinutes(1), "User prompt title");
            host.AddFillerLines(5, "between");
            var systemPrompt = host.AddPrompt("System-injected follow-up", t0.AddMinutes(2), title: null);
            host.AddFillerLines(140, "tail");

            host.UpdateLayout();
            host.AssertScrollable();
            host.ScrollToPrompt(titled, desiredTop: 60);

            var titledRect = host.GetPromptRect(titled);
            var systemRect = host.GetPromptRect(systemPrompt);
            Assert.Multiple(() =>
            {
                Assert.That(titledRect.Bottom, Is.GreaterThan(0), "Setup failed: titled prompt should be visible.");
                Assert.That(systemRect.Bottom, Is.GreaterThan(0), "Setup failed: null-title system prompt should be visible.");
                Assert.That(systemRect.Top, Is.GreaterThan(titledRect.Top), "Setup failed: null-title row should be lower and would win if not ignored.");
            });

            var result = TranscriptTitleContextScanner.Scan(host.Thread, host.ToolEntries, host.ScrollViewer);
            Assert.Multiple(() =>
            {
                Assert.That(result.Intent, Is.EqualTo("User prompt title"));
                Assert.That(result.Timestamp, Is.EqualTo(t0.AddMinutes(1)));
            });
        });
    }

    [Test]
    public void Scan_WithoutScrollViewer_UsesLatestTimestampAcrossPromptsAndIntents()
    {
        WpfTestContext.Run(() =>
        {
            var t0 = new DateTimeOffset(2026, 10, 10, 21, 0, 0, TimeSpan.Zero);
            var thread = new TranscriptThreadState("thread-1", TranscriptThreadKind.Coordinator, "Coordinator", t0);

            var promptParagraph = new Paragraph(new Run("prompt"));
            thread.PromptParagraphs.Add(new PromptEntry(promptParagraph, t0.AddMinutes(3), "latest prompt title"));
            thread.PromptParagraphs.Add(new PromptEntry(new Paragraph(new Run("system")), t0.AddMinutes(4), null));

            var toolEntries = new[]
            {
                CreateDetachedToolEntry(thread, "older intent", t0.AddMinutes(1)),
                CreateDetachedToolEntry(thread, "middle intent", t0.AddMinutes(2)),
            };

            var result = TranscriptTitleContextScanner.Scan(thread, toolEntries, scrollViewer: null);
            Assert.Multiple(() =>
            {
                Assert.That(result.Intent, Is.EqualTo("latest prompt title"));
                Assert.That(result.Timestamp, Is.EqualTo(t0.AddMinutes(3)));
            });
        });
    }

    [Test]
    public void Scan_WithoutScrollViewer_CanReturnLatestIntentWhenItIsNewest()
    {
        WpfTestContext.Run(() =>
        {
            var t0 = new DateTimeOffset(2026, 10, 10, 22, 0, 0, TimeSpan.Zero);
            var thread = new TranscriptThreadState("thread-2", TranscriptThreadKind.Agent, "Agent", t0);
            thread.PromptParagraphs.Add(new PromptEntry(new Paragraph(new Run("prompt")), t0.AddMinutes(1), "older prompt"));

            var toolEntries = new[]
            {
                CreateDetachedToolEntry(thread, "older intent", t0.AddMinutes(2)),
                CreateDetachedToolEntry(thread, "newest intent", t0.AddMinutes(4)),
            };

            var result = TranscriptTitleContextScanner.Scan(thread, toolEntries, scrollViewer: null);
            Assert.Multiple(() =>
            {
                Assert.That(result.Intent, Is.EqualTo("newest intent"));
                Assert.That(result.Timestamp, Is.EqualTo(t0.AddMinutes(4)));
            });
        });
    }

    [Test]
    public void MainWindow_WiresSharedScannerForCoordinatorAndSecondaryPaths()
    {
        var source = File.ReadAllText(Path.Combine(SourceDirectory(), "MainWindow.xaml.cs"));
        var scanCalls = Regex.Matches(source, "TranscriptTitleContextScanner\\.Scan\\(", RegexOptions.CultureInvariant).Count;

        Assert.Multiple(() =>
        {
            Assert.That(scanCalls, Is.EqualTo(2), "Scanner should be called from both coordinator and secondary transcript paths.");
            Assert.That(source, Does.Contain("CoordinatorThread, _agentThreadRegistry.ToolEntries.Values, _transcriptScrollViewer"));
            Assert.That(source, Does.Contain("entry.Thread, _agentThreadRegistry.ToolEntries.Values, scrollViewer"));
        });
    }

    [Test]
    public void MainWindow_PromptEntryTitleCapture_UsesCleanPromptBodyAndDeriveTitle()
    {
        var source = File.ReadAllText(Path.Combine(SourceDirectory(), "MainWindow.xaml.cs"));
        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("var promptBody = hasDictation ? displayPrompt[..^voiceAnnotation.Length] : displayPrompt;"));
            Assert.That(source, Does.Contain("new PromptEntry(promptParagraph, startedAt,"));
            Assert.That(source, Does.Contain("promptIsSystemInjected || suppressTranscript || string.IsNullOrWhiteSpace(promptBody)"));
            Assert.That(source, Does.Contain("? null : NotesStore.DeriveTitle(promptBody)"));
            Assert.That(source, Does.Not.Contain("NotesStore.DeriveTitle(new TextRange("));
        });
    }

    [Test]
    public void PromptTitleDerivation_UsesFirstLineAndWordBoundaryTruncation()
    {
        var promptBody = "This is a very long first line that should truncate at a word boundary safely\nSecond line should not be used";
        var derived = NotesStore.DeriveTitle(promptBody);

        Assert.Multiple(() =>
        {
            Assert.That(derived, Does.EndWith("…"));
            Assert.That(derived.Length, Is.LessThanOrEqualTo(41));
            Assert.That(derived, Does.Not.Contain('\n'));
            Assert.That(derived, Is.EqualTo("This is a very long first line that…"));
        });
    }

    private static ToolTranscriptEntry CreateDetachedToolEntry(
        TranscriptThreadState ownerThread,
        string intent,
        DateTimeOffset startedAt)
    {
        var turn = new TranscriptTurnView(ownerThread, "prompt", startedAt, new Section(), Array.Empty<Block>());
        var thinking = new TranscriptThinkingBlockView(
            turn,
            sequence: 1,
            headerTextBlock: new TextBlock(),
            expander: new Expander(),
            contentPanel: new StackPanel());

        return new ToolTranscriptEntry(
            toolCallId: Guid.NewGuid().ToString("N"),
            turn,
            thinking,
            new ToolTranscriptDescriptor("report_intent", Intent: intent),
            argsJson: null,
            startedAt,
            expander: new Expander(),
            iconTextBlock: new TextBlock(),
            emojiImage: new System.Windows.Controls.Image(),
            messageTextBlock: new TextBlock(),
            detailTextBox: new TextBox(),
            transcriptButton: new Button());
    }

    private static string SourceDirectory()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "SquadDash", "MainWindow.xaml.cs")))
            directory = directory.Parent;

        return Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("SquadDash source not found."), "SquadDash");
    }

    private sealed class ScannerHost : IDisposable
    {
        public ScannerHost()
        {
            var startedAt = new DateTimeOffset(2026, 10, 10, 10, 0, 0, TimeSpan.Zero);
            Thread = new TranscriptThreadState("test-thread", TranscriptThreadKind.Coordinator, "Coordinator", startedAt);
            Thread.Document.PagePadding = new Thickness(0);

            TranscriptBox = new RichTextBox
            {
                Width = 620,
                Height = 250,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Document = Thread.Document,
            };

            Window = new Window
            {
                Content = TranscriptBox,
                Width = 640,
                Height = 280,
                ShowActivated = false,
                ShowInTaskbar = false,
                Left = -12000,
                Top = -12000,
            };

            Window.Show();
            PumpLayout();

            ScrollViewer = TranscriptBox.Template?.FindName("PART_ContentHost", TranscriptBox) as ScrollViewer
                ?? throw new AssertionException("Failed to resolve RichTextBox ScrollViewer.");
        }

        public TranscriptThreadState Thread { get; }
        public RichTextBox TranscriptBox { get; }
        public Window Window { get; }
        public ScrollViewer ScrollViewer { get; }
        public List<ToolTranscriptEntry> ToolEntries { get; } = [];

        public void AddFillerLines(int lineCount, string prefix)
        {
            for (var i = 0; i < lineCount; i++)
                Thread.Document.Blocks.Add(new Paragraph(new Run($"{prefix} filler line {i:D3}")));
        }

        public Paragraph AddPrompt(string body, DateTimeOffset timestamp, string? title)
        {
            var paragraph = new Paragraph(new Run(body));
            Thread.Document.Blocks.Add(paragraph);
            Thread.PromptParagraphs.Add(new PromptEntry(paragraph, timestamp, title));
            return paragraph;
        }

        public Paragraph AddMultilinePrompt(DateTimeOffset timestamp, string title, int lineCount)
        {
            var paragraph = new Paragraph();
            for (var i = 0; i < lineCount; i++)
            {
                if (i > 0)
                    paragraph.Inlines.Add(new LineBreak());
                paragraph.Inlines.Add(new Run($"Multiline line {i:D2}"));
            }

            Thread.Document.Blocks.Add(paragraph);
            Thread.PromptParagraphs.Add(new PromptEntry(paragraph, timestamp, title));
            return paragraph;
        }

        public ToolTranscriptEntry AddToolEntry(
            string intent,
            DateTimeOffset timestamp,
            TranscriptThreadState? ownerThread = null,
            bool showEntryExpander = true,
            bool showThinkingExpander = false,
            string toolName = "report_intent",
            string? displayText = null)
        {
            ownerThread ??= Thread;
            var turn = new TranscriptTurnView(ownerThread, "prompt", timestamp, new Section(), Array.Empty<Block>());

            var thinkingExpander = CreateEntryExpander($"thinking:{intent}");
            thinkingExpander.Visibility = showThinkingExpander ? Visibility.Visible : Visibility.Collapsed;
            var thinking = new TranscriptThinkingBlockView(
                turn,
                sequence: 1,
                headerTextBlock: new TextBlock(),
                expander: thinkingExpander,
                contentPanel: new StackPanel());

            var entryExpander = CreateEntryExpander($"entry:{intent}");
            entryExpander.Visibility = showEntryExpander ? Visibility.Visible : Visibility.Collapsed;

            if (showEntryExpander)
                Thread.Document.Blocks.Add(new BlockUIContainer(entryExpander));
            if (showThinkingExpander)
                Thread.Document.Blocks.Add(new BlockUIContainer(thinkingExpander));

            var entry = new ToolTranscriptEntry(
                toolCallId: Guid.NewGuid().ToString("N"),
                turn,
                thinking,
                new ToolTranscriptDescriptor(toolName, Intent: intent, DisplayText: displayText),
                argsJson: null,
                timestamp,
                entryExpander,
                iconTextBlock: new TextBlock { Text = "•" },
                emojiImage: new System.Windows.Controls.Image(),
                messageTextBlock: new TextBlock { Text = intent },
                detailTextBox: new TextBox(),
                transcriptButton: new Button());

            ToolEntries.Add(entry);
            return entry;
        }

        public void UpdateLayout()
        {
            Window.UpdateLayout();
            PumpLayout();
        }

        public void PumpLayout()
        {
            Window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Window.UpdateLayout();
        }

        public void AssertScrollable()
        {
            PumpLayout();
            Assert.That(ScrollViewer.ScrollableHeight, Is.GreaterThan(1),
                "Setup failed: transcript must be scrollable so geometry selection is meaningful.");
        }

        public (double Top, double Bottom) GetPromptRect(Paragraph paragraph)
        {
            var renderScope = ScrollViewer.Content as FrameworkElement
                ?? throw new AssertionException("ScrollViewer content was not a FrameworkElement render scope.");
            Assert.That(paragraph.ContentStart.HasValidLayout, Is.True, "Prompt start pointer should have valid layout.");
            Assert.That(paragraph.ContentEnd.HasValidLayout, Is.True, "Prompt end pointer should have valid layout.");

            var start = paragraph.ContentStart.GetCharacterRect(LogicalDirection.Forward);
            var end = paragraph.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
            var top = renderScope.TranslatePoint(start.TopLeft, ScrollViewer).Y;
            var bottom = renderScope.TranslatePoint(end.BottomLeft, ScrollViewer).Y;
            return (top, bottom);
        }

        public double GetPromptContentTop(Paragraph paragraph)
        {
            var rect = GetPromptRect(paragraph);
            return rect.Top + ScrollViewer.VerticalOffset;
        }

        public (double Top, double Bottom) GetElementRect(FrameworkElement element)
        {
            var top = element.TranslatePoint(new Point(), ScrollViewer).Y;
            return (top, top + element.ActualHeight);
        }

        public void ScrollToPrompt(Paragraph paragraph, double desiredTop)
        {
            var contentTop = GetPromptContentTop(paragraph);
            ScrollViewer.ScrollToVerticalOffset(Math.Max(0, contentTop - desiredTop));
            PumpLayout();
        }

        public void ScrollToElement(FrameworkElement element, double desiredTop)
        {
            var rect = GetElementRect(element);
            var contentTop = rect.Top + ScrollViewer.VerticalOffset;
            ScrollViewer.ScrollToVerticalOffset(Math.Max(0, contentTop - desiredTop));
            PumpLayout();
        }

        public void Dispose()
        {
            Window.Close();
        }

        private static Expander CreateEntryExpander(string text)
        {
            return new Expander
            {
                Header = new TextBlock { Text = text },
                Content = new Border
                {
                    Height = 26,
                    Width = 500,
                    Child = new TextBlock { Text = text },
                },
                IsExpanded = false,
                Width = 520,
            };
        }
    }
}
