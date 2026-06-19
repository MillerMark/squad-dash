using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace SquadDash;

internal sealed record ModelProviderProbeWarning(
    string Message,
    string? Guidance,
    IReadOnlyList<string> Folders);

internal sealed class ModelProviderProbeWindow : ChromedWindow {
    private readonly ModelProviderProbeService _probeService;
    private readonly string _providerUrl;
    private readonly string? _apiKey;
    private readonly ObservableCollection<ModelProviderProbeResult> _models;
    private readonly bool _canLoadFoundryModels;
    private readonly HashSet<string> _loadedFoundryModelIds;
    private readonly HashSet<string> _loadedFoundryUnloadIds;
    private readonly StackPanel? _memoryPanel;
    private readonly DataGrid _modelsGrid;
    private readonly Button _useButton;
    private readonly Button _copyAllButton;
    private readonly Button? _cleanFoundryButton;
    private readonly Button _closeButton;
    private readonly TextBlock _statusText;
    private readonly string? _initialModelId;
    private string? _loadedFoundryModelId;
    private bool _isBusy;
    private bool _allowClose;
    private bool _closeFinalizationStarted;
    private bool? _finalDialogResult;
    private string? _desiredModelIdOnClose;

    public string? SelectedModelId { get; private set; }

    public ModelProviderProbeWindow(
        ModelProviderProbeService probeService,
        string providerUrl,
        string? apiKey,
        IReadOnlyList<ModelProviderProbeResult> models,
        ModelProviderProbeWarning? providerWarning = null,
        string? initialModelId = null,
        bool canLoadFoundryModels = false,
        ModelProviderLocalStatus? localStatus = null) : base(captionHeight: CloseButtonHeight) {
        _probeService = probeService;
        _providerUrl = providerUrl;
        _apiKey = apiKey;
        _canLoadFoundryModels = canLoadFoundryModels;
        _loadedFoundryModelIds = BuildLoadedFoundryModelIdSet(localStatus?.LoadedModels ?? Array.Empty<FoundryLoadedModel>());
        _loadedFoundryUnloadIds = BuildFoundryUnloadIdSet(localStatus?.LoadedModels ?? Array.Empty<FoundryLoadedModel>());
        _models = new ObservableCollection<ModelProviderProbeResult>(
            models.Select(model => model with {
                CanLoadLocally = canLoadFoundryModels,
                IsLoadedLocally = canLoadFoundryModels && IsModelLoaded(model, _loadedFoundryModelIds)
            }));
        _initialModelId = NormalizeModelId(initialModelId);
        _loadedFoundryModelId = _loadedFoundryUnloadIds.Count == 1
            ? _loadedFoundryUnloadIds.First()
            : null;

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

        if (providerWarning is not null && !string.IsNullOrWhiteSpace(providerWarning.Message)) {
            var warningHost = new DockPanel {
                Margin = new Thickness(0, 0, 0, 12)
            };
            warningHost.ContextMenu = MakeWarningContextMenu(BuildWarningClipboardText(providerWarning));

            var warningBorder = new Border {
                Padding = new Thickness(10, 8, 10, 8)
            };
            warningBorder.SetResourceReference(Border.BackgroundProperty, "SystemErrorBackground");

            var warningPanel = new DockPanel();
            warningBorder.Child = warningPanel;

            if (providerWarning.Folders.Count > 0) {
                var openFoldersButton = MakeButton("Open CLI Folders", 132);
                openFoldersButton.Margin = new Thickness(10, 0, 0, 0);
                openFoldersButton.VerticalAlignment = VerticalAlignment.Bottom;
                openFoldersButton.Click += (_, _) => OpenWarningFolders(providerWarning.Folders);
                DockPanel.SetDock(openFoldersButton, Dock.Right);
                warningPanel.Children.Add(openFoldersButton);
            }

            var warningText = new TextBlock {
                Text = providerWarning.Message.Trim(),
                TextWrapping = TextWrapping.Wrap,
                FontWeight = FontWeights.SemiBold
            };
            warningText.SetResourceReference(TextBlock.ForegroundProperty, "SystemErrorText");
            warningPanel.Children.Add(warningText);

            DockPanel.SetDock(warningBorder, Dock.Top);
            warningHost.Children.Add(warningBorder);

            if (!string.IsNullOrWhiteSpace(providerWarning.Guidance)) {
                var guidanceText = new TextBlock {
                    Text = providerWarning.Guidance.Trim(),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(10, 6, 10, 0),
                    FontWeight = FontWeights.SemiBold
                };
                guidanceText.SetResourceReference(TextBlock.ForegroundProperty, "ImportantText");
                DockPanel.SetDock(guidanceText, Dock.Bottom);
                warningHost.Children.Add(guidanceText);
            }

            DockPanel.SetDock(warningHost, Dock.Top);
            root.Children.Add(warningHost);
        }

        if (localStatus?.GpuMemory.Count > 0) {
            _memoryPanel = BuildMemoryPanel(localStatus);
            DockPanel.SetDock(_memoryPanel, Dock.Top);
            root.Children.Add(_memoryPanel);
        }

        var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        _closeButton = MakeButton("Close", 86);
        _closeButton.Click += (_, _) => BeginCloseWithModelState(null, false);
        DockPanel.SetDock(_closeButton, Dock.Right);
        footer.Children.Add(_closeButton);

        _useButton = MakeButton("Use Selected Model", 160);
        _useButton.IsEnabled = false;
        _useButton.Margin = new Thickness(0, 0, 8, 0);
        _useButton.Click += (_, _) => UseSelectedModel();
        DockPanel.SetDock(_useButton, Dock.Right);
        footer.Children.Add(_useButton);

        _copyAllButton = MakeButton("Copy All Details", 140);
        _copyAllButton.IsEnabled = _models.Count > 0;
        _copyAllButton.Margin = new Thickness(0, 0, 8, 0);
        _copyAllButton.Click += (_, _) => CopyAllDetails();
        DockPanel.SetDock(_copyAllButton, Dock.Right);
        footer.Children.Add(_copyAllButton);

        if (_canLoadFoundryModels) {
            _cleanFoundryButton = MakeButton("Unload Loaded Models", 168);
            _cleanFoundryButton.IsEnabled = _loadedFoundryUnloadIds.Count > 0;
            _cleanFoundryButton.Margin = new Thickness(0, 0, 8, 0);
            _cleanFoundryButton.Click += (_, _) => _ = UnloadLoadedFoundryModelsAsync();
            DockPanel.SetDock(_cleanFoundryButton, Dock.Right);
            footer.Children.Add(_cleanFoundryButton);
        }

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
        _modelsGrid.Columns.Add(MakeStatusColumn("Chat", nameof(ModelProviderProbeResult.ChatStatus), nameof(ModelProviderProbeResult.ChatStatusDisplay), 100));
        _modelsGrid.Columns.Add(MakeStatusColumn("Tools", nameof(ModelProviderProbeResult.ToolStatus), nameof(ModelProviderProbeResult.ToolStatusDisplay), 120));
        _modelsGrid.Columns.Add(MakeTextColumn("Notes", nameof(ModelProviderProbeResult.NoteSummary), new DataGridLength(1, DataGridLengthUnitType.Star), minWidth: 260));
        _modelsGrid.Columns.Add(MakeActionColumn());

        root.Children.Add(_modelsGrid);
    }

