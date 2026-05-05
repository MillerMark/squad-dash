using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Collections.Generic;
using System.Windows.Media.Imaging;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;

namespace SquadDash;

internal sealed class HireAgentWindow : Window {
    private static readonly string[] CommonRoles = [
        "Backend Engineer",
        "Frontend Engineer",
        "Full-Stack Engineer",
        "Lead Architect",
        "Database Specialist",
        "DevOps Engineer",
        "SRE / Reliability Engineer",
        "Testing / QA Specialist",
        "Security Engineer",
        "Documentation Specialist",
        "SDK / Tooling Engineer",
        "Performance Engineer"
    ];

    private readonly IReadOnlyList<HireUniverseDefinition> _universes;
    private readonly TabControl _universeTabs;
    private readonly ComboBox _roleComboBox;
    private readonly TextBlock _roleHintText;
    private readonly Button _advancedOptionsButton;
    private readonly TextBox _nameBox;
    private readonly BulletedTextBox _bestForBox;
    private readonly BulletedTextBox _avoidBox;
    private readonly BulletedTextBox _whatIOwnBox;
    private readonly TextBox _modelPreferenceBox;
    private readonly TextBlock _modelHintText;
    private readonly TextBox _extraGuidanceBox;
    private readonly StackPanel _advancedOptionsPanel;
    private readonly Border _imageBorder;
    private readonly Image _imagePreview;
    private readonly TextBlock _imagePlaceholderText;
    private readonly Button _hireButton;
    private readonly Action<string, string?> _persistAgentImage;
    private readonly string _roleIconAssetsDirectory;
    private readonly Dictionary<HireAgentOption, Border> _cardBorders = new();
    private HireAgentOption? _selectedTemplate;
    private string? _selectedImagePath;
    private bool _imageIsRoleDefault = true;
    private bool _hasManualReplaceableChanges;
    private bool _suppressManualChangeTracking;
    private bool _suppressTabHandling;
    private bool _advancedOptionsVisible;

    internal sealed record HireAgentSubmission(
        string UniverseName,
        string AgentName,
        string Role,
        IReadOnlyList<string> BestFor,
        IReadOnlyList<string> Avoid,
        IReadOnlyList<string> WhatIOwn,
        string? ModelPreference,
        string? ExtraGuidance,
        string? ImagePath,
        string PromptText);

    internal sealed record HireUniverseDefinition(
        string Name,
        string DisplayName,
        IReadOnlyList<HireAgentOption> AvailableAgents);

    internal sealed class HireAgentOption {
        public HireAgentOption(
            string universeName,
            string name,
            string? role,
            IReadOnlyList<string> bestFor,
            IReadOnlyList<string> avoid,
            string? imagePath,
            ImageSource? imageSource) {
            UniverseName = universeName;
            Name = name;
            Role = role;
            BestFor = bestFor;
            Avoid = avoid;
            ImagePath = imagePath;
            ImageSource = imageSource;
        }

        public string UniverseName { get; }
        public string Name { get; }
        public string? Role { get; }
        public IReadOnlyList<string> BestFor { get; }
        public IReadOnlyList<string> Avoid { get; }
        public string? ImagePath { get; set; }
        public ImageSource? ImageSource { get; set; }
    }

    private sealed record SquadDashRoutingAgent(
        string Name,
        string? Role,
        IReadOnlyList<string> BestFor,
        IReadOnlyList<string> Avoid);

    public HireAgentSubmission? Result { get; private set; }

