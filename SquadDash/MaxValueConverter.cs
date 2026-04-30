using System;
using System.Globalization;
using System.Windows.Data;

namespace SquadDash;

/// <summary>
/// Returns the maximum of two or more doubles — used to size secondary panels
/// to the tallest of the Active / Roster reference panels.
/// </summary>
public sealed class MaxValueConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        double max = 0;
        foreach (var v in values)
        {
            if (v is double d && !double.IsNaN(d) && !double.IsInfinity(d))
                max = Math.Max(max, d);
        }
        return max;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
