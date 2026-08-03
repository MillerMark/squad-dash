using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace SquadDash;

/// <summary>Tag stored on the transcript card's <see cref="BlockUIContainer"/> for identity tracking.</summary>
internal sealed record TranscriptApprovalCardTag(string PlanId, string GateId, int Version);

/// <summary>
/// Builds a themed, font-aware WPF card for a plan approval gate that can be inserted
/// into the coordinator transcript as a <see cref="BlockUIContainer"/>.
/// </summary>
internal static class TranscriptApprovalCardBuilder
{
    /// <summary>Result of building the card, with handles for later update/disable.</summary>
    internal sealed class CardResult
    {
        /// <summary>The <see cref="BlockUIContainer"/> to insert into the transcript <see cref="FlowDocument"/>.</summary>
        internal BlockUIContainer Container { get; init; } = null!;

        /// <summary>The primary approve button — disable when processing.</summary>
        internal Button ApproveButton { get; init; } = null!;

        /// <summary>Optional action that starts a free-form change-request conversation.</summary>
        internal Button? RequestChangesButton { get; init; }

        /// <summary>Optional note text box where the user can add a comment before approving.</summary>
        internal TextBox NoteTextBox { get; init; } = null!;

        /// <summary>Editable note section; hidden after the approval has been recorded.</summary>
        internal FrameworkElement NoteSection { get; init; } = null!;

        /// <summary>Read-only persisted note shown after approval, when one was supplied.</summary>
        internal TextBlock ResolutionNote { get; init; } = null!;

        /// <summary>Semi-transparent overlay shown during async approval processing.</summary>
        internal Border SpinnerOverlay { get; init; } = null!;

        /// <summary>Panel containing the approve button and any future action buttons.</summary>
        internal WrapPanel ActionsPanel { get; init; } = null!;

        /// <summary>Chrome-free check shown after the approval action has resolved.</summary>
        internal TextBlock ResolvedIndicator { get; init; } = null!;

        /// <summary>Live status shown while a change request is being described or reworked.</summary>
        internal TextBlock ReworkIndicator { get; init; } = null!;

        /// <summary>Root content stack containing all card sections.</summary>
        internal StackPanel ContentStack { get; init; } = null!;

        /// <summary>Link that opens the plan represented by this approval request.</summary>
        internal Hyperlink PlanLink { get; init; } = null!;

        /// <summary>Commit-evidence links rendered in the card.</summary>
        internal IReadOnlyList<Hyperlink> CommitLinks { get; init; } = [];
    }

    /// <summary>
    /// Creates the full approval card visual and wraps it in a <see cref="BlockUIContainer"/>
    /// ready for insertion into a <see cref="FlowDocument"/>.
    /// </summary>
    internal static CardResult Build(
        ApprovalReviewSnapshot snapshot,
        Plan plan,
        PlanApprovalGate gate,
        double fontSize,
        Action<string?> onApprove,
        Action? onRequestChanges = null,
        int requestVersion = 1,
        Action? onOpenPlan = null,
        Action<string>? onOpenCommit = null)
    {
        var activeGateCount = plan.ApprovalGates
            .Count(g => g.Status == PlanGateStatus.AwaitingApproval);
        var commitLinks = new List<Hyperlink>();

        var stack = new StackPanel { Margin = new Thickness(4) };

        // ── Header ───────────────────────────────────────────────────────
        var headerPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };

