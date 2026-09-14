using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace SquadDash;

internal sealed class CommitViewerWindow : ChromedWindow
{
    private readonly string _repositoryPath;
    private readonly IReadOnlyList<CommitViewerEntry> _commits;
    private string _commitSha;
    private readonly string? _baselineSha;
    private readonly string? _initialFilePath;
    private readonly ListBox _commitList = new();
    private readonly ListBox _fileList = new();
    private readonly StackPanel _diffPanel = new();
    private readonly TextBlock _heading = new();
    private readonly TextBlock _status = new();
    private readonly CommitViewerCodeFontStore _codeFontStore = new();
    private readonly double _codeHeaderFontRatio;
    private double _codeFontSize;

    private CommitViewerWindow(
        string repositoryPath,
        IReadOnlyList<CommitViewerEntry> commits,
        string? baselineSha = null,
        string? initialFilePath = null)
        : base(captionHeight: CloseButtonHeight)
    {
        _repositoryPath = repositoryPath;
        _commits = commits;
        _commitSha = commits[0].Sha;
        _baselineSha = baselineSha;
        _initialFilePath = initialFilePath;
        var defaultCodeFontSize = (double)Application.Current.Resources["FontSizeNormal"];
        _codeFontSize = _codeFontStore.Load(defaultCodeFontSize);
        _codeHeaderFontRatio = (double)Application.Current.Resources["FontSizeSmall"] / defaultCodeFontSize;
        Title = baselineSha is not null
            ? $"Changes {ShortSha(baselineSha)}..{ShortSha(_commitSha)}"
            : commits.Count == 1 ? $"Commit {ShortSha(_commitSha)}" : $"Evidence · {commits.Count} commits";
        Width = commits.Count > 1 ? 1400 : 1120;
        Height = 760;
        MinWidth = 720;
        MinHeight = 440;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (commits.Count > 1) Loaded += (_, _) => SizeForActiveMonitor();

        _heading.FontSize = 18;
        _heading.FontWeight = FontWeights.SemiBold;
        _heading.TextTrimming = TextTrimming.CharacterEllipsis;
        _heading.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        _status.Margin = new Thickness(0, 3, 0, 0);
        _status.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");

        var header = new StackPanel { Margin = new Thickness(16, 13, 16, 12) };
        header.Children.Add(_heading);
        header.Children.Add(_status);

        _fileList.BorderThickness = new Thickness(0);
        _fileList.Padding = new Thickness(5);
        _fileList.SetResourceReference(BackgroundProperty, "CardSurface");
        _fileList.SelectionChanged += FileList_SelectionChanged;

        var diffScroll = new ScrollViewer
        {
            Content = _diffPanel,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(8)
        };
        diffScroll.SetResourceReference(BackgroundProperty, "CardSurface");
        diffScroll.SetResourceReference(StyleProperty, "RosterScrollViewerStyle");
        diffScroll.PreviewMouseWheel += DiffScroll_PreviewMouseWheel;

        _commitList.BorderThickness = new Thickness(0);
        _commitList.Padding = new Thickness(5);
        _commitList.SetResourceReference(BackgroundProperty, "CardSurface");
        _commitList.SelectionChanged += CommitList_SelectionChanged;

        var split = new Grid();
        if (commits.Count > 1)
        {
            split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
            split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        }
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var offset = commits.Count > 1 ? 2 : 0;
        if (commits.Count > 1)
        {
            Grid.SetColumn(_commitList, 0);
            var commitSplitter = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch };
            commitSplitter.SetResourceReference(BackgroundProperty, "LineColor");
            Grid.SetColumn(commitSplitter, 1);
            split.Children.Add(_commitList);
            split.Children.Add(commitSplitter);
        }
        Grid.SetColumn(_fileList, offset);
        var splitter = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch };
        splitter.SetResourceReference(BackgroundProperty, "LineColor");
        Grid.SetColumn(splitter, offset + 1);
        splitter.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount != 2 || _fileList.Items.Count == 0) return;
            e.Handled = true;

            var fileColumn = split.ColumnDefinitions[offset];
            var availableWidth = split.ActualWidth - splitter.ActualWidth -
                split.ColumnDefinitions.Take(offset).Sum(column => column.ActualWidth);
            var chromeWidth = _fileList.Padding.Left + _fileList.Padding.Right +
                _fileList.BorderThickness.Left + _fileList.BorderThickness.Right +
                SystemParameters.VerticalScrollBarWidth;
            fileColumn.Width = new GridLength(CommitViewerLayout.CalculateFileListWidth(
                _fileList.Items.Cast<ListBoxItem>().Select(CommitViewerLayout.MeasureFileListItemWidth),
                chromeWidth, availableWidth, fileColumn.ActualWidth));
        };
        Grid.SetColumn(diffScroll, offset + 2);
        split.Children.Add(_fileList);
        split.Children.Add(splitter);
        split.Children.Add(diffScroll);

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(split);
        ApplyOuterBorder(titleText: string.Empty).Child = root;
    }

    internal static async Task<CommitViewerWindow> CreateAsync(string repositoryPath, string commitSha)
    {
        var repositoryRoot = await CommitViewerGit.ResolveRepositoryRootAsync(repositoryPath);
        var window = new CommitViewerWindow(repositoryRoot, [new CommitViewerEntry(commitSha)]);
        await window.LoadCommitAsync();
        return window;
    }

    internal static async Task<CommitViewerWindow> CreateAsync(
        string repositoryPath, IReadOnlyList<CommitViewerEntry> commits)
    {
        if (commits.Count == 0) throw new ArgumentException("At least one commit is required.", nameof(commits));
        var repositoryRoot = await CommitViewerGit.ResolveRepositoryRootAsync(repositoryPath);
        var window = new CommitViewerWindow(repositoryRoot, commits);
        var alignUncertainGlyphs = commits.Any(commit => commit.IsUncertain);
        foreach (var commit in commits)
        {
            var subject = (await window.RunGitAsync("show", "-s", "--format=%s", commit.Sha)).Trim();
            var label = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(subject) ? ShortSha(commit.Sha) : subject,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TableCellText");
            FrameworkElement content = label;
            if (alignUncertainGlyphs)
            {
                var row = new Grid { Margin = new Thickness(2, 5, 4, 5) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                if (commit.IsUncertain)
                {
                    var glyph = Application.Current?.TryFindResource("TaskAwaitingHumanApproval") as Viewbox;
                    if (glyph is not null)
                    {
                        glyph.Width = 8;
                        glyph.Height = 13;
                        glyph.HorizontalAlignment = HorizontalAlignment.Left;
                        glyph.VerticalAlignment = VerticalAlignment.Top;
                        glyph.Margin = new Thickness(1, 2, 0, 0);
                        glyph.ToolTip = ToolTipHelper.MakeThemedToolTip(
                            CommitViewerLayout.BuildUncertainCommitToolTip(commit.Explanation));
                        row.Children.Add(glyph);
                    }
                }
                Grid.SetColumn(label, 1);
                row.Children.Add(label);
                content = row;
            }
            window._commitList.Items.Add(new ListBoxItem
            {
                Content = content,
                Tag = commit,
                ToolTip = CommitViewerLayout.BuildCommitToolTip(commit.IsUncertain, commit.Explanation) is { } tip
                    ? ToolTipHelper.MakeThemedToolTip(tip.ToString()!)
                    : null
            });
        }
        if (commits.Count > 1) window._commitList.SelectedIndex = 0;
        else await window.LoadCommitAsync();
        return window;
    }

    internal static async Task<CommitViewerWindow> CreateRangeAsync(
        string repositoryPath,
        string baselineSha,
        string terminalSha,
        string initialFilePath)
    {
        var repositoryRoot = await CommitViewerGit.ResolveRepositoryRootAsync(repositoryPath);
        var window = new CommitViewerWindow(
            repositoryRoot,
            [new CommitViewerEntry(terminalSha)],
            baselineSha,
            initialFilePath);
        await window.LoadCommitAsync();
        return window;
    }

    private void SizeForActiveMonitor()
    {
        var handle = new WindowInteropHelper(Owner ?? this).Handle;
        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var workingWidth = (info.Work.Right - info.Work.Left) / dpi.DpiScaleX;
            Width = CommitViewerLayout.CalculateWindowWidth(workingWidth, MinWidth);
        }
    }

    private async void CommitList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_commitList.SelectedItem is not ListBoxItem { Tag: CommitViewerEntry commit }) return;
        _commitSha = commit.Sha;
        await LoadCommitAsync();
    }

    private async Task LoadCommitAsync()
    {
        _fileList.Items.Clear();
        _heading.Text = $"Loading commit {ShortSha(_commitSha)}…";
        var description = await RunGitAsync("show", "-s", "--format=%s%n%an · %ad", "--date=local", _commitSha);
        var stats = _baselineSha is null
            ? await RunGitAsync("diff-tree", "--root", "--no-commit-id", "-r", "--numstat", _commitSha)
            : await RunGitAsync("diff", "--numstat", _baselineSha, _commitSha);
        var files = ParseNumStat(stats);

        var descriptionLines = description.Trim().Split('\n', 2);
        _heading.Text = descriptionLines.FirstOrDefault()?.TrimEnd('\r') ?? $"Commit {ShortSha(_commitSha)}";
        var revisionLabel = _baselineSha is null
            ? ShortSha(_commitSha)
            : $"{ShortSha(_baselineSha)}..{ShortSha(_commitSha)}";
        _status.Text = descriptionLines.Length > 1
            ? $"{revisionLabel} · {descriptionLines[1].Trim()} · {files.Count} changed file{(files.Count == 1 ? "" : "s")}"
            : $"{revisionLabel} · {files.Count} changed file{(files.Count == 1 ? "" : "s")}";

        foreach (var file in files)
            _fileList.Items.Add(CreateFileItem(file));

        if (_fileList.Items.Count > 0)
        {
            var selectedIndex = 0;
            if (!string.IsNullOrWhiteSpace(_initialFilePath))
            {
                for (var index = 0; index < _fileList.Items.Count; index++)
                {
                    if (_fileList.Items[index] is ListBoxItem { Tag: CommitFile candidate } &&
                        string.Equals(candidate.Path, _initialFilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedIndex = index;
                        break;
                    }
                }
            }
            _fileList.SelectedIndex = selectedIndex;
        }
        else
            ShowMessage("This commit does not contain any file changes.");
    }

    private ListBoxItem CreateFileItem(CommitFile file)
    {
        var path = new TextBlock
        {
            Text = file.Path,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 8, 0)
        };
        path.SetResourceReference(TextBlock.ForegroundProperty, "TableCellText");
        path.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");

        var summary = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        summary.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");
        if (file.IsBinary)
        {
            summary.Text = "binary";
            summary.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        }
        else
        {
            var added = new Run($"+{file.Added}") { FontWeight = FontWeights.SemiBold };
            added.SetResourceReference(TextElement.ForegroundProperty, "DiffAddedSummary");
            var removed = new Run($"  -{file.Removed}") { FontWeight = FontWeights.SemiBold };
            removed.SetResourceReference(TextElement.ForegroundProperty, "DiffRemovedSummary");
            summary.Inlines.Add(added);
            summary.Inlines.Add(removed);
        }

        var row = new Grid { Margin = new Thickness(3, 5, 3, 5) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(path, 0);
        Grid.SetColumn(summary, 1);
        row.Children.Add(path);
        row.Children.Add(summary);
        return new ListBoxItem { Content = row, Tag = file, ToolTip = file.Path };
    }

    private async void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_fileList.SelectedItem is not ListBoxItem { Tag: CommitFile file }) return;
        ShowMessage("Loading diff…");
        try
        {
            var text = _baselineSha is null
                ? await RunGitAsync("show", "--format=", "--no-ext-diff", "--no-color", "--unified=3", _commitSha, "--", file.Path)
                : await RunGitAsync("diff", "--no-ext-diff", "--no-color", "--unified=3", _baselineSha, _commitSha, "--", file.Path);
            RenderDiff(text);
        }
        catch (Exception ex)
        {
            ShowMessage($"Could not load this diff: {ex.Message}");
        }
    }

    private void RenderDiff(string text)
    {
        _diffPanel.Children.Clear();
        if (string.IsNullOrWhiteSpace(text))
        {
            ShowMessage("No text diff is available for this file.");
            return;
        }

        foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n'))
        {
            var kind = rawLine.StartsWith('+') && !rawLine.StartsWith("+++") ? DiffLineKind.Added
                : rawLine.StartsWith('-') && !rawLine.StartsWith("---") ? DiffLineKind.Removed
                : rawLine.StartsWith("@@") || rawLine.StartsWith("diff ") || rawLine.StartsWith("index ") ||
                  rawLine.StartsWith("---") || rawLine.StartsWith("+++") ? DiffLineKind.Header
                : DiffLineKind.Context;
            var line = DiffLinePresenter.Create(rawLine.Length == 0 ? " " : rawLine, kind);
            line.Tag = kind;
            ApplyCodeFontSize(line, kind);
            _diffPanel.Children.Add(line);
        }
    }

    private void ApplyCodeFontSize(TextBlock line, DiffLineKind kind) =>
        line.FontSize = _codeFontSize * (kind == DiffLineKind.Header ? _codeHeaderFontRatio : 1);

    private void DiffScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || e.Delta == 0)
            return;

        e.Handled = true;
        var fontSize = CommitViewerCodeFontStore.Clamp(_codeFontSize + Math.Sign(e.Delta));
        if (fontSize == _codeFontSize)
            return;

        _codeFontSize = fontSize;
        foreach (var child in _diffPanel.Children)
        {
            if (child is TextBlock { Tag: DiffLineKind kind } line)
                ApplyCodeFontSize(line, kind);
        }

        try
        {
            _codeFontStore.Save(_codeFontSize);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            SquadDashTrace.Write("CommitViewerCodeFont", $"Could not save code font size: {ex.Message}");
            MessageBox.Show(this, $"The code font size changed, but could not be saved.\n\n{ex.Message}",
                "Commit Viewer", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowMessage(string message)
    {
        _diffPanel.Children.Clear();
        var text = new TextBlock { Text = message, Margin = new Thickness(10), TextWrapping = TextWrapping.Wrap };
        text.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        _diffPanel.Children.Add(text);
    }

    private Task<string> RunGitAsync(params string[] arguments) =>
        CommitViewerGit.RunAsync(_repositoryPath, arguments);

    private static List<CommitFile> ParseNumStat(string text)
    {
        var result = new List<CommitFile>();
        foreach (var line in text.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t', 3);
            if (parts.Length != 3) continue;
            var binary = parts[0] == "-" || parts[1] == "-";
            _ = int.TryParse(parts[0], out var added);
            _ = int.TryParse(parts[1], out var removed);
            result.Add(new CommitFile(parts[2], added, removed, binary));
        }
        return result;
    }

    private static string ShortSha(string sha) => sha.Length > 8 ? sha[..8] : sha;
    private sealed record CommitFile(string Path, int Added, int Removed, bool IsBinary);

    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}

internal sealed record CommitViewerEntry(string Sha, bool IsUncertain = false, string? Explanation = null);
