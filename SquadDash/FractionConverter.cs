namespace SquadDash;

using System;
using System.Globalization;
using System.Windows.Data;

/// <summary>
/// Multiplies a double value by a fraction supplied as ConverterParameter (e.g. "0.5").
/// Used to cap the Approved-items ScrollViewer height to a fraction of its parent.
/// </summary>
internal sealed class FractionConverter : IValueConverter {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
        if (value is double height && parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var fraction))
            return Math.Max(0, height * fraction);
        return 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
