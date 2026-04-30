namespace SquadDash;

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

internal sealed class CommitApprovalWindow : Window {
    private readonly Action<string>                               _navigateUrl;
    private readonly Action<DateTimeOffset>                       _scrollToTurn;
    private readonly Action<CommitApprovalItem>                   _onItemChanged;
    private readonly Action<IReadOnlyList<CommitApprovalItem>>    _onItemsRemoved;

    private readonly StackPanel _needsApprovalPanel;
    private readonly StackPanel _approvedPanel;

    public CommitApprovalWindow(
        Action<string>                            navigateUrl,
        Action<DateTimeOffset>                    scrollToTurn,
        Action<CommitApprovalItem>                onItemChanged,
        Action<IReadOnlyList<CommitApprovalItem>> onItemsRemoved) {
        _navigateUrl    = navigateUrl;
        _scrollToTurn   = scrollToTurn;
        _onItemChanged  = onItemChanged;
        _onItemsRemoved = onItemsRemoved;

        Title          = "Commit Approvals";
        Width          = 560;
        Height         = 480;
        MinWidth       = 420;
        MinHeight      = 300;
        WindowStyle    = WindowStyle.None;
        AllowsTransparency = true;
        Background     = Brushes.Transparent;
        ResizeMode     = ResizeMode.CanResizeWithGrip;
        ShowInTaskbar  = false;
        ShowActivated  = false;
        Topmost        = false;

        WindowChrome.SetWindowChrome(this, new WindowChrome {
            CaptionHeight         = 36,
            ResizeBorderThickness = new Thickness(4),
            GlassFrameThickness   = new Thickness(0),
            UseAeroCaptionButtons = false,
        });

        SourceInitialized += (_, _) =>
            NativeMethods.DisableRoundedCorners(new WindowInteropHelper(this).Handle);

        var outerBorder = new Border {
            BorderThickness = new Thickness(1.5),
            CornerRadius    = new CornerRadius(4),
        };
        outerBorder.SetResourceReference(Border.BackgroundProperty,   "AppSurface");
        outerBorder.SetResourceReference(Border.BorderBrushProperty,  "PanelBorder");

        // Outer layout: title bar row + content area row
        var outerGrid = new Grid();
        outerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        outerBorder.Child = outerGrid;

        // ── Title bar ────────────────────────────────────────────────────────
        var titleBar = new DockPanel {
            LastChildFill = false,
            Background    = Brushes.Transparent,
            Height        = 36,
        };
        Grid.SetRow(titleBar, 0);
        outerGrid.Children.Add(titleBar);

        var closeButton = new Button { Width = 30, Height = 30 };
        closeButton.SetResourceReference(Control.StyleProperty, "PanelCloseButtonStyle");
        WindowChrome.SetIsHitTestVisibleInChrome(closeButton, true);
        closeButton.Click += (_, _) => Close();
        DockPanel.SetDock(closeButton, Dock.Right);
        titleBar.Children.Add(closeButton);

        var titleBlock = new TextBlock {
            Text                = "Commit Approvals",
            FontSize            = 16,
            FontWeight          = FontWeights.SemiBold,
            VerticalAlignment   = VerticalAlignment.Center,
            Margin              = new Thickness(12, 0, 0, 0),
        };
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "ImportantText");
        titleBar.Children.Add(titleBlock);

        // ── Content area (wrapped in Border with Margin=12) ──────────────────
        var contentBorder = new Border { Margin = new Thickness(12) };
        Grid.SetRow(contentBorder, 1);
        outerGrid.Children.Add(contentBorder);

        var sectionGrid = new Grid();
        sectionGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        sectionGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        sectionGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        sectionGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        contentBorder.Child = sectionGrid;

        // Row 0: "Needs Approval" header
        var needsApprovalHeader = new TextBlock {
            Text   = "Needs Approval",
            Margin = new Thickness(8, 4, 8, 4),
        };
        needsApprovalHeader.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        Grid.SetRow(needsApprovalHeader, 0);
        sectionGrid.Children.Add(needsApprovalHeader);

        // Row 1: Needs Approval scroll view
        _needsApprovalPanel = new StackPanel();
        var needsApprovalScroll = new ScrollViewer {
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content                       = _needsApprovalPanel,
        };
        Grid.SetRow(needsApprovalScroll, 1);
        sectionGrid.Children.Add(needsApprovalScroll);

        // Row 2: "Approved" section DockPanel header
        var approvedHeaderPanel = new DockPanel {
            LastChildFill = false,
            Margin        = new Thickness(0, 8, 0, 0),
        };
        Grid.SetRow(approvedHeaderPanel, 2);
        sectionGrid.Children.Add(approvedHeaderPanel);

