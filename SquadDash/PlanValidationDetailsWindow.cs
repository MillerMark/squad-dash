using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace SquadDash;

/// <summary>Standalone master-detail review surface for one durable plan validation result.</summary>
internal sealed class PlanValidationDetailsWindow : ChromedWindow
{
    private static readonly DependencyProperty EnvironmentFontSizeProperty =
        DependencyProperty.Register(nameof(EnvironmentFontSize), typeof(double),
            typeof(PlanValidationDetailsWindow),
            new PropertyMetadata(SystemFonts.MessageFontSize, OnEnvironmentFontSizeChanged));

    private readonly ApplicationSettingsStore _settingsStore;
    private double _fontMultiplier;
    private readonly TextBlock _assertionStatus;
    private readonly TextBlock _assertionText;
    private readonly TextBox _evidenceText;

    internal PlanValidationDetailsWindow(PlanValidationDetailsPresentation presentation,
        ApplicationSettingsStore? settingsStore = null)
        : base(captionHeight: CloseButtonHeight, resizeMode: ResizeMode.CanResize)
    {
        Title = $"Validation Details — {presentation.ValidationTitle}";
        Width = 980;
        Height = 720;
        MinWidth = 720;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = true;
        _settingsStore = settingsStore ?? new ApplicationSettingsStore();
        _fontMultiplier = _settingsStore.Load().ValidationDetailsFontMultiplier;
        SetResourceReference(EnvironmentFontSizeProperty, "FontSizeBody");
        ApplyFontMultiplier();
        PreviewMouseWheel += Window_PreviewMouseWheel;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = BuildHeader(presentation);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var content = new Grid { Margin = new Thickness(16, 8, 16, 16) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var assertionList = new ListBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            ItemContainerStyle = BuildAssertionItemStyle(),
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(assertionList, ScrollBarVisibility.Disabled);
        foreach (var assertion in presentation.Assertions)
            assertionList.Items.Add(BuildAssertionItem(assertion));

        var splitter = new GridSplitter
        {
            Width = 5,
            Margin = new Thickness(14, 0, 14, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
        };
        splitter.SetResourceReference(BackgroundProperty, "PanelBorder");

        var details = new Grid();
        details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        details.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _assertionStatus = new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 2, 0, 8),
        };
        _assertionText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 18),
        };
        _assertionText.SetResourceReference(TextBlock.ForegroundProperty, "ImportantText");
        _evidenceText = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(12),
            BorderThickness = new Thickness(1),
        };
        _evidenceText.SetResourceReference(Control.BackgroundProperty, "CardSurface");
        _evidenceText.SetResourceReference(Control.ForegroundProperty, "ImportantText");
        _evidenceText.SetResourceReference(Control.BorderBrushProperty, "PanelBorder");
        _evidenceText.SetBinding(FontSizeProperty, new Binding(nameof(FontSize)) { Source = this });

        Grid.SetRow(_assertionStatus, 0);
        Grid.SetRow(_assertionText, 1);
        Grid.SetRow(_evidenceText, 2);
        details.Children.Add(_assertionStatus);
        details.Children.Add(_assertionText);
        details.Children.Add(_evidenceText);

        Grid.SetColumn(assertionList, 0);
        Grid.SetColumn(splitter, 1);
        Grid.SetColumn(details, 2);
        content.Children.Add(assertionList);
        content.Children.Add(splitter);
        content.Children.Add(details);
        Grid.SetRow(content, 1);
        root.Children.Add(content);

        assertionList.SelectionChanged += (_, _) =>
        {
            if (assertionList.SelectedItem is ListBoxItem { Tag: PlanValidationAssertionDetails selected })
                ShowAssertion(selected);
        };
        assertionList.SelectedIndex = Math.Max(
            0,
            presentation.Assertions.ToList().FindIndex(assertion => assertion.Passed == false));

        ApplyOuterBorder("AppSurface", Title).Child = root;
        // Chrome uses FontSizeBody explicitly; keep it on the same local multiplier.
        ((FrameworkElement)Content).Resources["FontSizeBody"] = FontSize;
    }

    private double EnvironmentFontSize => (double)GetValue(EnvironmentFontSizeProperty);

    private static void OnEnvironmentFontSizeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
        ((PlanValidationDetailsWindow)sender).ApplyFontMultiplier();

    private void ApplyFontMultiplier()
    {
        FontSize = EnvironmentFontSize * _fontMultiplier;
        if (Content is FrameworkElement chrome)
            chrome.Resources["FontSizeBody"] = FontSize;
    }

    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (TryZoomFont(e.Delta, Keyboard.Modifiers))
            e.Handled = true;
    }

    internal bool TryZoomFont(int delta, ModifierKeys modifiers)
    {
        if (modifiers != ModifierKeys.Control || delta == 0)
            return false;

        var multiplier = ApplicationSettingsSnapshot.NormalizeValidationDetailsFontMultiplier(
            Math.Round(_fontMultiplier + Math.Sign(delta) * 0.1, 2));
        if (multiplier == _fontMultiplier)
            return true;

        _fontMultiplier = multiplier;
        ApplyFontMultiplier();
        try
        {
            _settingsStore.SaveValidationDetailsFontMultiplier(multiplier);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            SquadDashTrace.Write("ValidationDetailsFont", $"Could not save font multiplier: {ex.Message}");
            MessageBox.Show(this, $"The font size changed, but could not be saved.\n\n{ex.Message}",
                "Validation Details", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        return true;
    }

    private static Style BuildAssertionItemStyle()
    {
        var root = new FrameworkElementFactory(typeof(Grid));
        root.SetValue(Panel.BackgroundProperty, Brushes.Transparent);

        // Only the selection layers are translucent, never the text or status glyph.
        var fill = new FrameworkElementFactory(typeof(Border), "SelectionFill");
        fill.SetResourceReference(Border.BackgroundProperty, "ChipSelectedSurface");
        fill.SetValue(UIElement.OpacityProperty, 0.5);
        fill.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        root.AppendChild(fill);

        var border = new FrameworkElementFactory(typeof(Border), "SelectionBorder");
        border.SetResourceReference(Border.BorderBrushProperty, "ChipSelectedBorder");
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(UIElement.OpacityProperty, 0.5);
        border.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        root.AppendChild(border);

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.MarginProperty, new Thickness(2));
        content.SetValue(ContentPresenter.ContentSourceProperty, "Content");
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        root.AppendChild(content);

        var template = new ControlTemplate(typeof(ListBoxItem)) { VisualTree = root };
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "SelectionFill"));
        selected.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "SelectionBorder"));
        selected.Setters.Add(new Setter(ForegroundProperty, new DynamicResourceExtension("ImportantText")));
        template.Triggers.Add(selected);

        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(ForegroundProperty, new DynamicResourceExtension("BodyText")));
        style.Setters.Add(new Setter(HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(TemplateProperty, template));
        return style;
    }

    private FrameworkElement BuildHeader(PlanValidationDetailsPresentation presentation)
    {
        var panel = new StackPanel { Margin = new Thickness(16, 14, 16, 10) };
        var status = new TextBlock
        {
            Text = presentation.FailedCount == 1
                ? "Validation failed — 1 assertion did not pass"
                : $"Validation failed — {presentation.FailedCount} assertions did not pass",
            FontWeight = FontWeights.SemiBold,
        };
        status.SetResourceReference(TextBlock.ForegroundProperty, "PriorityCritical");
        panel.Children.Add(status);

        var summary = new TextBlock
        {
            Text = presentation.Summary,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 8),
        };
        summary.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        panel.Children.Add(summary);

        var provenance = new TextBlock
        {
            Text = BuildProvenance(presentation),
            TextWrapping = TextWrapping.Wrap,
        };
        provenance.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        panel.Children.Add(provenance);
        return panel;
    }

    private static string BuildProvenance(PlanValidationDetailsPresentation presentation)
    {
        var parts = new List<string>
        {
            presentation.ValidationId,
            $"{presentation.Assertions.Count} assertions",
        };
        if (!string.IsNullOrWhiteSpace(presentation.ValidatedCommit))
            parts.Add($"commit {ShortSha(presentation.ValidatedCommit)}");
        if (presentation.CompletedAt is { } completedAt)
            parts.Add(completedAt.ToLocalTime().ToString("g"));
        return string.Join("  ·  ", parts);
    }

    private static ListBoxItem BuildAssertionItem(PlanValidationAssertionDetails assertion)
    {
        var grid = new Grid { Margin = new Thickness(2, 7, 8, 7) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var glyph = new TextBlock
        {
            Text = assertion.Passed switch { true => "✓", false => "✕", _ => "•" },
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 9, 0),
            VerticalAlignment = VerticalAlignment.Top,
        };
        glyph.SetResourceReference(TextBlock.ForegroundProperty,
            assertion.Passed switch { true => "TaskSuccessText", false => "PriorityCritical", _ => "SubtleText" });

        var text = new TextBlock
        {
            Text = $"{assertion.Number}. {assertion.Assertion}",
            TextWrapping = TextWrapping.Wrap,
        };
        text.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(Foreground))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ListBoxItem), 1),
        });
        Grid.SetColumn(glyph, 0);
        Grid.SetColumn(text, 1);
        grid.Children.Add(glyph);
        grid.Children.Add(text);
        return new ListBoxItem { Content = grid, Tag = assertion };
    }

    private void ShowAssertion(PlanValidationAssertionDetails assertion)
    {
        _assertionStatus.Text = $"Assertion {assertion.Number} · {assertion.StatusLabel}";
        _assertionStatus.SetResourceReference(TextBlock.ForegroundProperty,
            assertion.Passed switch { true => "TaskSuccessText", false => "PriorityCritical", _ => "SubtleText" });
        _assertionText.Text = assertion.Assertion;
        _evidenceText.Text = assertion.Evidence;
        _evidenceText.ScrollToHome();
    }

    private static string ShortSha(string sha) => sha.Length <= 8 ? sha : sha[..8];
}
