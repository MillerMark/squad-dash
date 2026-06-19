using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace SquadDash;

internal sealed class ModelProviderProbeWindow : ChromedWindow {
    private readonly ModelProviderProbeService _probeService;
    private readonly string _providerUrl;
    private readonly string? _apiKey;
    private readonly ObservableCollection<ModelProviderProbeResult> _models;
    private readonly DataGrid _modelsGrid;
    private readonly Button _useButton;
    private readonly Button _liveProbeButton;
    private readonly Button _loadButton;
    private readonly TextBlock _statusText;

    public string? SelectedModelId { get; private set; }

    public ModelProviderProbeWindow(
        ModelProviderProbeService probeService,
        string providerUrl,
        string? apiKey,
        IReadOnlyList<ModelProviderProbeResult> models) : base(captionHeight: CloseButtonHeight) {
        _probeService = probeService;
        _providerUrl = providerUrl;
        _apiKey = apiKey;
        _models = new ObservableCollection<ModelProviderProbeResult>(models);

        Title = "Model Probe";
        Width = 980;
        Height = 620;
        MinWidth = 760;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var content = ApplyOuterBorder(titleText: "Model Probe");
        var root = new DockPanel { Margin = new Thickness(16) };
        content.Child = root;

        var summary = new TextBlock {
            Text = $"Provider: {_providerUrl}\nSelect one model to run a live chat/tool probe. Live probes can load that model, so run them one at a time.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        summary.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        summary.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
        DockPanel.SetDock(summary, Dock.Top);
        root.Children.Add(summary);

        var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var closeButton = MakeButton("Close", 86);
        closeButton.Click += (_, _) => Close();
        DockPanel.SetDock(closeButton, Dock.Right);
        footer.Children.Add(closeButton);

        _useButton = MakeButton("Use This Model", 130);
        _useButton.IsEnabled = false;
        _useButton.Margin = new Thickness(0, 0, 8, 0);
        _useButton.Click += (_, _) => UseSelectedModel();
        DockPanel.SetDock(_useButton, Dock.Right);
        footer.Children.Add(_useButton);

        _loadButton = MakeButton("Load Model", 110);
        _loadButton.IsEnabled = false;
        _loadButton.Margin = new Thickness(0, 0, 8, 0);
        _loadButton.Click += LoadButton_Click;
        DockPanel.SetDock(_loadButton, Dock.Right);
        footer.Children.Add(_loadButton);

        _liveProbeButton = MakeButton("Live Probe", 110);
        _liveProbeButton.IsEnabled = false;
        _liveProbeButton.Margin = new Thickness(0, 0, 8, 0);
        _liveProbeButton.Click += LiveProbeButton_Click;
        DockPanel.SetDock(_liveProbeButton, Dock.Right);
        footer.Children.Add(_liveProbeButton);

        _statusText = new TextBlock {
            Text = models.Count == 0 ? "No models were returned by the provider." : $"{models.Count} model(s) found.",
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        _statusText.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        _statusText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
        footer.Children.Add(_statusText);

        _modelsGrid = new DataGrid {
            ItemsSource = _models,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal
        };
        ApplyGridTheme(_modelsGrid);
        _modelsGrid.SelectionChanged += (_, _) => SyncButtonState();
        _modelsGrid.MouseDoubleClick += (_, _) => UseSelectedModel();

        _modelsGrid.Columns.Add(MakeTextColumn("Model", nameof(ModelProviderProbeResult.ModelId), 260));
        _modelsGrid.Columns.Add(MakeTextColumn("Parent", nameof(ModelProviderProbeResult.ParentModel), 150));
        _modelsGrid.Columns.Add(MakeTextColumn("Owner", nameof(ModelProviderProbeResult.Owner), 110));
        _modelsGrid.Columns.Add(MakeTextColumn("Catalog Tools", nameof(ModelProviderProbeResult.CatalogToolCallingText), 120));
        _modelsGrid.Columns.Add(MakeTextColumn("Chat", nameof(ModelProviderProbeResult.ChatStatusText), 90));
        _modelsGrid.Columns.Add(MakeTextColumn("Tool Probe", nameof(ModelProviderProbeResult.ToolStatusText), 100));
        _modelsGrid.Columns.Add(MakeTextColumn("Notes", nameof(ModelProviderProbeResult.Notes), 260));

        root.Children.Add(_modelsGrid);
    }

    private Button MakeButton(string text, double width) {
        var button = new Button {
            Content = text,
            Width = width,
            Height = 30,
            Padding = new Thickness(10, 4, 10, 4)
        };
        button.SetResourceReference(Button.StyleProperty, "ThemedButtonStyle");
        return button;
    }

    private static DataGridTextColumn MakeTextColumn(string header, string path, double width) {
        return new DataGridTextColumn {
            Header = header,
            Binding = new Binding(path),
            Width = width,
            ElementStyle = new Style(typeof(TextBlock)) {
                Setters = {
                    new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap),
                    new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center)
                }
            }
        };
    }

