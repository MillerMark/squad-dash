namespace SquadDash;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Input;
using System.Windows.Threading;
using System.Threading.Tasks;

/// <summary>Manages content in the inline Tasks panel.</summary>
internal sealed class TasksPanelController {

    private readonly StackPanel      _activePanel;
    private readonly StackPanel      _completedPanel;
    private readonly UIElement       _completedSection;
    private readonly Func<string?>   _getTasksPath;
    private readonly Action          _reloadPanel;
    private readonly Action          _editTasksAction;
    private readonly Func<string, Brush> _priorityDotColor;
    private readonly Action<TaskItem>?  _attachFollowUp;
    private readonly Action<TaskItem>?  _addToNotes;
    private readonly Func<IReadOnlyList<SquadTeamMember>>? _getRoster;

    private bool      _showCompleted;
    private string    _filterText = string.Empty;
    private MenuItem? _toggleCompletedItem;
    private readonly List<MenuItem> _allToggleItems = [];
    private DateTime  _lastPopupShownTime = DateTime.MinValue;

    // ── Construction ─────────────────────────────────────────────────────────

    public TasksPanelController(
        StackPanel           activePanel,
        StackPanel           completedPanel,
        UIElement            completedSection,
        Border               outerBorder,
        Func<string?>        getTasksPath,
        Action               editTasksAction,
        Func<string, Brush>  priorityDotColor,
        Action               reloadPanel,
        Action<TaskItem>?    attachFollowUp = null,
        Action<TaskItem>?    addToNotes     = null,
        Func<IReadOnlyList<SquadTeamMember>>? getRoster = null) {

        _activePanel      = activePanel;
        _completedPanel   = completedPanel;
        _completedSection = completedSection;
        _getTasksPath     = getTasksPath;
        _editTasksAction  = editTasksAction;
        _priorityDotColor = priorityDotColor;
        _reloadPanel      = reloadPanel;
        _attachFollowUp   = attachFollowUp;
        _addToNotes       = addToNotes;
        _getRoster        = getRoster;

        AttachPanelContextMenu(outerBorder);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void Refresh(TaskParseResult result) {
        _activePanel.Children.Clear();
        _completedPanel.Children.Clear();
        _allToggleItems.Clear();

        var openGroups = result.OpenGroups;
        var hasOpen    = openGroups.Any(g => g.Items.Count > 0);

        if (!hasOpen) {
            ShowEmptyInPanel("No open tasks");
        } else {
            foreach (var group in openGroups) {
                if (group.Items.Count == 0) continue;

                // Priority heading: colored dot + label
                var headingRow = new StackPanel {
                    Orientation = Orientation.Horizontal,
                    Margin      = new Thickness(0, 8, 0, 3),
                };
                var dot = new Ellipse {
                    Width             = 9,
                    Height            = 9,
                    Fill              = _priorityDotColor(group.Emoji),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin            = new Thickness(0, 0, 5, 0),
                };
                var headingLabel = new TextBlock {
                    Text              = group.Label,
                    FontSize          = 11,
                    FontWeight        = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                headingLabel.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
                headingRow.Children.Add(dot);
                headingRow.Children.Add(headingLabel);
                _activePanel.Children.Add(headingRow);

                foreach (var item in group.Items)
                    _activePanel.Children.Add(BuildRow(item));
            }
        }

        foreach (var item in result.CompletedItems)
            _completedPanel.Children.Add(BuildDoneRow(item));

        ApplyFilter();
    }

    public void ShowEmpty(string message) => ShowEmptyInPanel(message);

    public void SetFilter(string text) {
        _filterText = text.Trim();
        ApplyFilter();
    }

    // ── Panel context menu────────────────────────────────────────────────────

    private static ContextMenu MakeMenu() {
        var m = new ContextMenu();
        m.SetResourceReference(ContextMenu.StyleProperty, "ThemedContextMenuStyle");
        return m;
    }

    private static MenuItem MakeItem(string header) {
        var i = new MenuItem { Header = header };
        i.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
        return i;
    }

    private static Separator MakeSep() {
        var s = new Separator();
        s.SetResourceReference(Separator.StyleProperty, "ThemedMenuSeparatorStyle");
        return s;
    }

    private void AttachPanelContextMenu(Border outerBorder) {
        var menu = MakeMenu();

        _toggleCompletedItem = MakeItem(string.Empty);
        UpdateToggleHeader();
        _toggleCompletedItem.Click += (_, _) => {
            _showCompleted = !_showCompleted;
            _completedSection.Visibility = _showCompleted ? Visibility.Visible : Visibility.Collapsed;
            UpdateToggleHeader();
        };
        menu.Items.Add(_toggleCompletedItem);

        menu.Items.Add(MakeSep());

        var editItem = MakeItem("Edit Tasks");
        editItem.Click += (_, _) => _editTasksAction();
        menu.Items.Add(editItem);

        outerBorder.ContextMenu = menu;
    }

    private void UpdateToggleHeader() {
        var label = _showCompleted ? "Hide Completed Tasks" : "Show Completed Tasks";
        if (_toggleCompletedItem is not null)
            _toggleCompletedItem.Header = label;
        foreach (var item in _allToggleItems)
            item.Header = label;
    }

    private MenuItem BuildToggleCompletedMenuItem() {
        var item = MakeItem(_showCompleted ? "Hide Completed Tasks" : "Show Completed Tasks");
        _allToggleItems.Add(item);
        item.Click += (_, _) => {
            _showCompleted = !_showCompleted;
            _completedSection.Visibility = _showCompleted ? Visibility.Visible : Visibility.Collapsed;
            UpdateToggleHeader();
        };
        return item;
    }

    // ── Row construction — open tasks ─────────────────────────────────────────

    private Border BuildRow(TaskItem item) {
        var row = new Border { Background = Brushes.Transparent, Tag = item };
        row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "HoverSurface");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;

        var menu           = MakeMenu();
        if (_attachFollowUp is not null || _addToNotes is not null)
        {
            if (_attachFollowUp is not null)
            {
                var followUpItem = MakeItem("Add to chat");
                followUpItem.Click += (_, _) => _attachFollowUp(item);
                menu.Items.Add(followUpItem);
            }
            if (_addToNotes is not null)
            {
                var notesItem = MakeItem("Add to Notes");
                notesItem.Click += (_, _) => _addToNotes(item);
                menu.Items.Add(notesItem);
            }
            menu.Items.Add(MakeSep());
        }
        var markCompleteItem = MakeItem("Mark as Complete");
        markCompleteItem.Click += (_, _) => _ = HandleMarkCompleteAsync(item, isDone: true);
        menu.Items.Add(markCompleteItem);
        menu.Items.Add(MakeSep());
        menu.Items.Add(BuildAssignToMenuItem(item));
        menu.Items.Add(MakeSep());
        menu.Items.Add(BuildToggleCompletedMenuItem());
        menu.Items.Add(MakeSep());
        var editTasksItem2 = MakeItem("Edit Tasks");
        editTasksItem2.Click += (_, _) => _editTasksAction();
        menu.Items.Add(editTasksItem2);
        row.ContextMenu = menu;

        var grid = new Grid { Margin = new Thickness(4, 3, 4, 3) };
        // Fixed-width column 0 (20px) so text in column 1 left-aligns identically
        // whether the row has a CheckBox or a circle dot.
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        if (item.IsUserOwned) {
            var checkBox = new CheckBox {
                IsChecked           = false,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Top,
                Margin              = new Thickness(0, 1, 0, 0),
            };
            Grid.SetColumn(checkBox, 0);
            grid.Children.Add(checkBox);

            // Wire after IsChecked is set so construction doesn't fire the handler
            checkBox.Checked += (_, _) => _ = HandleMarkCompleteAsync(item, isDone: true);
        } else {
            // Non-user-owned tasks: show a filled rounded-rect instead of a checkbox.
            // Centered within the same 20px column so text aligns with checkbox rows.
            var dot = new Border {
                Width               = 11,
                Height              = 11,
                CornerRadius        = new CornerRadius(3),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Top,
                Margin              = new Thickness(-1, 2, 1, 0),
            };
            dot.SetResourceReference(Border.BackgroundProperty, "LineColor");
            Grid.SetColumn(dot, 0);
            grid.Children.Add(dot);
        }

        var label = new TextBlock {
            Text         = item.Text,
            FontSize     = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth     = 220,
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        row.Child = grid;

        // Popup content is built lazily on first open so Refresh() stays cheap.
        {
            var contentStack = new StackPanel { Margin = new Thickness(10) };

            var titleBlock = new TextBlock {
                Text         = item.Text,
                FontSize     = 13,
                FontWeight   = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth     = 300,
            };
            titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
            contentStack.Children.Add(titleBlock);

            var popupBorder = new Border {
                Child           = contentStack,
                Padding         = new Thickness(0),
                BorderThickness = new Thickness(1),
                MinWidth        = 220,
                MaxWidth        = 340,
            };

            var popup = new Popup {
                Child              = popupBorder,
                PlacementTarget    = row,
                Placement          = PlacementMode.Left,
                StaysOpen          = true,
                AllowsTransparency = true,
            };

            bool brushesResolved = false;
            void ResolveBrushes() {
                if (brushesResolved) return;
                brushesResolved = true;
                popupBorder.Background  = (row.TryFindResource("PopupSurface")      as Brush)
                                       ?? new SolidColorBrush(Color.FromRgb(0x30, 0x2C, 0x28));
                popupBorder.BorderBrush = (row.TryFindResource("ActivePanelBorder") as Brush)
                                       ?? new SolidColorBrush(Color.FromRgb(0x55, 0x4E, 0x47));
            }

            // FlowDocument/viewer is deferred until the popup first opens.
            bool contentBuilt = false;
            void EnsureContentBuilt() {
                if (contentBuilt) return;
                contentBuilt = true;
                if (!string.IsNullOrEmpty(item.Description)) {
                    var doc = MarkdownFlowDocumentBuilder.Build(item.Description);
                    doc.TextAlignment = TextAlignment.Left;
                    var viewer = new FlowDocumentScrollViewer {
                        Document                    = doc,
                        MaxWidth                    = 320,
                        MaxHeight                   = 220,
                        Margin                      = new Thickness(0, 6, 0, 0),
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    };
                    contentStack.Children.Add(viewer);
                } else {
                    var noDesc = new TextBlock {
                        Text      = "No description",
                        FontSize  = 11,
                        FontStyle = FontStyles.Italic,
                        Margin    = new Thickness(0, 4, 0, 0),
                        Opacity   = 0.6,
                    };
                    noDesc.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
                    contentStack.Children.Add(noDesc);
                }
            }

            bool isFading = false;
            bool isMouseOverPopup = false;
            DispatcherTimer? fadeDelayTimer = null;
            DispatcherTimer? openTimer = null;

            void CancelPendingFade() {
                fadeDelayTimer?.Stop();
                fadeDelayTimer = null;
            }

            void CancelFade() {
                CancelPendingFade();
                if (!isFading) return;
                popupBorder.BeginAnimation(UIElement.OpacityProperty, null);
                popupBorder.Opacity = 1.0;
                isFading = false;
            }

            void BeginFadeOut() {
                if (!popup.IsOpen || isFading || isMouseOverPopup) return;
                isFading = true;
                var anim = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(350)) {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                };
                anim.Completed += (_, _) => {
                    popup.IsOpen = false;
                    popupBorder.BeginAnimation(UIElement.OpacityProperty, null);
                    popupBorder.Opacity = 1.0;
                    isFading = false;
                };
                popupBorder.BeginAnimation(UIElement.OpacityProperty, anim);
            }

            // Deferred fade: waits delayMs before actually fading so the mouse has time to
            // travel from the row into the popup.
            void ScheduleFadeOut(int delayMs = 500) {
                CancelPendingFade();
                if (!popup.IsOpen || isMouseOverPopup) return;
                fadeDelayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
                fadeDelayTimer.Tick += (_, _) => {
                    fadeDelayTimer!.Stop();
                    fadeDelayTimer = null;
                    BeginFadeOut();
                };
                fadeDelayTimer.Start();
            }

            void OpenPopup() {
                if (popup.IsOpen || isFading) return;
                ResolveBrushes();
                EnsureContentBuilt();
                popupBorder.BeginAnimation(UIElement.OpacityProperty, null);
                popupBorder.Opacity = 1.0;
                popup.IsOpen = true;
                _lastPopupShownTime = DateTime.Now;
            }

            // instant=true skips the 400 ms hover-dwell delay when the user is sliding
            // between tasks and a popup was recently visible.
            void StartOpenTimer(bool instant = false) {
                openTimer?.Stop();
                openTimer = null;
                if (instant) {
                    OpenPopup();
                } else {
                    openTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                    openTimer.Tick += (_, _) => {
                        openTimer!.Stop();
                        openTimer = null;
                        OpenPopup();
                    };
                    openTimer.Start();
                }
            }

            popupBorder.MouseEnter += (_, _) => {
                isMouseOverPopup = true;
                CancelFade();
            };
            popupBorder.MouseLeave += (_, _) => {
                isMouseOverPopup = false;
                BeginFadeOut();
            };

            row.MouseEnter += (_, _) => {
                // Cancel any in-progress fade so the popup snaps back to full opacity
                // if the mouse returns while it was fading away.
                CancelFade();
                if (!popup.IsOpen) {
                    bool instant = (DateTime.Now - _lastPopupShownTime).TotalMilliseconds < 1000;
                    StartOpenTimer(instant);
                }
            };

            row.MouseLeave += (_, _) => {
                openTimer?.Stop();
                openTimer = null;
                ScheduleFadeOut(500);
            };

            row.PreviewMouseDown += (_, _) => BeginFadeOut();

            // Stop all timers and close the popup when the row is removed from the panel
            // (e.g. on Refresh()), preventing timer accumulation.
            row.Unloaded += (_, _) => {
                openTimer?.Stop();
                openTimer = null;
                fadeDelayTimer?.Stop();
                fadeDelayTimer = null;
                if (popup.IsOpen) {
                    popupBorder.BeginAnimation(UIElement.OpacityProperty, null);
                    popup.IsOpen = false;
                }
            };
        }

        return row;
    }

    // ── Row construction — done tasks ─────────────────────────────────────────

    private Border BuildDoneRow(TaskItem item) {
        var row = new Border { Background = Brushes.Transparent, Tag = item };
        row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "HoverSurface");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;

        var menu             = MakeMenu();
        if (_attachFollowUp is not null || _addToNotes is not null)
        {
            if (_attachFollowUp is not null)
            {
                var followUpItem = MakeItem("Add to chat");
                followUpItem.Click += (_, _) => _attachFollowUp(item);
                menu.Items.Add(followUpItem);
            }
            if (_addToNotes is not null)
            {
                var notesItem = MakeItem("Add to Notes");
                notesItem.Click += (_, _) => _addToNotes(item);
                menu.Items.Add(notesItem);
            }
            menu.Items.Add(MakeSep());
        }
        var markIncompleteItem = MakeItem("Mark as Incomplete");
        markIncompleteItem.Click += (_, _) => _ = HandleMarkCompleteAsync(item, isDone: false);
        menu.Items.Add(markIncompleteItem);
        menu.Items.Add(MakeSep());
        menu.Items.Add(BuildToggleCompletedMenuItem());
        menu.Items.Add(MakeSep());
        var editTasksItem3 = MakeItem("Edit Tasks");
        editTasksItem3.Click += (_, _) => _editTasksAction();
        menu.Items.Add(editTasksItem3);
        row.ContextMenu = menu;

        var label = new TextBlock {
            Text         = item.Text,
            FontSize     = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth     = 220,
            Opacity      = 0.6,
            TextDecorations = TextDecorations.Strikethrough,
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");

        var wrapper = new Grid { Margin = new Thickness(4, 3, 4, 3) };
        wrapper.Children.Add(label);
        row.Child = wrapper;
        return row;
    }

    // ── Filter ────────────────────────────────────────────────────────────────

    private void ApplyFilter() {
        ApplyFilterToPanel(_activePanel, syncHeadings: true);
        ApplyFilterToPanel(_completedPanel, syncHeadings: false);
    }

    private void ApplyFilterToPanel(StackPanel panel, bool syncHeadings) {
        var (ownerCandidates, textFilter) = ParseFilter(_filterText);

        // Pass 1: show/hide item rows.
        foreach (UIElement child in panel.Children) {
            if (child is System.Windows.Controls.Border { Tag: TaskItem item }) {
                bool visible = true;

                // Owner filter (from @handle or @me).
                if (ownerCandidates is not null) {
                    visible = ownerCandidates.Any(c =>
                        c == TasksPanelFilter.UserOwnedSentinel
                            ? item.IsUserOwned
                            : string.Equals(item.Owner?.Trim(), c, StringComparison.OrdinalIgnoreCase));
                }

                // Text filter applied on top — both conditions must hold.
                if (visible && !string.IsNullOrEmpty(textFilter))
                    visible = PanelFilterHelper.Matches(item.Text, textFilter);

                child.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        if (!syncHeadings) return;

        // Pass 2: hide priority headings whose items are all filtered out.
        StackPanel? currentHeading = null;
        bool headingHasVisible = false;

        foreach (UIElement child in panel.Children) {
            if (child is StackPanel heading) {
                if (currentHeading is not null)
                    currentHeading.Visibility = headingHasVisible ? Visibility.Visible : Visibility.Collapsed;
                currentHeading = heading;
                headingHasVisible = false;
            } else if (child is System.Windows.Controls.Border { Tag: TaskItem }) {
                if (child.Visibility == Visibility.Visible)
                    headingHasVisible = true;
            }
        }
        if (currentHeading is not null)
            currentHeading.Visibility = headingHasVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private (IReadOnlyList<string>? ownerCandidates, string textFilter) ParseFilter(string filter)
        => TasksPanelFilter.Parse(filter, _getRoster?.Invoke());


    // ── Empty state ───────────────────────────────────────────────────────────
    private void ShowEmptyInPanel(string message) {
        _activePanel.Children.Clear();
        var empty = new TextBlock {
            Text       = message,
            FontSize   = 12,
            Margin     = new Thickness(0, 4, 0, 0),
            Opacity    = 0.6,
        };
        empty.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        _activePanel.Children.Add(empty);
    }

    // ── Write-back ────────────────────────────────────────────────────────────

    private async Task HandleMarkCompleteAsync(TaskItem item, bool isDone) {
        var path = _getTasksPath();
        if (path is null || !File.Exists(path)) return;

        var lines = await Task.Run(() => File.ReadAllLines(path));
        bool wrote = false;
        for (int i = 0; i < lines.Length; i++) {
            if (lines[i].TrimEnd() == item.RawLine) {
                if (isDone)
                    lines[i] = lines[i].Replace("- [ ]", "- [x]", StringComparison.Ordinal);
                else
                    lines[i] = lines[i].Replace("- [x]", "- [ ]", StringComparison.Ordinal);
                await Task.Run(() => File.WriteAllLines(path, lines));
                wrote = true;
                break;
            }
        }

        if (wrote)
            _reloadPanel();
    }

    private MenuItem BuildAssignToMenuItem(TaskItem item) {
        var assignItem = MakeItem("Assign to");
        // A MenuItem (not ContextMenu) placeholder — WPF needs at least one item to render the
        // submenu arrow. It is replaced with real items when the submenu opens.
        assignItem.Items.Add(MakeItem(string.Empty));

        assignItem.SubmenuOpened += (_, _) => {
            assignItem.Items.Clear();

            // "Me" always first
            var meItem = MakeItem("Me");
            meItem.Click += (_, _) => _ = HandleAssignOwnerAsync(item, "you");
            if (string.Equals(item.Owner, "you", StringComparison.OrdinalIgnoreCase) ||
                item.IsUserOwned)
                meItem.IsChecked = true;
            assignItem.Items.Add(meItem);

            // Roster members (non-utility, non-retired)
            var roster = _getRoster?.Invoke() ?? [];
            var candidates = roster
                .Where(m => !m.IsUtilityAgent &&
                            !string.Equals(m.Status, "Retired", StringComparison.OrdinalIgnoreCase))
                .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (candidates.Count > 0) {
                assignItem.Items.Add(MakeSep());
                foreach (var member in candidates) {
                    var agentItem = MakeItem(member.Name);
                    agentItem.Click += (_, _) => _ = HandleAssignOwnerAsync(item, member.Name);
                    if (item.Owner is not null &&
                        string.Equals(item.Owner.Trim(), member.Name, StringComparison.OrdinalIgnoreCase))
                        agentItem.IsChecked = true;
                    assignItem.Items.Add(agentItem);
                }
            }

            // "Remove owner" only if task currently has an owner
            if (!string.IsNullOrWhiteSpace(item.Owner)) {
                assignItem.Items.Add(MakeSep());
                var removeItem = MakeItem("Remove owner");
                removeItem.Click += (_, _) => _ = HandleAssignOwnerAsync(item, null);
                assignItem.Items.Add(removeItem);
            }
        };

        return assignItem;
    }

    private async Task HandleAssignOwnerAsync(TaskItem item, string? ownerName) {
        var path = _getTasksPath();
        if (path is null || !File.Exists(path)) return;

        var lines = await Task.Run(() => File.ReadAllLines(path));
        bool wrote = false;
        for (int i = 0; i < lines.Length; i++) {
            if (lines[i].TrimEnd() != item.RawLine) continue;

            // Strip any existing *(Owner: ...)* suffix
            const string marker = " *(Owner:";
            var line = lines[i].TrimEnd();
            var ownerIdx = line.IndexOf(marker, StringComparison.Ordinal);
            if (ownerIdx >= 0) {
                // Find closing ')' after the marker
                var after = line[(ownerIdx + marker.Length)..];
                var closeIdx = after.IndexOf(')', StringComparison.Ordinal);
                if (closeIdx >= 0)
                    line = line[..ownerIdx];
                else
                    line = line[..ownerIdx];
            }

            if (!string.IsNullOrWhiteSpace(ownerName))
                line = $"{line} *(Owner: {ownerName})*";

            lines[i] = line;
            await Task.Run(() => File.WriteAllLines(path, lines));
            wrote = true;
            break;
        }

        if (wrote)
            _reloadPanel();
    }
}