        var icon = new TextBlock
        {
            Text = "🔒",
            FontSize = fontSize + 4,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        DockPanel.SetDock(icon, Dock.Left);
        headerPanel.Children.Add(icon);

        var titleBlock = new TextBlock
        {
            Text = "Approval Required",
            FontSize = fontSize + 2,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "ImportantText");
        headerPanel.Children.Add(titleBlock);
        stack.Children.Add(headerPanel);

        // ── Plan progress ────────────────────────────────────────────────
        var progressBlock = CreateStyledTextBlock(string.Empty, fontSize, "LabelText");
        var planTitleLink = new Hyperlink(new Run(snapshot.PlanTitle))
        {
            Cursor = onOpenPlan is null ? Cursors.Arrow : Cursors.Hand,
            IsEnabled = onOpenPlan is not null,
            FontWeight = FontWeights.SemiBold,
            ToolTip = onOpenPlan is null
                ? null
                : ToolTipHelper.MakeThemedToolTip("Open this plan in the Plan Viewer"),
        };
        planTitleLink.SetResourceReference(TextElement.ForegroundProperty, "DocumentLinkText");
        if (onOpenPlan is not null)
            planTitleLink.Click += (_, _) => onOpenPlan();
        progressBlock.Inlines.Add(planTitleLink);
        var progressSuffix = $" — {snapshot.CompletedTaskCount}/{snapshot.TotalTaskCount} tasks complete";
        if (snapshot.CurrentStage is not null)
            progressSuffix += $" (stage: {snapshot.CurrentStage})";
        progressBlock.Inlines.Add(new Run(progressSuffix));
        progressBlock.Margin = new Thickness(0, 0, 0, 4);
        stack.Children.Add(progressBlock);

        // ── Gate reason ──────────────────────────────────────────────────
        var reasonBlock = CreateStyledTextBlock($"Gate: {snapshot.GateReason}", fontSize, "BodyText");
        reasonBlock.Margin = new Thickness(0, 0, 0, 8);
        reasonBlock.FontStyle = FontStyles.Italic;
        stack.Children.Add(reasonBlock);

        // ── Completed tasks with commit evidence ─────────────────────────
        if (snapshot.CompletedTasks.Count > 0)
        {
            var taskHeader = CreateStyledTextBlock(
                $"✅ {snapshot.CompletedTasks.Count} completed task(s) under review:",
                fontSize, "LabelText");
            taskHeader.FontWeight = FontWeights.Medium;
            taskHeader.Margin = new Thickness(0, 0, 0, 4);
            stack.Children.Add(taskHeader);

            foreach (var task in snapshot.CompletedTasks)
            {
                var taskPanel = new StackPanel { Margin = new Thickness(12, 0, 0, 2) };

                var taskTitle = CreateStyledTextBlock($"• {task.Title}", fontSize - 1, "LabelText");
                taskPanel.Children.Add(taskTitle);

                foreach (var commit in task.Commits)
                {
                    var commitLine = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Margin = new Thickness(12, 0, 0, 0),
                    };

                    var shaBlock = CreateStyledTextBlock(string.Empty, fontSize - 2, "SubtleText");
                    var shaLink = new Hyperlink(new Run(commit.Link.ShortSha))
                    {
                        Cursor = onOpenCommit is null ? Cursors.Arrow : Cursors.Hand,
                        IsEnabled = onOpenCommit is not null,
                        FontFamily = new FontFamily("Consolas"),
                        ToolTip = onOpenCommit is null
                            ? null
                            : ToolTipHelper.MakeThemedToolTip("Open this commit in the internal diff viewer"),
                    };
                    shaLink.SetResourceReference(TextElement.ForegroundProperty, "DocumentLinkText");
                    if (onOpenCommit is not null)
                    {
                        var capturedSha = commit.Link.FullSha;
                        shaLink.Click += (_, _) => onOpenCommit(capturedSha);
                    }
                    commitLinks.Add(shaLink);
                    shaBlock.Inlines.Add(shaLink);
                    commitLine.Children.Add(shaBlock);

                    var subjectBlock = CreateStyledTextBlock(
                        $"  {commit.Link.Subject}", fontSize - 2, "BodyText");
                    subjectBlock.TextTrimming = TextTrimming.CharacterEllipsis;
                    subjectBlock.MaxWidth = fontSize * 36;
                    commitLine.Children.Add(subjectBlock);

                    if (commit.VerificationPassed is bool verified)
                    {
                        var verifyIcon = CreateStyledTextBlock(
                            verified ? " ✓" : " ✗",
                            fontSize - 2,
                            verified ? "ToolSuccessIcon" : "ToolFailureIcon");
                        commitLine.Children.Add(verifyIcon);
                    }

                    taskPanel.Children.Add(commitLine);
                }
                stack.Children.Add(taskPanel);
            }

            stack.Children.Add(new Border { Height = 6 });
        }

        // ── Expandable changed-files section ─────────────────────────────
        if (snapshot.AllChangedFiles.Count > 0)
        {
            var filesExpander = BuildChangedFilesExpander(snapshot.AllChangedFiles, fontSize);
            stack.Children.Add(filesExpander);
        }