    private HireAgentWindow(
        IReadOnlyList<HireUniverseDefinition> universes,
        string activeUniverseName,
        string roleIconAssetsDirectory,
        Action<string, string?> persistAgentImage) {
        _universes = universes;
        _persistAgentImage = persistAgentImage;
        _roleIconAssetsDirectory = roleIconAssetsDirectory;

        Title = "Hire a New Agent";
        Width = 1240;
        Height = 860;
        MinWidth = 1080;
        MinHeight = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.SetResourceReference(BackgroundProperty, "AppSurface");

        Resources = new ResourceDictionary();

        var themedTabItemStyle = new Style(typeof(TabItem));
        themedTabItemStyle.Setters.Add(new Setter(Control.ForegroundProperty, TryFindResource("LabelText")));
        themedTabItemStyle.Setters.Add(new Setter(Control.BackgroundProperty, TryFindResource("CardSurface")));
        themedTabItemStyle.Setters.Add(new Setter(Control.BorderBrushProperty, TryFindResource("PanelBorder")));
        themedTabItemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14, 6, 14, 6)));
        themedTabItemStyle.Setters.Add(new Setter(Control.TemplateProperty, BuildTabItemTemplate()));
        Resources.Add(typeof(TabItem), themedTabItemStyle);

        var themedComboItemStyle = new Style(typeof(ComboBoxItem));
        themedComboItemStyle.Setters.Add(new Setter(Control.ForegroundProperty, TryFindResource("LabelText")));
        themedComboItemStyle.Setters.Add(new Setter(Control.BackgroundProperty, TryFindResource("InputSurface")));
        var comboHoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        comboHoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, TryFindResource("RoleBadgeSurface")));
        themedComboItemStyle.Triggers.Add(comboHoverTrigger);
        var comboSelectedTrigger = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        comboSelectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, TryFindResource("AccentFill")));
        themedComboItemStyle.Triggers.Add(comboSelectedTrigger);
        Resources.Add(typeof(ComboBoxItem), themedComboItemStyle);
        Resources[SystemColors.WindowBrushKey] = TryFindResource("InputSurface");
        Resources[SystemColors.WindowTextBrushKey] = TryFindResource("LabelText");
        Resources[SystemColors.ControlBrushKey] = TryFindResource("InputSurface");
        Resources[SystemColors.ControlTextBrushKey] = TryFindResource("LabelText");
        Resources[SystemColors.HighlightBrushKey] = TryFindResource("AccentFill");
        Resources[SystemColors.InactiveSelectionHighlightBrushKey] = TryFindResource("RoleBadgeSurface");
        Resources[SystemColors.HighlightTextBrushKey] = TryFindResource("LabelText");
        Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = TryFindResource("LabelText");
        Resources[SystemColors.ControlDarkBrushKey] = TryFindResource("PanelBorder");
        Resources[SystemColors.ControlDarkDarkBrushKey] = TryFindResource("PanelBorder");

        var rootGrid = new Grid {
            Margin = new Thickness(16)
        };
        rootGrid.SetResourceReference(Panel.BackgroundProperty, "AppSurface");
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.55, GridUnitType.Star) });
        rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Content = rootGrid;

        var leftPanel = new DockPanel();
        Grid.SetRow(leftPanel, 0);
        Grid.SetColumn(leftPanel, 0);
        rootGrid.Children.Add(leftPanel);

        var universeLabel = BuildFieldLabel("Universe:");
        universeLabel.Margin = new Thickness(0, 0, 0, 10);
        DockPanel.SetDock(universeLabel, Dock.Top);
        leftPanel.Children.Add(universeLabel);

        _universeTabs = new TabControl();
        _universeTabs.SetResourceReference(Control.BackgroundProperty, "AppSurface");
        _universeTabs.SetResourceReference(Control.BorderBrushProperty, "PanelBorder");
        _universeTabs.SetResourceReference(Control.ForegroundProperty, "LabelText");
        _universeTabs.SelectionChanged += UniverseTabs_SelectionChanged;
        leftPanel.Children.Add(_universeTabs);

        var previewBorder = new Border {
            Padding = new Thickness(20),
            CornerRadius = new CornerRadius(24),
            MaxWidth = 550
        };
        previewBorder.SetResourceReference(Border.BackgroundProperty, "CardSurface");
        previewBorder.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");
        previewBorder.BorderThickness = new Thickness(1);
        Grid.SetRow(previewBorder, 0);
        Grid.SetColumn(previewBorder, 2);
        rootGrid.Children.Add(previewBorder);

        var previewDock = new DockPanel();
        previewBorder.Child = previewDock;

        var previewTitle = new TextBlock {
            Text = "New Agent",
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 18)
        };
        previewTitle.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        DockPanel.SetDock(previewTitle, Dock.Top);
        previewDock.Children.Add(previewTitle);

        var buttonRow = new StackPanel {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        DockPanel.SetDock(buttonRow, Dock.Bottom);
        previewDock.Children.Add(buttonRow);

        var cancelButton = new Button {
            Content = "Cancel",
            Width = 120,
            Height = 36,
            Margin = new Thickness(0, 0, 10, 0),
            IsCancel = true
        };
        cancelButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        buttonRow.Children.Add(cancelButton);

        _hireButton = new Button {
            Content = "Hire Agent",
            Width = 140,
            Height = 36,
            IsDefault = true
        };
        _hireButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        _hireButton.Click += HireButton_Click;
        buttonRow.Children.Add(_hireButton);

        var previewScroll = new ScrollViewer {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        previewDock.Children.Add(previewScroll);

        var disclaimerText = new TextBlock {
            Text = "Universes and characters are used for internal/reference purposes only and are not affiliated with or endorsed by any rights holders.",
            Margin = new Thickness(0, 18, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Left,
            FontSize = 11
        };
        disclaimerText.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        Grid.SetRow(disclaimerText, 1);
        Grid.SetColumnSpan(disclaimerText, 3);
        rootGrid.Children.Add(disclaimerText);

        var previewGrid = new Grid();
        previewGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        previewGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        previewGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        previewGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        previewGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        previewGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        previewGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        previewGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        previewScroll.Content = previewGrid;

        var roleSection = new StackPanel {
            Margin = new Thickness(0, 0, 0, 16)
        };
        Grid.SetRow(roleSection, 0);
        previewGrid.Children.Add(roleSection);
        roleSection.Children.Add(BuildFieldLabel("Role:"));
        _roleComboBox = new ComboBox {
            IsEditable = true,
            IsTextSearchEnabled = false,
            StaysOpenOnEdit = true,
            Height = 28,
            Margin = new Thickness(0, 6, 0, 0),
            ItemsSource = CommonRoles
        };
        _roleComboBox.Template = BuildComboBoxTemplate();
        _roleComboBox.SetResourceReference(Control.BackgroundProperty, "InputSurface");
        _roleComboBox.SetResourceReference(Control.ForegroundProperty, "LabelText");
        _roleComboBox.SetResourceReference(Control.BorderBrushProperty, "InputBorder");
        _roleComboBox.SelectionChanged += (_, _) => UpdatePreviewState();
        _roleComboBox.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, _) => UpdatePreviewState()));
        roleSection.Children.Add(_roleComboBox);
        _roleHintText = BuildHintText("Enter a role for your new agent");
        roleSection.Children.Add(_roleHintText);

        _nameBox = BuildTextBox();
        WireChangeTracking(_nameBox, tracksReplaceableChanges: true);
        var nameSection = BuildFieldSection("Name", _nameBox);
        Grid.SetRow(nameSection, 2);
        previewGrid.Children.Add(nameSection);

        _advancedOptionsButton = new Button {
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 16),
            Padding = new Thickness(14, 0, 14, 0),
            Content = "Show Advanced Options"
        };
        _advancedOptionsButton.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        _advancedOptionsButton.Click += AdvancedOptionsButton_Click;
        Grid.SetRow(_advancedOptionsButton, 3);
        previewGrid.Children.Add(_advancedOptionsButton);

        _advancedOptionsPanel = new StackPanel {
            Visibility = Visibility.Collapsed
        };
        Grid.SetRow(_advancedOptionsPanel, 4);
        previewGrid.Children.Add(_advancedOptionsPanel);

        _bestForBox = BuildBulletedTextBox();
        WireChangeTracking(_bestForBox);
        _bestForBox.MinHeight = 64;
        _bestForBox.MaxHeight = 84;
        var bestForSection = BuildFieldSection("Best-for", _bestForBox, minHeight: 64);
        _advancedOptionsPanel.Children.Add(bestForSection);

        _avoidBox = BuildBulletedTextBox();
        WireChangeTracking(_avoidBox);
        _avoidBox.MinHeight = 64;
        _avoidBox.MaxHeight = 84;
        var avoidSection = BuildFieldSection("Avoid", _avoidBox, minHeight: 64);
        _advancedOptionsPanel.Children.Add(avoidSection);

        var imageSection = new Grid {
            Margin = new Thickness(0, 0, 0, 16)
        };
        imageSection.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        imageSection.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(imageSection, 1);
        previewGrid.Children.Add(imageSection);

        imageSection.Children.Add(BuildFieldLabel("Image"));
        _imageBorder = new Border {
            Height = 280,
            Margin = new Thickness(0, 6, 0, 0),
            CornerRadius = new CornerRadius(18),
            Cursor = Cursors.Hand,
            ClipToBounds = true,
            VerticalAlignment = VerticalAlignment.Top
        };
        _imageBorder.SetResourceReference(Border.BackgroundProperty, "RoleBadgeSurface");
        _imageBorder.SetResourceReference(Border.BorderBrushProperty, "HireAgentImageBorder");
        _imageBorder.BorderThickness = new Thickness(1);
        _imageBorder.MouseLeftButtonUp += ImageBorder_MouseLeftButtonUp;
        Grid.SetRow(_imageBorder, 1);
        imageSection.Children.Add(_imageBorder);

        var imageGrid = new Grid { MaxWidth = 285 };
        _imageBorder.Child = imageGrid;

        _imagePreview = new Image {
            Stretch = Stretch.Uniform,
            MaxWidth = 285,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        RenderOptions.SetBitmapScalingMode(_imagePreview, BitmapScalingMode.HighQuality);
        imageGrid.Children.Add(_imagePreview);

        _imagePlaceholderText = new TextBlock {
            Text = "click to set agent image (optional)",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };
        _imagePlaceholderText.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        imageGrid.Children.Add(_imagePlaceholderText);

        _whatIOwnBox = BuildBulletedTextBox();
        WireChangeTracking(_whatIOwnBox);
        _whatIOwnBox.MinHeight = 64;
        _whatIOwnBox.MaxHeight = 84;
        var ownSection = BuildFieldSection("What I own:", _whatIOwnBox, minHeight: 64);
        _advancedOptionsPanel.Children.Add(ownSection);

        var modelSection = new StackPanel {
            Margin = new Thickness(0, 0, 0, 16)
        };
        modelSection.Children.Add(BuildFieldLabel("Model Preference:"));
        _modelPreferenceBox = BuildTextBox();
        _modelPreferenceBox.Margin = new Thickness(0, 6, 0, 0);
        WireChangeTracking(_modelPreferenceBox);
        modelSection.Children.Add(_modelPreferenceBox);
        _modelHintText = BuildHintText("Leave empty to use the default model.");
        modelSection.Children.Add(_modelHintText);
        _advancedOptionsPanel.Children.Add(modelSection);

        var extraSection = new StackPanel();
        extraSection.Children.Add(BuildFieldLabel("Optional extra guidance/plugin text"));
        _extraGuidanceBox = BuildTextBox(multiline: true, minHeight: 56);
        _extraGuidanceBox.MaxHeight = 76;
        _extraGuidanceBox.Margin = new Thickness(0, 6, 0, 0);
        WireChangeTracking(_extraGuidanceBox);
        extraSection.Children.Add(_extraGuidanceBox);
        var extraHint = BuildHintText("Optional notes for charter generation, plugin setup, or coordinator handoff.");
        extraHint.Margin = new Thickness(0, 2, 0, 0);
        extraSection.Children.Add(extraHint);
        _advancedOptionsPanel.Children.Add(extraSection);

        PopulateUniverseTabs(activeUniverseName);
        Loaded += HireAgentWindow_Loaded;
        UpdatePreviewState();
    }

    public static HireAgentSubmission? Show(
        Window owner,
        IReadOnlyList<HireUniverseDefinition> universes,
        string activeUniverseName,
        string roleIconAssetsDirectory,
        Action<string, string?> persistAgentImage) {
        var window = new HireAgentWindow(universes, activeUniverseName, roleIconAssetsDirectory, persistAgentImage) {
            Owner = owner
        };

        return window.ShowDialog() == true
            ? window.Result
            : null;
    }

    internal static IReadOnlyList<HireUniverseDefinition> LoadCatalog(
        string? workspaceFolderPath,
        ApplicationSettingsSnapshot settingsSnapshot,
        string agentImageAssetsDirectory,
        IReadOnlyCollection<string> existingTeamNames) {
        var existing = new HashSet<string>(
            existingTeamNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim()),
            StringComparer.OrdinalIgnoreCase);

        var universes = new List<HireUniverseDefinition> {
            BuildSquadDashUniverse(workspaceFolderPath, settingsSnapshot, agentImageAssetsDirectory, existing)
        };
        universes.AddRange(LoadReferenceUniverses(workspaceFolderPath, settingsSnapshot, agentImageAssetsDirectory, existing));
        return universes;
    }

    internal static string ResolveActiveUniverseName(string? workspaceFolderPath) {
        if (string.IsNullOrWhiteSpace(workspaceFolderPath))
            return SquadInstallerService.SquadDashUniverseName;

        var historyPath = Path.Combine(workspaceFolderPath, ".squad", "casting", "history.json");
        if (File.Exists(historyPath)) {
            try {
                using var document = JsonDocument.Parse(File.ReadAllText(historyPath));
                if (document.RootElement.TryGetProperty("universe_usage_history", out var usageHistory) &&
                    usageHistory.ValueKind == JsonValueKind.Array) {
                    var lastUniverse = usageHistory
                        .EnumerateArray()
                        .Select(item => item.TryGetProperty("universe", out var universe) ? universe.GetString() : null)
                        .LastOrDefault(value => !string.IsNullOrWhiteSpace(value));
                    if (!string.IsNullOrWhiteSpace(lastUniverse))
                        return lastUniverse!;
                }
            }
            catch {
            }
        }

        var registryPath = Path.Combine(workspaceFolderPath, ".squad", "casting", "registry.json");
        if (File.Exists(registryPath)) {
            try {
                using var document = JsonDocument.Parse(File.ReadAllText(registryPath));
                if (document.RootElement.TryGetProperty("agents", out var agents) &&
                    agents.ValueKind == JsonValueKind.Object) {
                    foreach (var agent in agents.EnumerateObject()) {
                        if (agent.Value.TryGetProperty("universe", out var universeElement)) {
                            var universe = universeElement.GetString();
                            if (!string.IsNullOrWhiteSpace(universe))
                                return universe!;
                        }
                    }
                }
            }
            catch {
            }
        }

        return SquadInstallerService.SquadDashUniverseName;
    }

    internal static IReadOnlyList<string> BuildImageKeyCandidates(string agentName) {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(agentName))
            candidates.Add(agentName.Trim());

        var normalized = AgentImagePathResolver.NormalizeAssetKeyForLookup(agentName);
        if (!string.IsNullOrWhiteSpace(normalized) &&
            !candidates.Contains(normalized, StringComparer.OrdinalIgnoreCase)) {
            candidates.Add(normalized);
        }

        return candidates;
    }

    private static TextBlock BuildFieldLabel(string text) {
        var block = new TextBlock {
            Text = text,
            FontWeight = FontWeights.SemiBold
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        return block;
    }

    private static TextBlock BuildHintText(string text) {
        var block = new TextBlock {
            Text = text,
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 14)
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        return block;
    }

    private static ControlTemplate BuildTabItemTemplate() {
        var template = new ControlTemplate(typeof(TabItem));

        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "TabBorder";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(12, 12, 0, 0));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.MarginProperty, new Thickness(0, 0, 6, 0));

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        content.SetBinding(ContentPresenter.MarginProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.AppendChild(content);

        template.VisualTree = border;

        var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, Application.Current.TryFindResource("RoleBadgeSurface"), "TabBorder"));
        hoverTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, Application.Current.TryFindResource("AccentFill"), "TabBorder"));
        template.Triggers.Add(hoverTrigger);

        var selectedTrigger = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
        selectedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, Application.Current.TryFindResource("InputSurface"), "TabBorder"));
        selectedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, Application.Current.TryFindResource("AccentFill"), "TabBorder"));
        template.Triggers.Add(selectedTrigger);

        return template;
    }

    private static TextBox BuildTextBox(bool multiline = false, double minHeight = 26) {
        var textBox = new TextBox {
            Height = multiline ? double.NaN : 26,
            MinHeight = minHeight,
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
            Padding = multiline ? new Thickness(10, 8, 10, 8) : new Thickness(10, 3, 10, 3),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        textBox.SetResourceReference(Control.ForegroundProperty, "LabelText");
        textBox.SetResourceReference(Control.BackgroundProperty, "InputSurface");
        textBox.SetResourceReference(Control.BorderBrushProperty, "InputBorder");
        return textBox;
    }

    private static BulletedTextBox BuildBulletedTextBox() {
        var textBox = new BulletedTextBox {
            MinHeight = 96
        };
        textBox.SetResourceReference(Control.ForegroundProperty, "LabelText");
        textBox.SetResourceReference(Control.BackgroundProperty, "InputSurface");
        textBox.SetResourceReference(Control.BorderBrushProperty, "InputBorder");
        return textBox;
    }

    private static FrameworkElement BuildFieldSection(string label, Control control, double minHeight = 30) {
        var stack = new StackPanel {
            Margin = new Thickness(0, 0, 0, 16)
        };
        stack.Children.Add(BuildFieldLabel(label));
        if (control.Margin == default)
            control.Margin = new Thickness(0, 6, 0, 0);
        if (control.MinHeight < minHeight)
            control.MinHeight = minHeight;
        stack.Children.Add(control);
        return stack;
    }

    private static ControlTemplate BuildComboBoxTemplate() {
        var template = new ControlTemplate(typeof(ComboBox));

        var root = new FrameworkElementFactory(typeof(Grid));
        root.SetValue(FrameworkElement.SnapsToDevicePixelsProperty, true);

        var textColumn = new FrameworkElementFactory(typeof(ColumnDefinition));
        textColumn.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
        var buttonColumn = new FrameworkElementFactory(typeof(ColumnDefinition));
        buttonColumn.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
        root.AppendChild(textColumn);
        root.AppendChild(buttonColumn);

        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "OuterBorder";
        border.SetValue(Grid.ColumnSpanProperty, 2);
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        root.AppendChild(border);

        var editableTextBox = new FrameworkElementFactory(typeof(TextBox));
        editableTextBox.Name = "PART_EditableTextBox";
        editableTextBox.SetValue(Grid.ColumnProperty, 0);
        editableTextBox.SetValue(FrameworkElement.MarginProperty, new Thickness(1, 1, 0, 1));
        editableTextBox.SetValue(Control.PaddingProperty, new Thickness(10, 3, 6, 3));
        editableTextBox.SetValue(Control.BorderThicknessProperty, new Thickness(0));
        editableTextBox.SetValue(Control.BackgroundProperty, Brushes.Transparent);
        editableTextBox.SetValue(Control.ForegroundProperty, Application.Current.TryFindResource("LabelText"));
        editableTextBox.SetValue(TextBoxBase.SelectionBrushProperty, Application.Current.TryFindResource("AccentFill"));
        editableTextBox.SetValue(TextBoxBase.SelectionOpacityProperty, 0.35);
        editableTextBox.SetValue(TextBox.VerticalContentAlignmentProperty, VerticalAlignment.Center);
        editableTextBox.SetValue(TextBox.HorizontalContentAlignmentProperty, HorizontalAlignment.Left);
        editableTextBox.SetValue(UIElement.FocusableProperty, true);
        editableTextBox.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        editableTextBox.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        editableTextBox.SetBinding(TextBox.IsReadOnlyProperty, new System.Windows.Data.Binding("IsReadOnly") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        root.AppendChild(editableTextBox);

        var dropDownButton = new FrameworkElementFactory(typeof(ToggleButton));
        dropDownButton.Name = "DropDownToggle";
        dropDownButton.SetValue(Grid.ColumnProperty, 1);
        dropDownButton.SetValue(FrameworkElement.WidthProperty, 30.0);
        dropDownButton.SetValue(Control.BackgroundProperty, Brushes.Transparent);
        dropDownButton.SetValue(Control.BorderThicknessProperty, new Thickness(0));
        dropDownButton.SetValue(UIElement.FocusableProperty, false);
        dropDownButton.SetBinding(ToggleButton.IsCheckedProperty, new System.Windows.Data.Binding("IsDropDownOpen") {
            RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent,
            Mode = System.Windows.Data.BindingMode.TwoWay
        });

        var arrow = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
        arrow.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M 0 0 L 4 4 L 8 0"));
        arrow.SetValue(System.Windows.Shapes.Path.StretchProperty, Stretch.None);
        arrow.SetValue(System.Windows.Shapes.Path.StrokeThicknessProperty, 1.75);
        arrow.SetValue(System.Windows.Shapes.Path.StrokeStartLineCapProperty, PenLineCap.Round);
        arrow.SetValue(System.Windows.Shapes.Path.StrokeEndLineCapProperty, PenLineCap.Round);
        arrow.SetValue(System.Windows.Shapes.Path.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        arrow.SetValue(System.Windows.Shapes.Path.VerticalAlignmentProperty, VerticalAlignment.Center);
        arrow.SetValue(System.Windows.Shapes.Path.StrokeProperty, Application.Current.TryFindResource("BodyText"));
        dropDownButton.AppendChild(arrow);

        // Replace the default ToggleButton chrome (which paints Windows-system blue on
        // IsMouseOver) with a minimal flat template: transparent at rest, HoverSurface tint
        // on hover.  CornerRadius(0,10,10,0) matches the right side of OuterBorder so the
        // hover tint never bleeds outside the rounded combo-box boundary.
        var toggleTemplate = new ControlTemplate(typeof(ToggleButton));
        var toggleBorder = new FrameworkElementFactory(typeof(Border));
        toggleBorder.Name = "ToggleBorder";
        toggleBorder.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        toggleBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(0, 10, 10, 0));
        var toggleContent = new FrameworkElementFactory(typeof(ContentPresenter));
        toggleContent.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        toggleContent.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        toggleBorder.AppendChild(toggleContent);
        toggleTemplate.VisualTree = toggleBorder;
        var toggleHoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        toggleHoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
            Application.Current.TryFindResource("HoverSurface"), "ToggleBorder"));
        toggleTemplate.Triggers.Add(toggleHoverTrigger);
        dropDownButton.SetValue(Control.TemplateProperty, toggleTemplate);

        root.AppendChild(dropDownButton);

        var popup = new FrameworkElementFactory(typeof(Popup));
        popup.Name = "PART_Popup";
        popup.SetValue(Popup.AllowsTransparencyProperty, true);
        popup.SetValue(Popup.PopupAnimationProperty, PopupAnimation.Fade);
        popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
        popup.SetValue(Popup.FocusableProperty, false);
        popup.SetBinding(Popup.IsOpenProperty, new System.Windows.Data.Binding("IsDropDownOpen") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        popup.SetBinding(Popup.PlacementTargetProperty, new System.Windows.Data.Binding() { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });

        var popupBorder = new FrameworkElementFactory(typeof(Border));
        popupBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
        popupBorder.SetValue(Border.MarginProperty, new Thickness(0, 4, 0, 0));
        popupBorder.SetValue(Border.PaddingProperty, new Thickness(4));
        popupBorder.SetValue(Border.MinWidthProperty, 160.0);
        popupBorder.SetValue(Border.SnapsToDevicePixelsProperty, true);
        popupBorder.SetValue(Border.BackgroundProperty, Application.Current.TryFindResource("InputSurface"));
        popupBorder.SetValue(Border.BorderBrushProperty, Application.Current.TryFindResource("InputBorder"));
        popupBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));

        var scrollViewer = new FrameworkElementFactory(typeof(ScrollViewer));
        scrollViewer.SetValue(ScrollViewer.CanContentScrollProperty, true);
        scrollViewer.SetValue(FrameworkElement.MaxHeightProperty, 260.0);

        var itemsPresenter = new FrameworkElementFactory(typeof(ItemsPresenter));
        scrollViewer.AppendChild(itemsPresenter);
        popupBorder.AppendChild(scrollViewer);
        popup.AppendChild(popupBorder);
        root.AppendChild(popup);

        template.VisualTree = root;

        var editableTrigger = new Trigger { Property = ComboBox.IsEditableProperty, Value = false };
        editableTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed, "PART_EditableTextBox"));
        template.Triggers.Add(editableTrigger);

        var focusTrigger = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
        focusTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, Application.Current.TryFindResource("AccentFill"), "OuterBorder"));
        template.Triggers.Add(focusTrigger);

        var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, Application.Current.TryFindResource("RoleBadgeSurface"), "OuterBorder"));
        template.Triggers.Add(hoverTrigger);

        return template;
    }

    private static ControlTemplate BuildFlatButtonTemplate() {
        var template = new ControlTemplate(typeof(Button));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Stretch);
        template.VisualTree = presenter;
        return template;
    }

    private static BitmapImage? LoadBitmap(string? path) {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch {
            return null;
        }
    }

    private void WireChangeTracking(Control control, bool tracksReplaceableChanges = false) {
        if (control is TextBox textBox) {
            textBox.TextChanged += (_, _) => {
                if (tracksReplaceableChanges)
                    MarkManualReplaceableChange();
                else
                    UpdatePreviewState();
            };
        }
    }

    private void MarkManualReplaceableChange() {
        if (_suppressManualChangeTracking)
            return;

        _hasManualReplaceableChanges = true;
        UpdatePreviewState();
    }

    private void RunWithoutManualChangeTracking(Action action) {
        var previous = _suppressManualChangeTracking;
        _suppressManualChangeTracking = true;
        try {
            action();
        }
        finally {
            _suppressManualChangeTracking = previous;
        }
    }

    private void PopulateUniverseTabs(string activeUniverseName) {
        foreach (var universe in _universes) {
            var tab = new TabItem {
                Header = universe.DisplayName,
                Tag = universe.Name,
                Content = BuildUniverseContent(universe)
            };
            _universeTabs.Items.Add(tab);
        }

        var target = _universeTabs.Items
            .OfType<TabItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, activeUniverseName, StringComparison.OrdinalIgnoreCase))
            ?? _universeTabs.Items.OfType<TabItem>().FirstOrDefault();
        if (target is not null)
            _universeTabs.SelectedItem = target;
    }

    private UIElement BuildUniverseContent(HireUniverseDefinition universe) {
        if (universe.AvailableAgents.Count == 0) {
            var emptyHost = new Grid();
            var textBlock = new TextBlock {
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                MaxWidth = 360
            };
            textBlock.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
            textBlock.Inlines.Add(new Run("No more agents available from this universe. Select from a different universe or "));
            var link = new Hyperlink(new Run("create a custom agent"));
            link.Click += (_, _) => CreateCustomAgent_Click(universe.Name);
            textBlock.Inlines.Add(link);
            textBlock.Inlines.Add(new Run("."));
            emptyHost.Children.Add(textBlock);
            return emptyHost;
        }

        var scrollViewer = new ScrollViewer {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var wrapPanel = new WrapPanel {
            Margin = new Thickness(4, 8, 0, 0),
            ItemWidth = 176
        };
        scrollViewer.Content = wrapPanel;

        foreach (var agent in universe.AvailableAgents)
            wrapPanel.Children.Add(BuildAgentCard(agent));

        return scrollViewer;
    }

    private UIElement BuildAgentCard(HireAgentOption option) {
        var border = new Border {
            Width = 170,
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 12, 12),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(18),
            Cursor = Cursors.Hand
        };
        border.SetResourceReference(Border.BackgroundProperty, "CardSurface");
        border.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");
        _cardBorders[option] = border;
        border.Child = BuildAgentCardContent(option);

        var button = new Button {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            FocusVisualStyle = null,
            Template = BuildFlatButtonTemplate(),
            Content = border,
            Tag = option
        };
        button.Click += AgentCardButton_Click;
        button.ContextMenu = BuildAgentCardContextMenu(option);
        return button;
    }

    private UIElement BuildAgentCardContent(HireAgentOption option) {
        var stack = new StackPanel();

        if (option.ImageSource is not null) {
            var imageBorder = new Border {
                Height = 100,
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 10),
                ClipToBounds = true
            };
            var image = new Image {
                Source = option.ImageSource,
                Stretch = Stretch.UniformToFill
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            imageBorder.Child = image;
            stack.Children.Add(imageBorder);
        }
        else {
            var avatar = new Border {
                Width = 58,
                Height = 58,
                CornerRadius = new CornerRadius(29),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10),
                BorderThickness = new Thickness(1)
            };
            avatar.SetResourceReference(Border.BackgroundProperty, "RoleBadgeSurface");
            avatar.SetResourceReference(Border.BorderBrushProperty, "BadgeBorder");
            avatar.Child = new TextBlock {
                Text = string.IsNullOrWhiteSpace(option.Name) ? "?" : option.Name.Trim()[..1].ToUpperInvariant(),
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            stack.Children.Add(avatar);
        }

        var nameBlock = new TextBlock {
            Text = option.Name,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        nameBlock.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        stack.Children.Add(nameBlock);

        if (!string.IsNullOrWhiteSpace(option.Role)) {
            var roleBlock = new TextBlock {
                Text = option.Role,
                Margin = new Thickness(0, 6, 0, 0),
                FontSize = 11,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            roleBlock.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
            stack.Children.Add(roleBlock);
        }

        return stack;
    }

    private ContextMenu BuildAgentCardContextMenu(HireAgentOption option) {
        var menu = new ContextMenu();
        menu.SetResourceReference(ContextMenu.StyleProperty, "ThemedContextMenuStyle");
        var setImage = new MenuItem { Header = "Set agent image..." };
        setImage.SetResourceReference(MenuItem.StyleProperty, "ThemedMenuItemStyle");
        setImage.Click += (_, _) => SetAgentImage(option);
        menu.Items.Add(setImage);
        return menu;
    }

    private void HireAgentWindow_Loaded(object? sender, RoutedEventArgs e) {
        _roleComboBox.Focus();
        if (_roleComboBox.Template.FindName("PART_EditableTextBox", _roleComboBox) is TextBox editable) {
            editable.SetResourceReference(Control.ForegroundProperty, "LabelText");
            editable.SetResourceReference(Control.BackgroundProperty, "InputSurface");
            editable.SetResourceReference(Control.BorderBrushProperty, "InputBorder");
            editable.Focus();
        }
    }

    private void UniverseTabs_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        if (_suppressTabHandling || !ReferenceEquals(sender, _universeTabs))
            return;

        if (_universeTabs.SelectedItem is not TabItem selectedTab)
            return;

        if (_selectedTemplate is not null &&
            !string.Equals(_selectedTemplate.UniverseName, selectedTab.Tag as string, StringComparison.OrdinalIgnoreCase)) {
            ClearSelectedTemplate(clearTemplateFields: false);
        }
    }

    private void AgentCardButton_Click(object sender, RoutedEventArgs e) {
        if (sender is not Button { Tag: HireAgentOption option })
            return;

        if (!ReferenceEquals(_selectedTemplate, option) && _hasManualReplaceableChanges) {
            var replace = MessageBox.Show(
                this,
                "Replace current agent?",
                "Hire Agent",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            _hasManualReplaceableChanges = false;
            if (replace != MessageBoxResult.Yes)
                return;
        }

        ApplyTemplate(option);
    }

    private void ApplyTemplate(HireAgentOption option) {
        RunWithoutManualChangeTracking(() => {
            _selectedTemplate = option;
            _nameBox.Text = option.Name;
            _selectedImagePath = option.ImagePath;
            _imagePreview.Source = option.ImageSource;
            _imageIsRoleDefault = option.ImagePath is null;
        });
        _hasManualReplaceableChanges = false;
        UpdateSelectionVisuals();
        UpdatePreviewState();
    }

    private void ClearSelectedTemplate(bool clearTemplateFields) {
        RunWithoutManualChangeTracking(() => {
            _selectedTemplate = null;
            if (clearTemplateFields) {
                _nameBox.Clear();
                _selectedImagePath = null;
                _imagePreview.Source = null;
                _imageIsRoleDefault = true;
            }
        });

        _hasManualReplaceableChanges = false;
        UpdateSelectionVisuals();
        UpdatePreviewState();
    }

    private void UpdateSelectionVisuals() {
        foreach (var pair in _cardBorders) {
            if (ReferenceEquals(pair.Key, _selectedTemplate))
                pair.Value.SetResourceReference(Border.BorderBrushProperty, "AccentFill");
            else
                pair.Value.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");
        }
    }

    private void CreateCustomAgent_Click(string universeName) {
        if (_universeTabs.Items.OfType<TabItem>().FirstOrDefault(item =>
                string.Equals(item.Tag as string, universeName, StringComparison.OrdinalIgnoreCase)) is { } tab) {
            _suppressTabHandling = true;
            _universeTabs.SelectedItem = tab;
            _suppressTabHandling = false;
        }

        ClearSelectedTemplate(clearTemplateFields: true);
        _nameBox.Focus();
    }

    private void SetAgentImage(HireAgentOption option) {
        var dialog = new OpenFileDialog {
            Title = $"Set Image for {option.Name}",
            Filter = "Image files|*.png;*.jpg;*.jpeg;*.jfif;*.bmp;*.webp|All files|*.*"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        option.ImagePath = dialog.FileName;
        option.ImageSource = LoadBitmap(dialog.FileName);
        foreach (var candidate in BuildImageKeyCandidates(option.Name))
            _persistAgentImage(candidate, dialog.FileName);

        if (_cardBorders.TryGetValue(option, out var border))
            border.Child = BuildAgentCardContent(option);

        if (ReferenceEquals(_selectedTemplate, option)) {
            _imageIsRoleDefault = false;
            _selectedImagePath = option.ImagePath;
            _imagePreview.Source = option.ImageSource;
            UpdatePreviewState();
        }
    }

    private void ImageBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
        var dialog = new OpenFileDialog {
            Title = "Select Agent Image",
            Filter = "Image files|*.png;*.jpg;*.jpeg;*.jfif;*.bmp;*.webp|All files|*.*"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        _imageIsRoleDefault = false;
        _selectedImagePath = dialog.FileName;
        _imagePreview.Source = LoadBitmap(_selectedImagePath);
        _hasManualReplaceableChanges = true;
        UpdatePreviewState();
    }

    private void AdvancedOptionsButton_Click(object sender, RoutedEventArgs e) {
        _advancedOptionsVisible = !_advancedOptionsVisible;
        UpdateAdvancedOptionsVisibility();
    }

    private void UpdateAdvancedOptionsVisibility() {
        _advancedOptionsPanel.Visibility = _advancedOptionsVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
        _advancedOptionsButton.Content = _advancedOptionsVisible
            ? "Hide Advanced Options"
            : "Show Advanced Options";
    }

    private void UpdatePreviewState() {
        if (_imageIsRoleDefault) {
            var roleText = GetRoleText();
            var roleIconPath = AgentImagePathResolver.ResolveRoleIconPath(_roleIconAssetsDirectory, string.Empty, roleText);
            var isSpecificRoleIcon = !string.IsNullOrWhiteSpace(roleText) &&
                                     !roleIconPath.EndsWith("GenericAgent.png", StringComparison.OrdinalIgnoreCase);
            _imagePreview.Source = isSpecificRoleIcon && File.Exists(roleIconPath)
                ? LoadBitmap(roleIconPath)
                : null;
        }

        _roleHintText.Visibility = string.IsNullOrWhiteSpace(GetRoleText())
            ? Visibility.Visible
            : Visibility.Collapsed;
        _modelHintText.Visibility = string.IsNullOrWhiteSpace(_modelPreferenceBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        _imageBorder.Height = _imagePreview.Source is null ? 56 : 280;
        _imagePlaceholderText.Visibility = _imagePreview.Source is null
            ? Visibility.Visible
            : Visibility.Collapsed;
        _hireButton.IsEnabled = !string.IsNullOrWhiteSpace(GetRoleText()) &&
                                !string.IsNullOrWhiteSpace(_nameBox.Text);
    }

    private string GetRoleText() =>
        _roleComboBox.Text?.Trim() ?? string.Empty;

    private void HireButton_Click(object sender, RoutedEventArgs e) {
        var role = GetRoleText();
        var name = _nameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(role)) {
            MessageBox.Show(this, "Enter a role for the new agent.", "Hire Agent", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(name)) {
            MessageBox.Show(this, "Select an available agent or enter a name.", "Hire Agent", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var activeUniverse = (_universeTabs.SelectedItem as TabItem)?.Tag as string
            ?? _selectedTemplate?.UniverseName
            ?? SquadInstallerService.SquadDashUniverseName;
        var bestFor = _bestForBox.GetBulletItems();
        var avoid = _avoidBox.GetBulletItems();
        var whatIOwn = _whatIOwnBox.GetBulletItems();
        var modelPreference = string.IsNullOrWhiteSpace(_modelPreferenceBox.Text)
            ? null
            : _modelPreferenceBox.Text.Trim();
        var extraGuidance = string.IsNullOrWhiteSpace(_extraGuidanceBox.Text)
            ? null
            : _extraGuidanceBox.Text.Trim();

        Result = new HireAgentSubmission(
            activeUniverse,
            name,
            role,
            bestFor,
            avoid,
            whatIOwn,
            modelPreference,
            extraGuidance,
            _selectedImagePath,
            BuildCoordinatorPrompt(activeUniverse, name, role));

        DialogResult = true;
        Close();
    }

    private string BuildCoordinatorPrompt(string universeName, string name, string role) {
        var builder = new StringBuilder();
        builder.AppendLine("Hire a new team member for this workspace.");
        builder.AppendLine();
        builder.AppendLine($"Preferred universe: {universeName}");
        builder.AppendLine($"Preferred name: {name}");
        builder.AppendLine($"Role: {role}");
        builder.AppendLine();
        builder.AppendLine("If `Preferred universe` names an allowlisted universe, honor that explicit universe choice for this hire even if it differs from the workspace's most recently used universe.");
        builder.AppendLine("Do not block this hire because the SquadDash Universe has a separate capacity limit when the selected universe is different.");
        builder.AppendLine();

        AppendBulletSection(builder, "Best For", _bestForBox.GetBulletItems());
        AppendBulletSection(builder, "Avoid", _avoidBox.GetBulletItems());
        AppendBulletSection(builder, "What I Own", _whatIOwnBox.GetBulletItems());

        if (!string.IsNullOrWhiteSpace(_modelPreferenceBox.Text)) {
            builder.AppendLine("Model Preference:");
            builder.AppendLine($"- {_modelPreferenceBox.Text.Trim()}");
            builder.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(_extraGuidanceBox.Text)) {
            builder.AppendLine("Extra Guidance:");
            builder.AppendLine(_extraGuidanceBox.Text.Trim());
            builder.AppendLine();
        }

        builder.AppendLine("Use Squad's standard add-member workflow so the new hire is created correctly.");
        builder.AppendLine("Generate or refine a high-quality charter.md and history.md for the new agent.");
        builder.AppendLine("Update .squad/team.md, .squad/routing.md, and .squad/casting/registry.json.");
        builder.AppendLine("Prefer role-based collaboration references over hard-coded teammate names.");
        builder.AppendLine("If the requested name conflicts with an existing team member, stop and ask for direction before making changes.");
        return builder.ToString().TrimEnd();
    }

    private static void AppendBulletSection(StringBuilder builder, string title, IReadOnlyList<string> items) {
        if (items.Count == 0)
            return;

        builder.AppendLine($"{title}:");
        foreach (var item in items)
            builder.AppendLine($"- {item}");
        builder.AppendLine();
    }

    private static HireUniverseDefinition BuildSquadDashUniverse(
        string? workspaceFolderPath,
        ApplicationSettingsSnapshot settingsSnapshot,
        string agentImageAssetsDirectory,
        IReadOnlySet<string> existingTeamNames) {
        var content = LoadSquadDashRoutingMd(workspaceFolderPath);
        var agents = ParseSquadDashRouting(content)
            .Where(agent => !existingTeamNames.Contains(agent.Name))
            .OrderBy(agent => agent.Name, StringComparer.OrdinalIgnoreCase)
            .Select(agent => {
                var imagePath = ResolveTemplateImagePath(
                    workspaceFolderPath,
                    settingsSnapshot,
                    agent.Name,
                    agent.Role,
                    agentImageAssetsDirectory);
                return new HireAgentOption(
                    SquadInstallerService.SquadDashUniverseName,
                    agent.Name,
                    agent.Role,
                    agent.BestFor,
                    agent.Avoid,
                    imagePath,
                    LoadBitmap(imagePath));
            })
            .ToArray();

        return new HireUniverseDefinition(
            SquadInstallerService.SquadDashUniverseName,
            "SquadDash",
            agents);
    }

    private static bool ContainsUniverseHeadings(string[] lines) =>
        lines.Any(line => {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("### ", StringComparison.Ordinal))
                return false;
            var heading = trimmed[4..].Trim();
            return heading.LastIndexOf(" (", StringComparison.Ordinal) > 0;
        });

    private static IReadOnlyList<HireUniverseDefinition> LoadReferenceUniverses(
        string? workspaceFolderPath,
        ApplicationSettingsSnapshot settingsSnapshot,
        string agentImageAssetsDirectory,
        IReadOnlySet<string> existingTeamNames) {
        string[]? workspaceLines = null;

        // Try workspace-local casting-reference.md first.
        if (!string.IsNullOrWhiteSpace(workspaceFolderPath)) {
            var referencePath = Path.Combine(workspaceFolderPath, ".squad", "templates", "casting-reference.md");
            if (File.Exists(referencePath))
                workspaceLines = File.ReadAllLines(referencePath);
        }

        // Load the embedded resource (contains the canonical character pools in ### heading format).
        string[]? embeddedLines = null;
        var embedded = SquadInstallerService.LoadEmbeddedCastingReferenceMdPublic();
        if (!string.IsNullOrEmpty(embedded))
            embeddedLines = embedded.Split('\n');

        // Prefer workspace lines, but fall back to embedded if the workspace file either doesn't
        // exist or doesn't contain parseable universe headings (e.g. new table-only format).
        string[] lines;
        if (workspaceLines is not null && ContainsUniverseHeadings(workspaceLines))
            lines = workspaceLines;
        else if (embeddedLines is not null)
            lines = embeddedLines;
        else
            return Array.Empty<HireUniverseDefinition>();

        var universes = new List<HireUniverseDefinition>();

        for (var i = 0; i < lines.Length; i++) {
            var line = lines[i].Trim();
            if (!line.StartsWith("### ", StringComparison.Ordinal))
                continue;

            var heading = line[4..].Trim();
            var marker = heading.LastIndexOf(" (", StringComparison.Ordinal);
            if (marker <= 0)
                continue;

            var universeName = heading[..marker].Trim();
            var namesLine = i + 1 < lines.Length ? lines[i + 1].Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(universeName) ||
                string.IsNullOrWhiteSpace(namesLine) ||
                namesLine.StartsWith("### ", StringComparison.Ordinal)) {
                continue;
            }

            var options = namesLine
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(name => !existingTeamNames.Contains(name))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Select(name => {
                    var imagePath = ResolveTemplateImagePath(
                        workspaceFolderPath,
                        settingsSnapshot,
                        name,
                        null,
                        agentImageAssetsDirectory);
                    return new HireAgentOption(
                        universeName,
                        name,
                        null,
                        Array.Empty<string>(),
                        Array.Empty<string>(),
                        imagePath,
                        LoadBitmap(imagePath));
                })
                .ToArray();

            universes.Add(new HireUniverseDefinition(universeName, universeName, options));
        }

        return universes
            .OrderBy(universe => universe.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string LoadSquadDashRoutingMd(string? workspaceFolderPath) {
        if (!string.IsNullOrWhiteSpace(workspaceFolderPath)) {
            var workspacePath = Path.Combine(workspaceFolderPath, ".squad", "universes", "squaddash.md");
            if (File.Exists(workspacePath))
                return File.ReadAllText(workspacePath);
        }

        return SquadInstallerService.LoadEmbeddedSquadDashMdPublic() ?? string.Empty;
    }

    private static IReadOnlyList<SquadDashRoutingAgent> ParseSquadDashRouting(string content) {
        var lines = content.Split('\n');
        var agents = new List<SquadDashRoutingAgent>();
        for (var i = 0; i < lines.Length; i++) {
            var line = lines[i].Trim();
            if (!line.StartsWith("### ", StringComparison.Ordinal))
                continue;

            var name = line[4..].Trim();
            string? role = null;
            var bestFor = new List<string>();
            var avoid = new List<string>();
            string? currentSection = null;

            for (var j = i + 1; j < lines.Length; j++) {
                var next = lines[j].Trim();
                if (next == "---" || next.StartsWith("### ", StringComparison.Ordinal)) {
                    i = j - 1;
                    break;
                }

                if (next.StartsWith("**Role:**", StringComparison.OrdinalIgnoreCase)) {
                    role = next["**Role:**".Length..].Trim();
                    currentSection = null;
                    continue;
                }

                if (next.StartsWith("**Best For:**", StringComparison.OrdinalIgnoreCase)) {
                    currentSection = "best";
                    continue;
                }

                if (next.StartsWith("**Avoid:**", StringComparison.OrdinalIgnoreCase)) {
                    currentSection = "avoid";
                    continue;
                }

                if (next.StartsWith("**", StringComparison.Ordinal))
                    currentSection = null;

                if (!next.StartsWith("-", StringComparison.Ordinal))
                    continue;

                var item = next[1..].Trim();
                if (currentSection == "best")
                    bestFor.Add(item);
                else if (currentSection == "avoid")
                    avoid.Add(item);
            }

            agents.Add(new SquadDashRoutingAgent(name, role, bestFor.ToArray(), avoid.ToArray()));
        }

        return agents;
    }

    private static string? ResolveTemplateImagePath(
        string? workspaceFolderPath,
        ApplicationSettingsSnapshot settingsSnapshot,
        string agentName,
        string? role,
        string agentImageAssetsDirectory) {
        if (!string.IsNullOrWhiteSpace(workspaceFolderPath) &&
            settingsSnapshot.AgentImagePathsByWorkspace.TryGetValue(workspaceFolderPath, out var workspaceImages)) {
            foreach (var candidate in BuildImageKeyCandidates(agentName)) {
                if (workspaceImages.TryGetValue(candidate, out var imagePath) &&
                    !string.IsNullOrWhiteSpace(imagePath) &&
                    File.Exists(imagePath)) {
                    return imagePath;
                }
            }
        }

        var normalizedName = AgentImagePathResolver.NormalizeAssetKeyForLookup(agentName) ?? agentName;
        return AgentImagePathResolver.ResolveBundledPath(agentImageAssetsDirectory, normalizedName, agentName, role);
    }
}
