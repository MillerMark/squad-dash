using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace SquadDash;

public static class TranscriptTitleSpinnerLayout
{
    internal const double FontScale = 0.8;

    private readonly record struct MeasureKey(
        string Text, double FontSize, double Dpi, Typeface Typeface, string Culture, FlowDirection FlowDirection);

    private static readonly ConditionalWeakTable<TextBlock, Dictionary<MeasureKey, double>> MeasureCaches = new();

    public static readonly DependencyProperty TitleProperty = DependencyProperty.RegisterAttached(
        "Title", typeof(TextBlock), typeof(TranscriptTitleSpinnerLayout),
        new PropertyMetadata(null, OnTitleChanged));

    public static TextBlock? GetTitle(DependencyObject element) => (TextBlock?)element.GetValue(TitleProperty);
    public static void SetTitle(DependencyObject element, TextBlock value) => element.SetValue(TitleProperty, value);

    internal static DockPanel CreateSecondaryTitleRow(TextBlock title, ISpinnerActivitySource activitySource, out ActivitySpinner spinner)
    {
        spinner = new ActivitySpinner { VerticalAlignment = VerticalAlignment.Center };
        SetTitle(spinner, title);
        spinner.DataContext = activitySource;
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(spinner, Dock.Left);
        row.Children.Add(spinner);
        row.Children.Add(title);
        return row;
    }

    private static void OnTitleChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var spinner = (ActivitySpinner)sender;
        if (e.NewValue is not TextBlock title)
        {
            BindingOperations.ClearBinding(spinner, ActivitySpinner.FontSizeProperty);
            BindingOperations.ClearBinding(spinner, FrameworkElement.MarginProperty);
            return;
        }

        spinner.Opacity = 0.85;
        spinner.SetBinding(ActivitySpinner.FontSizeProperty, new Binding(nameof(TextBlock.FontSize))
        {
            Source = title,
            Converter = new FractionConverter(),
            ConverterParameter = FontScale.ToString(CultureInfo.InvariantCulture)
        });
        var margin = new MultiBinding { Converter = new SpaceMarginConverter(), ConverterParameter = title };
        foreach (var property in new[]
        {
            nameof(TextBlock.FontSize), nameof(TextBlock.FontFamily), nameof(TextBlock.FontStyle),
            nameof(TextBlock.FontWeight), nameof(TextBlock.FontStretch), nameof(TextBlock.FlowDirection),
            nameof(TextBlock.Language)
        })
            margin.Bindings.Add(new Binding(property) { Source = title });
        spinner.SetBinding(FrameworkElement.MarginProperty, margin);
    }

    internal static double GetExpandedWidth(TextBlock title, double fontSize) =>
        ActivitySpinner.GetExpandedLayoutWidth(fontSize * FontScale) + MeasureText(title, " ", fontSize);

    internal static double MeasureText(TextBlock title, string text, double fontSize)
    {
        var culture = title.Language.GetSpecificCulture();
        var pixelsPerDip = VisualTreeHelper.GetDpi(title).PixelsPerDip;
        var typeface = new Typeface(title.FontFamily, title.FontStyle, title.FontWeight, title.FontStretch);
        var key = new MeasureKey(text, fontSize, pixelsPerDip, typeface, culture.Name, title.FlowDirection);
        var cache = MeasureCaches.GetOrCreateValue(title);
        if (cache.TryGetValue(key, out var width))
            return width;

        var formatted = new FormattedText(text, culture, title.FlowDirection, typeface,
            fontSize, Brushes.Black, pixelsPerDip);
        // FormattedText.Width excludes trailing whitespace, including a lone space.
        width = text == " " ? formatted.WidthIncludingTrailingWhitespace : formatted.Width;
        if (cache.Count >= 256)
            cache.Clear();
        cache[key] = width;
        return width;
    }

    private sealed class SpaceMarginConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var title = (TextBlock)parameter;
            return new Thickness(0, 0, MeasureText(title, " ", title.FontSize), 0);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