        // ── Downstream tasks ─────────────────────────────────────────────
        if (snapshot.DownstreamTasks.Count > 0)
        {
            var downstreamHeader = CreateStyledTextBlock(
                $"⏭ {snapshot.DownstreamTasks.Count} task(s) unblocked by approval:",
                fontSize - 1, "SubtleText");
            downstreamHeader.Margin = new Thickness(0, 4, 0, 2);
            stack.Children.Add(downstreamHeader);

            foreach (var dt in snapshot.DownstreamTasks.Take(5))
            {
                var dtLine = CreateStyledTextBlock($"  → {dt.Title} ({dt.Status})", fontSize - 1, "SubtleText");
                stack.Children.Add(dtLine);
            }
            if (snapshot.DownstreamTasks.Count > 5)
            {
                var moreBlock = CreateStyledTextBlock(
                    $"  … and {snapshot.DownstreamTasks.Count - 5} more",
                    fontSize - 1, "SubtleText");
                stack.Children.Add(moreBlock);
            }
            stack.Children.Add(new Border { Height = 6 });
        }

        // ── Approval note input ──────────────────────────────────────────
        var noteSection = new StackPanel();
        var noteLabel = CreateStyledTextBlock("Approval note (optional):", fontSize - 1, "SubtleText");
        noteLabel.Margin = new Thickness(0, 4, 0, 2);
        noteSection.Children.Add(noteLabel);

