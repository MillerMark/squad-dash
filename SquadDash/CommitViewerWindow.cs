using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace SquadDash;

internal sealed class CommitViewerWindow : ChromedWindow
{
    private readonly string _repositoryPath;
    private readonly string _commitSha;
    private readonly ListBox _fileList = new();
    private readonly StackPanel _diffPanel = new();
    private readonly TextBlock _heading = new();
    private readonly TextBlock _status = new();

    private CommitViewerWindow(string repositoryPath, string commitSha)
        : base(captionHeight: CloseButtonHeight)
    {
        _repositoryPath = repositoryPath;
        _commitSha = commitSha;
        Title = $"Commit {ShortSha(commitSha)}";
        Width = 1120;
        Height = 760;
        MinWidth = 720;
        MinHeight = 440;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

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

        var split = new Grid();
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(_fileList, 0);
        var splitter = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch };
        splitter.SetResourceReference(BackgroundProperty, "LineColor");
        Grid.SetColumn(splitter, 1);
        Grid.SetColumn(diffScroll, 2);
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
        var window = new CommitViewerWindow(repositoryPath, commitSha);
        await window.LoadCommitAsync();
        return window;
    }

    private async Task LoadCommitAsync()
    {
        _heading.Text = $"Loading commit {ShortSha(_commitSha)}…";
        var description = await RunGitAsync("show", "-s", "--format=%s%n%an · %ad", "--date=local", _commitSha);
        var stats = await RunGitAsync("diff-tree", "--root", "--no-commit-id", "-r", "--numstat", _commitSha);
        var files = ParseNumStat(stats);

        var descriptionLines = description.Trim().Split('\n', 2);
        _heading.Text = descriptionLines.FirstOrDefault()?.TrimEnd('\r') ?? $"Commit {ShortSha(_commitSha)}";
        _status.Text = descriptionLines.Length > 1
            ? $"{ShortSha(_commitSha)} · {descriptionLines[1].Trim()} · {files.Count} changed file{(files.Count == 1 ? "" : "s")}" 
            : $"{ShortSha(_commitSha)} · {files.Count} changed file{(files.Count == 1 ? "" : "s")}";

        foreach (var file in files)
            _fileList.Items.Add(CreateFileItem(file));

        if (_fileList.Items.Count > 0)
            _fileList.SelectedIndex = 0;
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

        var summary = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
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
            var text = await RunGitAsync("show", "--format=", "--no-ext-diff", "--no-color", "--unified=3", _commitSha, "--", file.Path);
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
            _diffPanel.Children.Add(DiffLinePresenter.Create(rawLine.Length == 0 ? " " : rawLine, kind));
        }
    }

    private void ShowMessage(string message)
    {
        _diffPanel.Children.Clear();
        var text = new TextBlock { Text = message, Margin = new Thickness(10), TextWrapping = TextWrapping.Wrap };
        text.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        _diffPanel.Children.Add(text);
    }

    private async Task<string> RunGitAsync(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = _repositoryPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Git could not be started.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0) throw new InvalidOperationException(error.Trim());
        return output;
    }

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
}
