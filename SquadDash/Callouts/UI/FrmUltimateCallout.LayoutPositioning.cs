using System;

namespace SquadDash;
/// <summary>
/// Interaction logic for FrmUltimateCallout.xaml
/// </summary>
public partial class
    FrmUltimateCallout {
    /// <summary>
    /// Determines the horizontal scale of the Close Button Figure (a fixed transparent flow document element 
    /// to get word-wrapping to keep words from overlapping the close button).
    /// </summary>
    double GetCloseButtonFigureHorizontalScale() {
        if (FontSize > 24)
            return 0.5;
        if (FontSize > 20)
            return 0.75;
        if (FontSize > 15)
            return 0.8;
        if (FontSize > 13.5)
            return 1;
        if (FontSize > 12)
            return 1.1;
        if (FontSize > 8)
            return 1.2;
        return 1.3;
    }

    double GetMarkdownMargin() {
        if (FontSize < 8)
            return 11;
        if (FontSize < 8.5)
            return 9;
        if (FontSize < 9.5)
            return 7.5;
        if (FontSize < 12)
            return 6;
        if (FontSize < 16)
            return 4;
        if (FontSize < 18)
            return 2;
        return 0;
    }

    double GetMarkdownVerticalOffset() {
        if (FontSize > 12.8)
            return -10 * (FontSize - 12.8) / (36 - 12.8);
        return 0;
    }

    double GetMarkdownHorizontalOffset() {
        if (FontSize > 12.8)
            return -8 * (FontSize - 12.8) / (36 - 12.8);
        return 0;
    }

    double GetMarkdownWidthAdjust() {
        if (FontSize < 8)
            return -15;
        if (FontSize < 10)
            return -10;
        if (FontSize < 12)
            return -5;
        if (FontSize > 35)
            return 32;
        if (FontSize > 30)
            return 24;
        if (FontSize > 25)
            return 16;
        if (FontSize > 18)
            return 5;
        return 0;
    }

    double GetExtraBottomMargin() {
        return _isTourMode ? FontSize * 1.8 : 0;
    }
}