    private static string BuildWarningClipboardText(ModelProviderProbeWarning warning) {
        return string.IsNullOrWhiteSpace(warning.Guidance)
            ? warning.Message.Trim()
            : $"{warning.Message.Trim()}{Environment.NewLine}{Environment.NewLine}{warning.Guidance.Trim()}";
    }

    private static ContextMenu MakeWarningContextMenu(string message) {
        var menu = new ContextMenu();
        var copyItem = new MenuItem { Header = "Copy Warning" };
        copyItem.Click += (_, _) => Clipboard.SetText(message);
        menu.Items.Add(copyItem);
        return menu;
    }

    private static void OpenWarningFolders(IReadOnlyList<string> folders) {
        foreach (var folder in folders.Distinct(StringComparer.OrdinalIgnoreCase)) {
            try {
                Process.Start(new ProcessStartInfo {
                    FileName = folder,
                    UseShellExecute = true
                });
            }
            catch (Exception ex) {
                UIErrorHelper.ShowWarning("Open Folder", $"Could not open:{Environment.NewLine}{folder}{Environment.NewLine}{Environment.NewLine}{ex.Message}");
            }
        }
    }

    private static HashSet<string> BuildLoadedFoundryModelIdSet(IReadOnlyList<FoundryLoadedModel> loadedModels) {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in loadedModels) {
            AddModelId(ids, model.ModelId);
            AddModelId(ids, RemoveFoundryVariantSuffix(model.ModelId));
            AddModelId(ids, model.Alias);
            AddModelId(ids, model.DisplayName);
        }