        var clearAllButton = new Button {
            Content = "Clear All",
            Height  = 24,
            Margin  = new Thickness(0, 0, 8, 0),
        };
        clearAllButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        WindowChrome.SetIsHitTestVisibleInChrome(clearAllButton, true);
        clearAllButton.Click += OnClearAllClicked;
        DockPanel.SetDock(clearAllButton, Dock.Right);
        approvedHeaderPanel.Children.Add(clearAllButton);

        var approvedHeader = new TextBlock {
            Text              = "Approved",
            Margin            = new Thickness(8, 4, 8, 4),
            VerticalAlignment = VerticalAlignment.Center,
        };
        approvedHeader.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        DockPanel.SetDock(approvedHeader, Dock.Left);
        approvedHeaderPanel.Children.Add(approvedHeader);

        // Row 3: Approved scroll view
        _approvedPanel = new StackPanel();
        var approvedScroll = new ScrollViewer {
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content                       = _approvedPanel,
        };
        Grid.SetRow(approvedScroll, 3);
        sectionGrid.Children.Add(approvedScroll);

        Content = outerBorder;
    }

    // ── Public API ───────────────────────────────────────────────────────────

    public void AddItem(CommitApprovalItem item) {
        _needsApprovalPanel.Children.Add(BuildRow(item));
    }

    public void ReplaceAllItems(IReadOnlyList<CommitApprovalItem> items) {
        _needsApprovalPanel.Children.Clear();
        _approvedPanel.Children.Clear();
        foreach (var item in items) {
            var target = item.IsApproved ? _approvedPanel : _needsApprovalPanel;
            target.Children.Add(BuildRow(item));
        }
    }

    // ── Row construction ─────────────────────────────────────────────────────

    private Border BuildRow(CommitApprovalItem item) {
        var row = new Border { Background = Brushes.Transparent, Tag = item };
        row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "HoverSurface");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;

        var grid = new Grid { Margin = new Thickness(4, 2, 4, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var checkBox = new CheckBox {
            IsChecked         = item.IsApproved,
            VerticalAlignment = VerticalAlignment.Center,
        };
        WindowChrome.SetIsHitTestVisibleInChrome(checkBox, true);
        Grid.SetColumn(checkBox, 0);
        grid.Children.Add(checkBox);

        var descBlock = new TextBlock {
            Text              = item.Description,
            TextTrimming      = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(6, 0, 0, 0),
            Cursor            = Cursors.Hand,
        };
        descBlock.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        WindowChrome.SetIsHitTestVisibleInChrome(descBlock, true);
        descBlock.MouseLeftButtonUp += (_, e) => { e.Handled = true; _scrollToTurn(item.TurnStartedAt); };
        Grid.SetColumn(descBlock, 1);
        grid.Children.Add(descBlock);

        if (item.CommitUrl is not null) {
            var shaDisplay = item.CommitSha.Length >= 7 ? item.CommitSha[..7] : item.CommitSha;
            var shaBlock = new TextBlock {
                Cursor            = Cursors.Hand,
                Margin            = new Thickness(4, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var shaRun = new Run(shaDisplay) { TextDecorations = TextDecorations.Underline };
            shaRun.SetResourceReference(Run.ForegroundProperty, "SubtleText");
            shaBlock.Inlines.Add(shaRun);
            WindowChrome.SetIsHitTestVisibleInChrome(shaBlock, true);
            shaBlock.MouseLeftButtonUp += (_, e) => { e.Handled = true; _navigateUrl(item.CommitUrl); };
            Grid.SetColumn(shaBlock, 2);
            grid.Children.Add(shaBlock);
        }

        // Wire checkbox after IsChecked is set so construction doesn't fire handlers
        checkBox.Checked   += (_, _) => HandleCheckChanged(row, item, isApproved: true);
        checkBox.Unchecked += (_, _) => HandleCheckChanged(row, item, isApproved: false);

        row.Child = grid;
        return row;
    }

    private void HandleCheckChanged(Border row, CommitApprovalItem item, bool isApproved) {
        var updated     = item with { IsApproved = isApproved };
        var sourcePanel = isApproved ? _needsApprovalPanel : _approvedPanel;
        var targetPanel = isApproved ? _approvedPanel      : _needsApprovalPanel;

        sourcePanel.Children.Remove(row);
        targetPanel.Children.Add(BuildRow(updated));

        _onItemChanged(updated);
    }

    private void OnClearAllClicked(object sender, RoutedEventArgs e) {
        var removed = new List<CommitApprovalItem>(_approvedPanel.Children.Count);
        foreach (Border row in _approvedPanel.Children) {
            if (row.Tag is CommitApprovalItem item)
                removed.Add(item);
        }
        _approvedPanel.Children.Clear();
        if (removed.Count > 0)
            _onItemsRemoved(removed);
    }
}
