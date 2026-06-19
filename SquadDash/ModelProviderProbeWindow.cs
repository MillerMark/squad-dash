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
        Width = 1300;
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

        _useButton = MakeButton("Use Selected Model", 160);
        _useButton.IsEnabled = false;
        _useButton.Margin = new Thickness(0, 0, 8, 0);
        _useButton.Click += (_, _) => UseSelectedModel();
        DockPanel.SetDock(_useButton, Dock.Right);
        footer.Children.Add(_useButton);

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
        _modelsGrid.Columns.Add(MakeTextColumn("Parent", nameof(ModelProviderProbeResult.ParentModel), 190));
        _modelsGrid.Columns.Add(MakeTextColumn("Owner", nameof(ModelProviderProbeResult.Owner), 110));
        _modelsGrid.Columns.Add(MakeTextColumn("Catalog Tools", nameof(ModelProviderProbeResult.CatalogToolCallingText), 120));
        _modelsGrid.Columns.Add(MakeTextColumn("Chat", nameof(ModelProviderProbeResult.ChatStatusText), 90));
        _modelsGrid.Columns.Add(MakeTextColumn("Tool Probe", nameof(ModelProviderProbeResult.ToolStatusText), 100));
        _modelsGrid.Columns.Add(MakeTextColumn("Notes", nameof(ModelProviderProbeResult.NoteSummary), new DataGridLength(1, DataGridLengthUnitType.Star), minWidth: 260));
        _modelsGrid.Columns.Add(MakeActionColumn());

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

    private static DataGridTextColumn MakeTextColumn(string header, string path, double width) =>
        MakeTextColumn(header, path, new DataGridLength(width), minWidth: 0);

    private static DataGridTextColumn MakeTextColumn(string header, string path, DataGridLength width, double minWidth) {
        return new DataGridTextColumn {
            Header = header,
            Binding = new Binding(path),
            Width = width,
            MinWidth = minWidth,
            ElementStyle = new Style(typeof(TextBlock)) {
                Setters = {
                    new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap),
                    new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center)
                }
            }
        };
    }

    private static DataGridTemplateColumn MakeActionColumn() {
        var template = new DataTemplate();
        var button = new FrameworkElementFactory(typeof(Button));
        button.SetValue(FrameworkElement.HeightProperty, 24.0);
        button.SetValue(FrameworkElement.MinWidthProperty, 70.0);
        button.SetValue(Control.PaddingProperty, new Thickness(8, 2, 8, 2));
        button.SetBinding(ContentControl.ContentProperty, new Binding(nameof(ModelProviderProbeResult.RowActionText)));
        button.SetResourceReference(Button.StyleProperty, "ThemedButtonStyle");
        button.AddHandler(Button.ClickEvent, new RoutedEventHandler(RowActionButton_Click));
        template.VisualTree = button;

        return new DataGridTemplateColumn {
            Header = "Action",
            CellTemplate = template,
            Width = 104,
            MinWidth = 96
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
        var selectedCellTrigger = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
        selectedCellTrigger.Setters.Add(new Setter(DataGridCell.BackgroundProperty, new DynamicResourceExtension("ActivePanelSurface")));
        selectedCellTrigger.Setters.Add(new Setter(DataGridCell.ForegroundProperty, new DynamicResourceExtension("ImportantText")));
        cellStyle.Triggers.Add(selectedCellTrigger);
        grid.CellStyle = cellStyle;

        var rowStyle = new Style(typeof(DataGridRow));
        rowStyle.Setters.Add(new Setter(DataGridRow.BackgroundProperty, new DynamicResourceExtension("TextBoxBackground")));
        rowStyle.Setters.Add(new Setter(DataGridRow.ForegroundProperty, new DynamicResourceExtension("LabelText")));
        var selectedRowTrigger = new Trigger { Property = DataGridRow.IsSelectedProperty, Value = true };
        selectedRowTrigger.Setters.Add(new Setter(DataGridRow.BackgroundProperty, new DynamicResourceExtension("ActivePanelSurface")));
        selectedRowTrigger.Setters.Add(new Setter(DataGridRow.ForegroundProperty, new DynamicResourceExtension("ImportantText")));
        rowStyle.Triggers.Add(selectedRowTrigger);
        grid.RowStyle = rowStyle;
    }

    private static void RowActionButton_Click(object sender, RoutedEventArgs e) {
        if (sender is not FrameworkElement { DataContext: ModelProviderProbeResult result })
            return;

        if (Window.GetWindow((DependencyObject)sender) is ModelProviderProbeWindow owner)
            owner.ExecuteRowAction(result);
        e.Handled = true;
    }

    private void SyncButtonState() {
        var hasSelection = _modelsGrid.SelectedItem is ModelProviderProbeResult;
        _useButton.IsEnabled = hasSelection;
    }

    private void UseSelectedModel() {
        if (_modelsGrid.SelectedItem is not ModelProviderProbeResult selected)
            return;

        SelectedModelId = selected.ModelId;
        DialogResult = true;
        Close();
    }

    private void ExecuteRowAction(ModelProviderProbeResult selected) {
        _modelsGrid.SelectedItem = selected;
        if (selected.RowActionText == "Load") {
            _ = LoadModelAsync(selected);
            return;
        }

        if (selected.RowActionText == "Probe") {
            _ = RunLiveProbeAsync(selected);
            return;
        }

        ShowDetails(selected);
    }

    private async Task RunLiveProbeAsync(ModelProviderProbeResult selected) {
        var index = _models.IndexOf(selected);
        if (index < 0)
            return;

        SetActionButtonsEnabled(false);
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

    private async Task LoadModelAsync(ModelProviderProbeResult selected) {
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

    private void ShowDetails(ModelProviderProbeResult selected) {
        var window = new ModelProviderProbeNoteWindow(_providerUrl, selected) {
            Owner = this
        };
        window.ShowDialog();
    }

    private void SetActionButtonsEnabled(bool enabled) {
        _useButton.IsEnabled = enabled;
        _modelsGrid.IsEnabled = enabled;
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

internal sealed class ModelProviderProbeNoteWindow : ChromedWindow {
    private readonly string _diagnosticText;
    private readonly TextBlock _statusText;

    public ModelProviderProbeNoteWindow(string providerUrl, ModelProviderProbeResult result) : base(captionHeight: CloseButtonHeight) {
        _diagnosticText = BuildDiagnosticText(providerUrl, result);

        Title = "Probe Details";
        Width = 780;
        Height = 520;
        MinWidth = 560;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var content = ApplyOuterBorder(titleText: "Probe Details");
        var root = new DockPanel { Margin = new Thickness(16) };
        content.Child = root;

        var header = new TextBlock {
            Text = result.ModelId,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
            FontWeight = FontWeights.SemiBold
        };
        header.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var closeButton = MakeButton("Close", 86);
        closeButton.Click += (_, _) => Close();
        DockPanel.SetDock(closeButton, Dock.Right);
        footer.Children.Add(closeButton);

        var copyButton = MakeButton("Copy", 86);
        copyButton.Margin = new Thickness(0, 0, 8, 0);
        copyButton.Click += (_, _) => CopyNote();
        DockPanel.SetDock(copyButton, Dock.Right);
        footer.Children.Add(copyButton);

        _statusText = new TextBlock {
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        _statusText.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        _statusText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
        footer.Children.Add(_statusText);

        var detailsBox = new TextBox {
            Text = _diagnosticText,
            IsReadOnly = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            Padding = new Thickness(8)
        };
        detailsBox.SetResourceReference(TextBox.BackgroundProperty, "TextBoxBackground");
        detailsBox.SetResourceReference(TextBox.BorderBrushProperty, "InputBorder");
        detailsBox.SetResourceReference(TextBox.ForegroundProperty, "LabelText");
        root.Children.Add(detailsBox);
    }

    private static string BuildDiagnosticText(string providerUrl, ModelProviderProbeResult result) {
        var lines = new List<string> {
            "Model Probe Diagnostics",
            "",
            $"Provider URL: {providerUrl}",
            $"Endpoint root: {result.ProviderEndpointRoot}",
            $"Provider kind: {InferProviderKind(providerUrl, result)}",
            $"Model: {result.ModelId}",
            $"Parent: {result.ParentModel ?? "(unknown)"}",
            $"Owner: {result.Owner ?? "(unknown)"}",
            $"Catalog tool calling: {result.CatalogToolCallingText}",
            $"Catalog notes: {result.CatalogNotes ?? "(none)"}",
            $"Chat probe: {result.ChatStatusText}",
            $"Tool probe: {result.ToolStatusText}",
            "",
            "Notes:",
            string.IsNullOrWhiteSpace(result.Notes) ? "(none)" : result.Notes!
        };

        return string.Join(Environment.NewLine, lines);
    }

    private static string InferProviderKind(string providerUrl, ModelProviderProbeResult result) {
        if (string.Equals(result.Owner, "Microsoft", StringComparison.OrdinalIgnoreCase) ||
            result.ModelId.Contains("-cuda-gpu", StringComparison.OrdinalIgnoreCase) ||
            providerUrl.Contains("5273", StringComparison.OrdinalIgnoreCase))
            return "Foundry Local";

        if (providerUrl.Contains("11434", StringComparison.OrdinalIgnoreCase))
            return "Ollama";

        return "OpenAI-compatible";
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

    private void CopyNote() {
        Clipboard.SetText(_diagnosticText);
        _statusText.Text = "Copied.";
    }
}