        return ids;
    }

    private static HashSet<string> BuildFoundryUnloadIdSet(IReadOnlyList<FoundryLoadedModel> loadedModels) {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in loadedModels) {
            AddModelId(ids, model.DisplayName);
            if (string.IsNullOrWhiteSpace(model.DisplayName))
                AddModelId(ids, RemoveFoundryVariantSuffix(model.ModelId));
        }

        return ids;
    }

    private static bool IsModelLoaded(ModelProviderProbeResult model, HashSet<string> loadedModelIds) {
        return loadedModelIds.Contains(model.ModelId) ||
               (!string.IsNullOrWhiteSpace(model.ParentModel) && loadedModelIds.Contains(model.ParentModel));
    }

    private static void AddModelId(HashSet<string> ids, string? value) {
        if (!string.IsNullOrWhiteSpace(value))
            ids.Add(value.Trim());
    }

    private static string? RemoveFoundryVariantSuffix(string? modelId) {
        if (string.IsNullOrWhiteSpace(modelId))
            return null;

        var trimmed = modelId.Trim();
        var separator = trimmed.LastIndexOf(':');
        return separator > 0 ? trimmed[..separator] : trimmed;
    }

    private StackPanel BuildMemoryPanel(ModelProviderLocalStatus status) {
        var host = new StackPanel {
            Margin = new Thickness(0, 0, 0, 12)
        };
        PopulateMemoryPanel(host, status);
        return host;
    }

    private void PopulateMemoryPanel(StackPanel host, ModelProviderLocalStatus status) {
        host.Children.Clear();

        var loadedGpuModels = status.LoadedModels
            .Where(model => string.Equals(model.Device, "Gpu", StringComparison.OrdinalIgnoreCase))
            .Select(model => model.DisplayName ?? model.Alias ?? RemoveFoundryVariantSuffix(model.ModelId) ?? model.ModelId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var loadedText = loadedGpuModels.Length == 0
            ? null
            : $"Loaded GPU models: {string.Join(", ", loadedGpuModels)}";

        foreach (var gpu in status.GpuMemory.OrderBy(gpu => gpu.Index)) {
            host.Children.Add(BuildGpuMemoryRow(gpu, loadedText));
        }
    }

    private FrameworkElement BuildGpuMemoryRow(LocalGpuMemoryInfo gpu, string? loadedText) {
        var row = new DockPanel {
            Height = 30,
            LastChildFill = true,
            Margin = new Thickness(0, 0, 0, 4)
        };

        var label = new TextBlock {
            Text = $"GPU {gpu.Index}: {gpu.Name}",
            Width = 260,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        label.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
        DockPanel.SetDock(label, Dock.Left);
        row.Children.Add(label);

        var totalText = new TextBlock {
            Text = $"{gpu.UsedMiB:N0} / {gpu.TotalMiB:N0} MiB",
            Width = 140,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        totalText.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        totalText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
        DockPanel.SetDock(totalText, Dock.Right);
        row.Children.Add(totalText);

        var total = Math.Max(1, gpu.TotalMiB);
        var used = Math.Clamp(gpu.UsedMiB, 0, total);
        var free = Math.Max(0, total - used);
        var bar = new Grid {
            Height = 22,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = loadedText
        };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.1, used), GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.1, free), GridUnitType.Star) });

        var usedBorder = new Border();
        usedBorder.SetResourceReference(Border.BackgroundProperty, "MemoryOccupied");
        Grid.SetColumn(usedBorder, 0);
        bar.Children.Add(usedBorder);

        var freeBorder = new Border();
        freeBorder.SetResourceReference(Border.BackgroundProperty, "MemoryFree");
        Grid.SetColumn(freeBorder, 1);
        bar.Children.Add(freeBorder);

        var overlayText = new TextBlock {
            Text = loadedText ?? "No loaded GPU models reported",
            Margin = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        overlayText.SetResourceReference(TextBlock.ForegroundProperty, "ImportantText");
        overlayText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
        Grid.SetColumnSpan(overlayText, 2);
        bar.Children.Add(overlayText);

        var frame = new Border {
            Child = bar,
            BorderThickness = new Thickness(1)
        };
        frame.SetResourceReference(Border.BorderBrushProperty, "SubtleBorder");
        row.Children.Add(frame);

        return row;
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
                    new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center),
                    new Setter(FrameworkElement.MarginProperty, new Thickness(6, 0, 6, 0))
                }
            }
        };
    }

    private static DataGridTemplateColumn MakeStatusColumn(string header, string statusPath, string textPath, double width) {
        var template = new DataTemplate();
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        text.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 0, 6, 0));
        text.SetBinding(TextBlock.TextProperty, new Binding(textPath));
        text.SetBinding(TextBlock.ForegroundProperty, new Binding(statusPath) {
            Converter = ProbeStatusBrushConverter.Instance
        });
        template.VisualTree = text;

        return new DataGridTemplateColumn {
            Header = header,
            CellTemplate = template,
            Width = width,
            MinWidth = width
        };
    }

    private static DataGridTemplateColumn MakeActionColumn() {
        var template = new DataTemplate();
        var button = new FrameworkElementFactory(typeof(Button));
        button.SetValue(FrameworkElement.HeightProperty, 24.0);
        button.SetValue(FrameworkElement.MinWidthProperty, 70.0);
        button.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 0, 6, 0));
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
        if (_isBusy) {
            _modelsGrid.IsEnabled = false;
            _modelsGrid.IsHitTestVisible = false;
            _useButton.IsEnabled = false;
            _copyAllButton.IsEnabled = false;
            if (_cleanFoundryButton is not null)
                _cleanFoundryButton.IsEnabled = false;
            _closeButton.IsEnabled = false;
            return;
        }

        _modelsGrid.IsEnabled = true;
        _modelsGrid.IsHitTestVisible = true;
        var hasSelection = _modelsGrid.SelectedItem is ModelProviderProbeResult;
        _useButton.IsEnabled = hasSelection;
        _copyAllButton.IsEnabled = _models.Count > 0;
        if (_cleanFoundryButton is not null)
            _cleanFoundryButton.IsEnabled = _loadedFoundryUnloadIds.Count > 0;
        _closeButton.IsEnabled = true;
    }

    protected override void OnClosing(CancelEventArgs e) {
        if (_allowClose) {
            base.OnClosing(e);
            return;
        }

        if (_isBusy && !_closeFinalizationStarted) {
            e.Cancel = true;
            _statusText.Text = "Wait for the current load or probe to finish.";
            return;
        }

        e.Cancel = true;
        BeginCloseWithModelState(null, false);
    }

    private void UseSelectedModel() {
        if (_isBusy) {
            _statusText.Text = "Wait for the current load or probe to finish.";
            return;
        }

        if (_modelsGrid.SelectedItem is not ModelProviderProbeResult selected)
            return;

        SelectedModelId = selected.ModelId;
        BeginCloseWithModelState(selected.ModelId, true);
    }

    private void ExecuteRowAction(ModelProviderProbeResult selected) {
        if (_isBusy) {
            _statusText.Text = "Wait for the current load or probe to finish.";
            return;
        }

        _modelsGrid.SelectedItem = selected;
        if (selected.RowActionText == "Load" && _canLoadFoundryModels) {
            _ = LoadModelAsync(selected, probeAfterLoad: selected.ChatStatus == ModelProbeCheckStatus.NotLoaded ||
                                                    selected.ToolStatus == ModelProbeCheckStatus.NotLoaded);
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
        var ticker = StartStatusTicker(index, "Probing");
        try {
            _models[index] = selected with { Notes = "Probing..." };
            _modelsGrid.SelectedIndex = index;
            var probed = await _probeService.RunLiveProbeAsync(_providerUrl, _apiKey, selected);
            ticker.Stop();
            _models[index] = probed;
            _modelsGrid.SelectedItem = probed;
            _statusText.Text = $"Live probe complete for {selected.ModelId}.";
        }
        catch (Exception ex) {
            ticker.Stop();
            _models[index] = selected with {
                ChatStatus = ModelProbeCheckStatus.Failed,
                ToolStatus = ModelProbeCheckStatus.Failed,
                Notes = ex.Message
            };
            _modelsGrid.SelectedIndex = index;
            _statusText.Text = $"Live probe failed: {ex.Message}";
        }
        finally {
            SetActionButtonsEnabled(true);
        }
    }

    private async Task LoadModelAsync(ModelProviderProbeResult selected, bool probeAfterLoad = false) {
        if (!_canLoadFoundryModels) {
            ShowDetails(selected);
            return;
        }

        var index = _models.IndexOf(selected);
        if (index < 0)
            return;

        SetActionButtonsEnabled(false);
        _statusText.Text = $"Loading {selected.ModelId} with Foundry...";
        var ticker = StartStatusTicker(index, "Loading");
        try {
            _models[index] = selected with { Notes = "Loading..." };
            _modelsGrid.SelectedIndex = index;
            var unloadNote = await UnloadCurrentFoundryModelBeforeLoadAsync(selected.ModelId);
            var result = await _probeService.LoadFoundryModelAsync(selected.ModelId);
            ticker.Stop();
            var diagnosticNote = result.Success
                ? BuildLoadNote("Load succeeded", result)
                : BuildLoadNote("Load failed", result);
            diagnosticNote = AppendDiagnosticNote(unloadNote, diagnosticNote);
            var displayNote = result.Success
                ? "Load succeeded."
                : $"Load failed: exit {result.ExitCode}.";

            var source = result.Success
                ? selected.WithoutStaleNotLoadedNotes()
                : selected;
            var loaded = source with {
                ChatStatus = result.Success && selected.ChatStatus == ModelProbeCheckStatus.NotLoaded
                    ? ModelProbeCheckStatus.NotRun
                    : selected.ChatStatus,
                ToolStatus = result.Success && selected.ToolStatus == ModelProbeCheckStatus.NotLoaded
                    ? ModelProbeCheckStatus.NotRun
                    : selected.ToolStatus,
                Notes = AppendNote(source.Notes, displayNote),
                DiagnosticNotes = AppendDiagnosticNote(source.DiagnosticNotes, diagnosticNote),
                CanLoadLocally = _canLoadFoundryModels,
                IsLoadedLocally = result.Success
            };
            if (result.Success)
                MarkAllLocalModelsUnloadedExcept(selected.ModelId);
            _models[index] = loaded;
            _modelsGrid.SelectedIndex = index;
            if (result.Success) {
                _loadedFoundryModelId = selected.ModelId;
                _loadedFoundryModelIds.Clear();
                _loadedFoundryModelIds.Add(selected.ModelId);
                if (!string.IsNullOrWhiteSpace(selected.ParentModel))
                    _loadedFoundryModelIds.Add(selected.ParentModel);
                _loadedFoundryUnloadIds.Clear();
                _loadedFoundryUnloadIds.Add(selected.ModelId);
                await RefreshMemoryPanelAsync();
            }
            _statusText.Text = result.Success
                ? probeAfterLoad
                    ? $"Loaded {selected.ModelId}. Running live probe..."
                    : $"Loaded {selected.ModelId}. Run Live Probe to test it."
                : $"Load failed for {selected.ModelId}.";
            if (result.Success && probeAfterLoad) {
                await RunLiveProbeAsync(loaded);
            }
        }
        catch (OperationCanceledException) {
            ticker.Stop();
            _models[index] = selected with {
                Notes = AppendNote(selected.Notes, "Load canceled.")
            };
            _modelsGrid.SelectedIndex = index;
            _statusText.Text = $"Load canceled for {selected.ModelId}.";
        }
        catch (Exception ex) {
            ticker.Stop();
            _models[index] = selected with {
                Notes = AppendNote(selected.Notes, "Load failed."),
                DiagnosticNotes = AppendDiagnosticNote(selected.DiagnosticNotes, $"Load failed: {ex.Message}")
            };
            _modelsGrid.SelectedIndex = index;
            _statusText.Text = $"Load failed: {ex.Message}";
        }
        finally {
            SetActionButtonsEnabled(true);
        }
    }

    private async Task<string?> UnloadCurrentFoundryModelBeforeLoadAsync(string nextModelId) {
        if (_loadedFoundryUnloadIds.Count == 0)
            return null;

        var notes = new List<string>();
        foreach (var previousModelId in _loadedFoundryUnloadIds
                     .Where(modelId => !string.Equals(modelId, nextModelId, StringComparison.OrdinalIgnoreCase))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .ToArray()) {
            _statusText.Text = $"Unloading {previousModelId} before loading {nextModelId}...";
            var result = await _probeService.UnloadFoundryModelAsync(previousModelId);
            notes.Add(BuildLoadNote(result.Success ? $"Unloaded {previousModelId}" : $"Unload {previousModelId} failed", result));
            if (result.Success) {
                _loadedFoundryUnloadIds.Remove(previousModelId);
                _loadedFoundryModelIds.Remove(previousModelId);
                MarkMatchingLocalModelLoaded(previousModelId, false);
            }
        }

        if (_loadedFoundryUnloadIds.Count == 0) {
            _loadedFoundryModelId = null;
            _loadedFoundryModelIds.Clear();
        }

        return notes.Count == 0
            ? null
            : string.Join(Environment.NewLine, notes);
    }

    private void BeginCloseWithModelState(string? desiredModelId, bool dialogResult) {
        if (_closeFinalizationStarted)
            return;

        _closeFinalizationStarted = true;
        _desiredModelIdOnClose = NormalizeModelId(desiredModelId) ?? _initialModelId;
        _finalDialogResult = dialogResult;
        _ = CloseWithModelStateAsync();
    }

    private async Task CloseWithModelStateAsync() {
        SetActionButtonsEnabled(false);
        _useButton.IsEnabled = false;
        var canClose = true;

        try {
            if (!_canLoadFoundryModels)
                return;

            var desiredModelId = _desiredModelIdOnClose;
            if (!string.IsNullOrWhiteSpace(_loadedFoundryModelId) &&
                !string.Equals(_loadedFoundryModelId, desiredModelId, StringComparison.OrdinalIgnoreCase)) {
                var temporaryModelId = _loadedFoundryModelId;
                _statusText.Text = $"Unloading temporary model {temporaryModelId}...";
                var unload = await _probeService.UnloadFoundryModelAsync(temporaryModelId);
                if (unload.Success) {
                    _loadedFoundryModelId = null;
                    _loadedFoundryUnloadIds.Remove(temporaryModelId);
                    _loadedFoundryModelIds.Remove(temporaryModelId);
                }
                else {
                    _statusText.Text = $"Could not unload temporary model {temporaryModelId}. Open Details for the model before closing.";
                    canClose = false;
                }
            }

            if (canClose &&
                !string.IsNullOrWhiteSpace(desiredModelId) &&
                !IsFoundryModelKnownLoaded(desiredModelId)) {
                _statusText.Text = $"Restoring selected model {desiredModelId}...";
                var restore = await _probeService.LoadFoundryModelAsync(desiredModelId);
                if (restore.Success) {
                    _loadedFoundryModelId = desiredModelId;
                    _loadedFoundryUnloadIds.Clear();
                    _loadedFoundryUnloadIds.Add(desiredModelId);
                    _loadedFoundryModelIds.Clear();
                    _loadedFoundryModelIds.Add(desiredModelId);
                }
                else {
                    _statusText.Text = $"Could not restore selected model {desiredModelId}.";
                    canClose = false;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            _statusText.Text = $"Model cleanup before close failed: {ex.Message}";
            canClose = false;
        }
        finally {
            if (canClose) {
                _allowClose = true;
                if (_finalDialogResult == true)
                    DialogResult = true;
                else
                    Close();
            }
            else {
                _closeFinalizationStarted = false;
                SetActionButtonsEnabled(true);
            }
        }
    }

    private void ShowDetails(ModelProviderProbeResult selected) {
        var window = new ModelProviderProbeNoteWindow(_providerUrl, selected) {
            Owner = this
        };
        window.ShowDialog();
    }

    private async Task UnloadLoadedFoundryModelsAsync() {
        if (!_canLoadFoundryModels || _loadedFoundryUnloadIds.Count == 0)
            return;

        SetActionButtonsEnabled(false);
        _statusText.Text = "Unloading loaded Foundry models...";
        var failures = new List<string>();
        foreach (var modelId in _loadedFoundryUnloadIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()) {
            _statusText.Text = $"Unloading {modelId}...";
            var result = await _probeService.UnloadFoundryModelAsync(modelId);
            if (result.Success) {
                _loadedFoundryUnloadIds.Remove(modelId);
                _loadedFoundryModelIds.Remove(modelId);
                MarkMatchingLocalModelLoaded(modelId, false);
            }
            else {
                failures.Add(BuildLoadNote($"Unload {modelId} failed", result));
            }
        }

        if (_loadedFoundryUnloadIds.Count == 0)
            _loadedFoundryModelId = null;
        if (_loadedFoundryUnloadIds.Count == 0)
            _loadedFoundryModelIds.Clear();

        _statusText.Text = failures.Count == 0
            ? "Loaded Foundry models were unloaded."
            : $"Could not unload {failures.Count} model(s). Open Details for diagnostics.";
        if (failures.Count > 0)
            AppendDiagnosticsToLoadedRows(string.Join(Environment.NewLine, failures));
        await RefreshMemoryPanelAsync();
        SetActionButtonsEnabled(true);
    }

    private async Task RefreshMemoryPanelAsync() {
        if (_memoryPanel is null)
            return;

        IReadOnlyList<FoundryLoadedModel> loadedModels;
        try {
            loadedModels = await _probeService.ListLoadedFoundryModelsAsync();
        }
        catch {
            loadedModels = Array.Empty<FoundryLoadedModel>();
        }

        IReadOnlyList<LocalGpuMemoryInfo> gpuMemory;
        try {
            gpuMemory = await _probeService.GetNvidiaGpuMemoryAsync();
        }
        catch {
            gpuMemory = Array.Empty<LocalGpuMemoryInfo>();
        }

        if (gpuMemory.Count > 0)
            PopulateMemoryPanel(_memoryPanel, new ModelProviderLocalStatus(loadedModels, gpuMemory));
    }

    private DispatcherTimer StartStatusTicker(int index, string label) {
        var started = DateTimeOffset.UtcNow;
        var timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) {
            Interval = TimeSpan.FromSeconds(1)
        };
        timer.Tick += (_, _) => UpdateTimedStatusNote(index, label, started);
        timer.Start();
        return timer;
    }

    private void UpdateTimedStatusNote(int index, string label, DateTimeOffset started) {
        if (index < 0 || index >= _models.Count)
            return;

        var seconds = Math.Max(1, (int)Math.Floor((DateTimeOffset.UtcNow - started).TotalSeconds));
        _models[index] = _models[index] with { Notes = $"{label}... ({seconds}s)" };
        _modelsGrid.SelectedIndex = index;
    }

    private void CopyAllDetails() {
        if (_isBusy) {
            _statusText.Text = "Wait for the current load or probe to finish.";
            return;
        }

        if (_models.Count == 0) {
            _statusText.Text = "No model details to copy.";
            return;
        }

        var details = _models
            .Select(model =>
                $"```{Environment.NewLine}{ModelProviderProbeNoteWindow.BuildDiagnosticText(_providerUrl, model)}{Environment.NewLine}```")
            .ToArray();
        Clipboard.SetText(string.Join($"{Environment.NewLine}{Environment.NewLine}", details));
        _statusText.Text = $"Copied details for {_models.Count} model(s).";
    }

    private void SetActionButtonsEnabled(bool enabled) {
        _isBusy = !enabled;
        _modelsGrid.IsEnabled = enabled;
        _modelsGrid.IsHitTestVisible = enabled;
        _closeButton.IsEnabled = enabled;
        _useButton.IsEnabled = enabled && _modelsGrid.SelectedItem is ModelProviderProbeResult;
        _copyAllButton.IsEnabled = enabled && _models.Count > 0;
        if (_cleanFoundryButton is not null)
            _cleanFoundryButton.IsEnabled = enabled && _loadedFoundryUnloadIds.Count > 0;
    }

    private bool IsFoundryModelKnownLoaded(string modelId) =>
        _loadedFoundryModelIds.Contains(modelId) ||
        _loadedFoundryUnloadIds.Contains(modelId);

    private void MarkAllLocalModelsUnloadedExcept(string modelId) {
        for (var i = 0; i < _models.Count; i++) {
            var model = _models[i];
            if (!model.CanLoadLocally || string.Equals(model.ModelId, modelId, StringComparison.OrdinalIgnoreCase))
                continue;

            _models[i] = model with { IsLoadedLocally = false };
        }
    }

    private void MarkMatchingLocalModelLoaded(string modelId, bool isLoaded) {
        for (var i = 0; i < _models.Count; i++) {
            var model = _models[i];
            if (!model.CanLoadLocally)
                continue;

            if (string.Equals(model.ModelId, modelId, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(model.ParentModel) &&
                 string.Equals(model.ParentModel, modelId, StringComparison.OrdinalIgnoreCase))) {
                _models[i] = model with { IsLoadedLocally = isLoaded };
            }
        }
    }

    private void AppendDiagnosticsToLoadedRows(string diagnostic) {
        for (var i = 0; i < _models.Count; i++) {
            var model = _models[i];
            if (!model.IsLoadedLocally)
                continue;

            _models[i] = model with {
                DiagnosticNotes = AppendDiagnosticNote(model.DiagnosticNotes, diagnostic)
            };
        }
    }

    private static string BuildLoadNote(string prefix, ModelProviderCommandResult result) {
        var detail = CombineCommandOutput(result.Output, result.Error);
        var suffix = string.IsNullOrWhiteSpace(detail)
            ? $"exit={result.ExitCode}"
            : $"{detail} exit={result.ExitCode}";
        return $"{prefix}: {suffix}";
    }

    private static string? CombineCommandOutput(string? output, string? error) {
        var parts = new[] { output, error }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .ToArray();
        return parts.Length == 0
            ? null
            : string.Join(Environment.NewLine, parts);
    }

    private static string AppendNote(string? existing, string note) {
        if (string.IsNullOrWhiteSpace(existing))
            return note;

        return $"{existing} {note}";
    }

    private static string? AppendDiagnosticNote(string? existing, string? note) =>
        ModelProviderProbeResult.AppendDiagnosticNote(existing, note);

    private static string? NormalizeModelId(string? modelId) =>
        string.IsNullOrWhiteSpace(modelId) ? null : modelId.Trim();
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

    internal static string BuildDiagnosticText(string providerUrl, ModelProviderProbeResult result) {
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
            BuildNotesText(result)
        };

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildNotesText(ModelProviderProbeResult result) {
        if (!string.IsNullOrWhiteSpace(result.Notes) &&
            !string.IsNullOrWhiteSpace(result.DiagnosticNotes))
            return $"{result.Notes}{Environment.NewLine}{Environment.NewLine}{result.DiagnosticNotes}";

        if (!string.IsNullOrWhiteSpace(result.DiagnosticNotes))
            return result.DiagnosticNotes;

        return string.IsNullOrWhiteSpace(result.Notes) ? "(none)" : result.Notes!;
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

internal sealed class ProbeStatusBrushConverter : IValueConverter {
    public static readonly ProbeStatusBrushConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
        return value switch {
            ModelProbeCheckStatus.Passed => FindBrush("StatusSuccess", Brushes.DodgerBlue),
            ModelProbeCheckStatus.Failed => FindBrush("StatusFailure", Brushes.IndianRed),
            ModelProbeCheckStatus.TimedOut => FindBrush("StatusFailure", Brushes.IndianRed),
            ModelProbeCheckStatus.NotLoaded => FindBrush("StatusPending", Brushes.MediumPurple),
            _ => FindBrush("LabelText", Brushes.LightGray)
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;

    private static Brush FindBrush(string resourceKey, Brush fallback) =>
        Application.Current.TryFindResource(resourceKey) as Brush ?? fallback;
}
