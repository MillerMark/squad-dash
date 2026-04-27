using System;
using System.Windows.Media;

namespace SquadDash;

internal static class ColorUtilities {
    internal static SolidColorBrush AccentBrush(string hex) {
        return (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
    }

    internal static SolidColorBrush CreateAccentBrush(string hex) {
        return (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
    }

    internal static SolidColorBrush CreateDarkAccentBrush(string hex) {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        RgbToHsl(color.R, color.G, color.B, out double h, out double s, out double l);
        var boostedL = Math.Max(0.15, l - 0.10);
        var boostedS  = Math.Min(1.0, s * 1.15);
        HslToRgb(h, boostedS, boostedL, out byte r, out byte g, out byte b);
        return new SolidColorBrush(Color.FromRgb(r, g, b));
    }

    internal static void RgbToHsl(byte r, byte g, byte b, out double h, out double s, out double l) {
        double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
        double max = Math.Max(rd, Math.Max(gd, bd));
        double min = Math.Min(rd, Math.Min(gd, bd));
        l = (max + min) / 2.0;
        if (max == min) { h = s = 0; return; }
        double d = max - min;
        s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
        if      (max == rd) h = (gd - bd) / d + (gd < bd ? 6 : 0);
        else if (max == gd) h = (bd - rd) / d + 2;
        else                h = (rd - gd) / d + 4;
        h /= 6.0;
    }

    internal static void HslToRgb(double h, double s, double l, out byte r, out byte g, out byte b) {
        if (s == 0) { r = g = b = (byte)(l * 255); return; }
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        double p = 2 * l - q;
        r = (byte)(HueToRgb(p, q, h + 1.0/3) * 255);
        g = (byte)(HueToRgb(p, q, h)         * 255);
        b = (byte)(HueToRgb(p, q, h - 1.0/3) * 255);
    }

    internal static double HueToRgb(double p, double q, double t) {
        if (t < 0) t += 1; if (t > 1) t -= 1;
        if (t < 1.0/6) return p + (q - p) * 6 * t;
        if (t < 1.0/2) return q;
        if (t < 2.0/3) return p + (q - p) * (2.0/3 - t) * 6;
        return p;
    }
}