        var noteBox = new TextBox
        {
            MinHeight = 24,
            MaxHeight = 72,
            AcceptsReturn = false,
            TextWrapping = TextWrapping.Wrap,
            FontSize = fontSize - 1,
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(6, 3, 6, 3),
            BorderThickness = new Thickness(1),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        noteBox.SetResourceReference(TextBox.BackgroundProperty, "InputSurface");
        noteBox.SetResourceReference(TextBox.ForegroundProperty, "LabelText");
        noteBox.SetResourceReference(TextBox.BorderBrushProperty, "InputBorder");
        AutomationProperties.SetName(noteBox, "Approval note");

        // A read-only RichTextBox normally owns transcript selection. Handle the click at
        // the embedded editor so the parent cannot consume it before TextBox establishes a
        // caret. Voice input already focused this control; this restores the equivalent
        // mouse/keyboard path, including positioning the caret where the user clicked.
        var noteSelectionAnchor = 0;
        noteBox.PreviewMouseLeftButtonDown += (_, e) =>
        {
            FocusNoteEditorAtPoint(noteBox, e.GetPosition(noteBox));
            noteSelectionAnchor = noteBox.CaretIndex;
            noteBox.CaptureMouse();
            e.Handled = true;
        };
        noteBox.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || !noteBox.IsMouseCaptured) return;
            var index = noteBox.GetCharacterIndexFromPoint(e.GetPosition(noteBox), snapToText: true);
            if (index < 0) index = noteBox.Text.Length;
            noteBox.SelectionStart = Math.Min(noteSelectionAnchor, index);
            noteBox.SelectionLength = Math.Abs(index - noteSelectionAnchor);
            e.Handled = true;
        };
        noteBox.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!noteBox.IsMouseCaptured) return;
            noteBox.ReleaseMouseCapture();
            e.Handled = true;
        };

        // Watermark overlay for the note box
        var watermark = CreateStyledTextBlock("Add a note about why you're approving…", fontSize - 1, "SubtleText");
        watermark.IsHitTestVisible = false;
        watermark.Margin = new Thickness(7, 4, 0, 0);
        watermark.Opacity = 0.7;
        noteBox.TextChanged += (_, _) =>
            watermark.Visibility = string.IsNullOrEmpty(noteBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        noteBox.GotFocus += (_, _) =>
            watermark.Visibility = Visibility.Collapsed;
        noteBox.LostFocus += (_, _) =>
            watermark.Visibility = string.IsNullOrEmpty(noteBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        var noteContainer = new Grid();
        noteContainer.Children.Add(noteBox);
        noteContainer.Children.Add(watermark);
        noteSection.Children.Add(noteContainer);
        stack.Children.Add(noteSection);

        var resolutionNote = CreateStyledTextBlock(string.Empty, fontSize - 1, "BodyText");
        resolutionNote.Margin = new Thickness(0, 4, 0, 8);
        resolutionNote.Visibility = Visibility.Collapsed;
        stack.Children.Add(resolutionNote);

        // ── Approve button ───────────────────────────────────────────────
        var actionsPanel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 2),
        };

        var approveLabel = ApprovalCardNotificationCoordinator.BuildApproveLabel(activeGateCount);

        var approveButton = TranscriptQuickReplyFactory.CreateButton(
            approveLabel,
            fontSize,
            toolTip: activeGateCount > 1
                ? $"Approve all {activeGateCount} pending checkpoints and resume plan execution"
                : "Approve this checkpoint and resume plan execution");
        AutomationProperties.SetName(approveButton, approveLabel);

        // Shared approve action used by button click and keyboard shortcut
        void DoApprove()
        {
            if (!approveButton.IsEnabled) return;
            var note = string.IsNullOrWhiteSpace(noteBox.Text) ? null : noteBox.Text.Trim();
            approveButton.IsEnabled = false;
            onApprove(note);
        }

        approveButton.Click += (_, _) => DoApprove();

        // Enter in the note box triggers approval for one-click keyboard flow
        noteBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                DoApprove();
            }
        };

        actionsPanel.Children.Add(approveButton);
        Button? requestChangesButton = null;
        if (onRequestChanges is not null)
        {
            requestChangesButton = TranscriptQuickReplyFactory.CreateButton(
                "Request changes…",
                fontSize,
                toolTip: "Describe revisions in the normal prompt box; the plan remains paused until SquadDash receives a valid rework decision");
            AutomationProperties.SetName(requestChangesButton, "Request changes");
            requestChangesButton.Click += (_, _) => onRequestChanges();
            actionsPanel.Children.Add(requestChangesButton);
        }
        stack.Children.Add(actionsPanel);

        var resolvedIndicator = CreateStyledTextBlock("✓ Approved.", fontSize, "PlanApprovalResolved");
        resolvedIndicator.FontWeight = FontWeights.Bold;
        resolvedIndicator.Margin = new Thickness(0, 2, 0, 2);
        resolvedIndicator.Visibility = Visibility.Collapsed;
        resolvedIndicator.ToolTip = ToolTipHelper.MakeThemedToolTip("Approved");
        AutomationProperties.SetName(resolvedIndicator, "Approved");
        stack.Children.Add(resolvedIndicator);

        var reworkIndicator = CreateStyledTextBlock(string.Empty, fontSize, "ImportantText");
        reworkIndicator.FontWeight = FontWeights.SemiBold;
        reworkIndicator.Margin = new Thickness(0, 2, 0, 2);
        reworkIndicator.Visibility = Visibility.Collapsed;
        stack.Children.Add(reworkIndicator);

        // ── Spinner overlay (hidden until update) ────────────────────────
        var spinnerOverlay = BuildSpinnerOverlay(fontSize);

        // ── Card border ──────────────────────────────────────────────────
        var innerGrid = new Grid();
        innerGrid.Children.Add(stack);
        innerGrid.Children.Add(spinnerOverlay);

        var border = new Border
        {
            Child = innerGrid,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 10, 14, 10),
            MaxWidth = fontSize * 54,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 4),
            Effect = new DropShadowEffect
            {
                BlurRadius = 6,
                ShadowDepth = 1,
                Opacity = 0.25,
                Color = Colors.Black,
            },
        };
        border.SetResourceReference(Border.BackgroundProperty, "CardSurface");
        border.SetResourceReference(Border.BorderBrushProperty, "SubtleBorder");
        AutomationProperties.SetName(border, $"Approval card for {snapshot.PlanTitle}");
        AutomationProperties.SetHelpText(border,
            $"{snapshot.CompletedTaskCount} of {snapshot.TotalTaskCount} tasks complete. Gate: {snapshot.GateReason}");

        var tag = new TranscriptApprovalCardTag(
            snapshot.PlanId, gate.GateId, requestVersion);
        var container = new BlockUIContainer(border)
        {
            Margin = new Thickness(0, 4, 0, 8),
            Tag = tag,
        };

        return new CardResult
        {
            Container = container,
            ApproveButton = approveButton,
            RequestChangesButton = requestChangesButton,
            NoteTextBox = noteBox,
            NoteSection = noteSection,
            ResolutionNote = resolutionNote,
            SpinnerOverlay = spinnerOverlay,
            ActionsPanel = actionsPanel,
            ResolvedIndicator = resolvedIndicator,
            ReworkIndicator = reworkIndicator,
            ContentStack = stack,
            PlanLink = planTitleLink,
            CommitLinks = commitLinks,
        };
    }

    internal static void FocusNoteEditorAtPoint(TextBox noteBox, Point point)
    {
        ArgumentNullException.ThrowIfNull(noteBox);
        noteBox.Focus();
        var index = noteBox.GetCharacterIndexFromPoint(point, snapToText: true);
        noteBox.CaretIndex = index < 0 ? noteBox.Text.Length : index;
    }

    /// <summary>Shows the updating overlay and disables all actions.</summary>
    internal static void ShowUpdatingState(CardResult card)
    {
        card.ApproveButton.IsEnabled = false;
        if (card.RequestChangesButton is not null)
            card.RequestChangesButton.IsEnabled = false;
        card.NoteTextBox.IsEnabled = false;
        card.SpinnerOverlay.Visibility = Visibility.Visible;
    }

    /// <summary>Hides the updating overlay and re-enables actions.</summary>
    internal static void HideUpdatingState(CardResult card)
    {
        card.SpinnerOverlay.Visibility = Visibility.Collapsed;
        card.ApproveButton.IsEnabled = true;
        if (card.RequestChangesButton is not null)
            card.RequestChangesButton.IsEnabled = true;
        card.NoteTextBox.IsEnabled = true;
    }

    /// <summary>Leaves a historical card visible while making its resolved state unambiguous.</summary>
    internal static void ShowResolvedState(CardResult card)
    {
        card.SpinnerOverlay.Visibility = Visibility.Collapsed;
        var note = card.NoteTextBox.Text.Trim();
        card.NoteSection.Visibility = Visibility.Collapsed;
        if (note.Length > 0)
        {
            card.ResolutionNote.Text = $"Approval note: {note}";
            card.ResolutionNote.Visibility = Visibility.Visible;
        }
        else
        {
            card.ResolutionNote.Visibility = Visibility.Collapsed;
        }

        // Do not restyle a Button into an indicator: QuickReplyButtonStyle owns a control
        // template whose chrome can remain visible despite local border/background values.
        // Remove the action UI and replace it with an actual text glyph.
        card.ActionsPanel.Visibility = Visibility.Collapsed;
        card.ResolvedIndicator.Visibility = Visibility.Visible;
    }

    /// <summary>Keeps approval available while the normal transcript asks what should change.</summary>
    internal static void ShowChangeRequestDraftingState(CardResult card)
    {
        if (card.RequestChangesButton is not null)
            card.RequestChangesButton.IsEnabled = false;
        card.ReworkIndicator.Text = "Describe the requested changes in the prompt box.";
        card.ReworkIndicator.Visibility = Visibility.Visible;
    }

    /// <summary>Restores the approval actions when the next prompt was unrelated to this review.</summary>
    internal static void ClearChangeRequestDraftingState(CardResult card)
    {
        if (card.RequestChangesButton is not null)
            card.RequestChangesButton.IsEnabled = true;
        card.ReworkIndicator.Visibility = Visibility.Collapsed;
    }

    /// <summary>Turns the card into durable history after the host accepts a rework request.</summary>
    internal static void ShowReworkRequestedState(CardResult card)
    {
        card.SpinnerOverlay.Visibility = Visibility.Collapsed;
        card.NoteSection.Visibility = Visibility.Collapsed;
        card.ActionsPanel.Visibility = Visibility.Collapsed;
        card.ResolvedIndicator.Visibility = Visibility.Collapsed;
        card.ReworkIndicator.Text = "↩ Changes requested. Reworking…";
        card.ReworkIndicator.Visibility = Visibility.Visible;
    }

    // ── Private helpers ──────────────────────────────────────────────────

    /// <summary>Creates a themed <see cref="TextBlock"/> with the given text, size, and foreground resource key.</summary>
    private static TextBlock CreateStyledTextBlock(string text, double fontSize, string foregroundKey)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            TextWrapping = TextWrapping.Wrap,
        };
        tb.SetResourceReference(TextBlock.ForegroundProperty, foregroundKey);
        return tb;
    }

    /// <summary>
    /// Builds a themed <see cref="Expander"/> listing changed files with status indicators and diff stats.
    /// Displays up to 50 files; remaining entries are summarised with a "… and N more" line.
    /// </summary>
    private static Expander BuildChangedFilesExpander(
        IReadOnlyList<ChangedFileEntry> files,
        double fontSize)
    {
        var contentPanel = new StackPanel { Margin = new Thickness(8, 4, 0, 4) };

        foreach (var file in files.Take(50))
        {
            var line = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 1, 0, 1),
            };

            var statusChar = file.Status switch
            {
                FileChangeStatus.Added => "A",
                FileChangeStatus.Modified => "M",
                FileChangeStatus.Deleted => "D",
                FileChangeStatus.Renamed => "R",
                FileChangeStatus.Copied => "C",
                _ => "?",
            };
            var statusColor = file.Status switch
            {
                FileChangeStatus.Added => "ToolSuccessIcon",
                FileChangeStatus.Deleted => "ToolFailureIcon",
                _ => "SubtleText",
            };

            var statusBlock = CreateStyledTextBlock(statusChar, fontSize - 2, statusColor);
            statusBlock.FontFamily = new FontFamily("Consolas");
            statusBlock.Width = fontSize;
            line.Children.Add(statusBlock);

            var pathBlock = CreateStyledTextBlock(file.FilePath, fontSize - 2, "BodyText");
            pathBlock.FontFamily = new FontFamily("Consolas");
            pathBlock.TextTrimming = TextTrimming.CharacterEllipsis;
            pathBlock.MaxWidth = fontSize * 36;
            line.Children.Add(pathBlock);

            if (file.Insertions > 0 || file.Deletions > 0)
            {
                var stats = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(8, 0, 0, 0),
                };
                if (file.Insertions > 0)
                {
                    var insBlock = CreateStyledTextBlock($"+{file.Insertions}", fontSize - 2, "ToolSuccessIcon");
                    stats.Children.Add(insBlock);
                }
                if (file.Deletions > 0)
                {
                    var delBlock = CreateStyledTextBlock($" −{file.Deletions}", fontSize - 2, "ToolFailureIcon");
                    stats.Children.Add(delBlock);
                }
                line.Children.Add(stats);
            }

            contentPanel.Children.Add(line);
        }

        if (files.Count > 50)
        {
            var moreBlock = CreateStyledTextBlock(
                $"… and {files.Count - 50} more files",
                fontSize - 2, "SubtleText");
            moreBlock.Margin = new Thickness(0, 4, 0, 0);
            contentPanel.Children.Add(moreBlock);
        }

        var headerPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
        };
        var headerText = CreateStyledTextBlock($"📁 {files.Count} changed file(s)", fontSize - 1, "LabelText");
        headerPanel.Children.Add(headerText);

        var expander = new Expander
        {
            Header = headerPanel,
            Content = contentPanel,
            IsExpanded = false,
            Margin = new Thickness(0, 2, 0, 4),
        };
        expander.SetResourceReference(Expander.ForegroundProperty, "LabelText");
        AutomationProperties.SetName(expander, $"{files.Count} changed files");
        if (Application.Current?.TryFindResource("ThemedExpanderStyle") is Style expanderStyle)
            expander.Style = expanderStyle;

        return expander;
    }

    /// <summary>
    /// Builds the semi-transparent spinner overlay displayed over the card during async approval processing.
    /// Initially <see cref="Visibility.Collapsed"/>; made visible by <see cref="ShowUpdatingState"/>.
    /// </summary>
    private static Border BuildSpinnerOverlay(double fontSize)
    {
        var spinnerPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var dots = new TextBlock
        {
            Text = "⟳",
            FontSize = fontSize + 2,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(),
        };
        dots.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        spinnerPanel.Children.Add(dots);

        var label = new TextBlock
        {
            Text = "Updating approval request…",
            FontSize = fontSize,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        spinnerPanel.Children.Add(label);

        var overlay = new Border
        {
            Child = spinnerPanel,
            Visibility = Visibility.Collapsed,
            CornerRadius = new CornerRadius(8),
            MinHeight = fontSize * 5,
        };
        overlay.SetResourceReference(Border.BackgroundProperty, "CardSurface");
        AutomationProperties.SetName(overlay, "Updating approval request");
        if (dots.RenderTransform is RotateTransform rotation)
        {
            rotation.BeginAnimation(
                RotateTransform.AngleProperty,
                new System.Windows.Media.Animation.DoubleAnimation(
                    0, 360, TimeSpan.FromMilliseconds(850))
                {
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                });
        }
        return overlay;
    }
}
