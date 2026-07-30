using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
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
        internal BlockUIContainer Container { get; init; } = null!;
        internal Button ApproveButton { get; init; } = null!;
        internal TextBox NoteTextBox { get; init; } = null!;
        internal Border SpinnerOverlay { get; init; } = null!;
        internal WrapPanel ActionsPanel { get; init; } = null!;
        internal StackPanel ContentStack { get; init; } = null!;
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
        Action<string?> onApprove)
    {
        var activeGateCount = plan.ApprovalGates
            .Count(g => g.Status == PlanGateStatus.AwaitingApproval);

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
        var progressText = $"{snapshot.PlanTitle} — {snapshot.CompletedTaskCount}/{snapshot.TotalTaskCount} tasks complete";
        if (snapshot.CurrentStage is not null)
            progressText += $" (stage: {snapshot.CurrentStage})";
        var progressBlock = CreateStyledTextBlock(progressText, fontSize, "LabelText");
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

                    var shaBlock = CreateStyledTextBlock(commit.Link.ShortSha, fontSize - 2, "SubtleText");
                    shaBlock.FontFamily = new FontFamily("Consolas");
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
        var noteLabel = CreateStyledTextBlock("Approval note (optional):", fontSize - 1, "SubtleText");
        noteLabel.Margin = new Thickness(0, 4, 0, 2);
        stack.Children.Add(noteLabel);

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
        stack.Children.Add(noteBox);

        // ── Approve button ───────────────────────────────────────────────
        var actionsPanel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 2),
        };

        var approveLabel = ApprovalCardNotificationCoordinator.BuildApproveLabel(activeGateCount);

        var approveButton = new Button
        {
            Content = approveLabel,
            FontSize = fontSize,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(0, 0, 8, 4),
            MinHeight = 32,
            Cursor = Cursors.Hand,
            BorderThickness = new Thickness(1),
        };
        if (Application.Current?.TryFindResource("QuickReplyButtonStyle") is Style qrStyle)
            approveButton.Style = qrStyle;
        approveButton.SetResourceReference(Control.BackgroundProperty, "ActivePanelSurface");
        approveButton.SetResourceReference(Control.ForegroundProperty, "QuickReplyText");
        approveButton.SetResourceReference(Control.BorderBrushProperty, "ActivePanelBorder");

        approveButton.Click += (_, _) =>
        {
            var note = string.IsNullOrWhiteSpace(noteBox.Text) ? null : noteBox.Text.Trim();
            approveButton.IsEnabled = false;
            onApprove(note);
        };

        actionsPanel.Children.Add(approveButton);
        stack.Children.Add(actionsPanel);

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

        var tag = new TranscriptApprovalCardTag(
            snapshot.PlanId, gate.GateId, plan.Progress.CompletedCount);
        var container = new BlockUIContainer(border)
        {
            Margin = new Thickness(0, 4, 0, 8),
            Tag = tag,
        };

        return new CardResult
        {
            Container = container,
            ApproveButton = approveButton,
            NoteTextBox = noteBox,
            SpinnerOverlay = spinnerOverlay,
            ActionsPanel = actionsPanel,
            ContentStack = stack,
        };
    }

    /// <summary>Shows the updating overlay and disables all actions.</summary>
    internal static void ShowUpdatingState(CardResult card)
    {
        card.ApproveButton.IsEnabled = false;
        card.NoteTextBox.IsEnabled = false;
        card.SpinnerOverlay.Visibility = Visibility.Visible;
    }

    /// <summary>Hides the updating overlay and re-enables actions.</summary>
    internal static void HideUpdatingState(CardResult card)
    {
        card.SpinnerOverlay.Visibility = Visibility.Collapsed;
        card.ApproveButton.IsEnabled = true;
        card.NoteTextBox.IsEnabled = true;
    }

    // ── Private helpers ──────────────────────────────────────────────────

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
        if (Application.Current?.TryFindResource("ThemedExpanderStyle") is Style expanderStyle)
            expander.Style = expanderStyle;

        return expander;
    }

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
        return overlay;
    }
}