    private static void ApplyGridTheme(DataGrid grid) {
        grid.SetResourceReference(DataGrid.BackgroundProperty, "AppSurface");
        grid.SetResourceReference(DataGrid.ForegroundProperty, "LabelText");
        grid.SetResourceReference(DataGrid.BorderBrushProperty, "SubtleBorder");
        grid.SetResourceReference(DataGrid.RowBackgroundProperty, "TextBoxBackground");
        grid.SetResourceReference(DataGrid.AlternatingRowBackgroundProperty, "TextBoxBackground");
        grid.SetResourceReference(DataGrid.HorizontalGridLinesBrushProperty, "SubtleBorder");
        grid.SetResourceReference(DataGrid.VerticalGridLinesBrushProperty, "SubtleBorder");

        var headerStyle = new Style(typeof(DataGridColumnHeader));
        headerStyle.Setters.Add(new Setter(DataGridColumnHeader.BackgroundProperty, new DynamicResourceExtension("AppSurface")));
        headerStyle.Setters.Add(new Setter(DataGridColumnHeader.ForegroundProperty, new DynamicResourceExtension("LabelText")));
        headerStyle.Setters.Add(new Setter(DataGridColumnHeader.BorderBrushProperty, new DynamicResourceExtension("SubtleBorder")));
        headerStyle.Setters.Add(new Setter(DataGridColumnHeader.PaddingProperty, new Thickness(6, 4, 6, 4)));
        grid.ColumnHeaderStyle = headerStyle;

        var cellStyle = new Style(typeof(DataGridCell));
        cellStyle.Setters.Add(new Setter(DataGridCell.BackgroundProperty, new DynamicResourceExtension("TextBoxBackground")));
        cellStyle.Setters.Add(new Setter(DataGridCell.ForegroundProperty, new DynamicResourceExtension("LabelText")));
        cellStyle.Setters.Add(new Setter(DataGridCell.BorderBrushProperty, new DynamicResourceExtension("SubtleBorder")));
        cellStyle.Setters.Add(new Setter(DataGridCell.BorderThicknessProperty, new Thickness(0)));
        cellStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 4, 6, 4)));
        grid.CellStyle = cellStyle;
    }

    private void SyncButtonState() {
        var hasSelection = _modelsGrid.SelectedItem is ModelProviderProbeResult;
        _useButton.IsEnabled = hasSelection;
        _liveProbeButton.IsEnabled = hasSelection;
        _loadButton.IsEnabled = hasSelection;
    }

    private void UseSelectedModel() {
        if (_modelsGrid.SelectedItem is not ModelProviderProbeResult selected)
            return;

        SelectedModelId = selected.ModelId;
        DialogResult = true;
        Close();
    }

    private async void LiveProbeButton_Click(object sender, RoutedEventArgs e) {
        if (_modelsGrid.SelectedItem is not ModelProviderProbeResult selected)
            return;

        var index = _models.IndexOf(selected);
        if (index < 0)
            return;

        _liveProbeButton.IsEnabled = false;
        _useButton.IsEnabled = false;
        _statusText.Text = $"Running live probe for {selected.ModelId}...";
        try {
            var probed = await _probeService.RunLiveProbeAsync(_providerUrl, _apiKey, selected);
            _models[index] = probed;
            _modelsGrid.SelectedItem = probed;
            _statusText.Text = $"Live probe complete for {selected.ModelId}.";
        }
        catch (Exception ex) {
            _models[index] = selected with {
                ChatStatus = ModelProbeCheckStatus.Failed,
                ToolStatus = ModelProbeCheckStatus.Failed,
                Notes = ex.Message
            };
            _modelsGrid.SelectedIndex = index;
            _statusText.Text = $"Live probe failed: {ex.Message}";
        }
        finally {
            SyncButtonState();
        }
    }

    private async void LoadButton_Click(object sender, RoutedEventArgs e) {
        if (_modelsGrid.SelectedItem is not ModelProviderProbeResult selected)
            return;

        var index = _models.IndexOf(selected);
        if (index < 0)
            return;

        SetActionButtonsEnabled(false);
        _statusText.Text = $"Loading {selected.ModelId} with Foundry...";
        try {
            var result = await _probeService.LoadFoundryModelAsync(selected.ModelId);
            var note = result.Success
                ? BuildLoadNote("Load succeeded", result)
                : BuildLoadNote("Load failed", result);

            _models[index] = selected with {
                ChatStatus = result.Success && selected.ChatStatus == ModelProbeCheckStatus.NotLoaded
                    ? ModelProbeCheckStatus.NotRun
                    : selected.ChatStatus,
                ToolStatus = result.Success && selected.ToolStatus == ModelProbeCheckStatus.NotLoaded
                    ? ModelProbeCheckStatus.NotRun
                    : selected.ToolStatus,
                Notes = AppendNote(selected.Notes, note)
            };
            _modelsGrid.SelectedIndex = index;
            _statusText.Text = result.Success
                ? $"Loaded {selected.ModelId}. Run Live Probe to test it."
                : $"Load failed for {selected.ModelId}.";
        }
        catch (OperationCanceledException) {
            _models[index] = selected with {
                Notes = AppendNote(selected.Notes, "Load canceled.")
            };
            _modelsGrid.SelectedIndex = index;
            _statusText.Text = $"Load canceled for {selected.ModelId}.";
        }
        catch (Exception ex) {
            _models[index] = selected with {
                Notes = AppendNote(selected.Notes, $"Load failed: {ex.Message}")
            };
            _modelsGrid.SelectedIndex = index;
            _statusText.Text = $"Load failed: {ex.Message}";
        }
        finally {
            SyncButtonState();
        }
    }

    private void SetActionButtonsEnabled(bool enabled) {
        _useButton.IsEnabled = enabled;
        _liveProbeButton.IsEnabled = enabled;
        _loadButton.IsEnabled = enabled;
    }

    private static string BuildLoadNote(string prefix, ModelProviderCommandResult result) {
        var detail = FirstNonEmpty(result.Output, result.Error);
        var suffix = string.IsNullOrWhiteSpace(detail)
            ? $"exit={result.ExitCode}"
            : $"{detail} exit={result.ExitCode}";
        return $"{prefix}: {suffix}";
    }

    private static string? FirstNonEmpty(params string?[] values) {
        foreach (var value in values) {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }
        return null;
    }

    private static string AppendNote(string? existing, string note) {
        if (string.IsNullOrWhiteSpace(existing))
            return note;

        return $"{existing} {note}";
    }
}
