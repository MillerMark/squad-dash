using System;
using System.Collections.Generic;
using System.Windows.Documents;

namespace SquadDash;

/// <summary>
/// Caches prompt anchors in document coordinates. Vertical scrolling and viewport-height
/// changes do not affect these values; a width change or prompt-list mutation does.
/// </summary>
internal sealed class PromptNavigationGeometryCache
{
    private readonly Dictionary<Paragraph, double> _absoluteYs = [];
    private Paragraph[] _promptOrder = [];
    private double _layoutWidth = double.NaN;
    private double _fontSize = double.NaN;

    internal double[] GetAbsoluteYs(
        IReadOnlyList<PromptEntry> prompts,
        double layoutWidth,
        double fontSize,
        Func<Paragraph, double>? measure)
    {
        if (LayoutSignatureChanged(prompts, layoutWidth, fontSize))
        {
            _absoluteYs.Clear();
            _promptOrder = new Paragraph[prompts.Count];
            for (var i = 0; i < prompts.Count; i++)
                _promptOrder[i] = prompts[i].Paragraph;
            _layoutWidth = layoutWidth;
            _fontSize = fontSize;
        }

        var result = new double[prompts.Count];
        for (var i = 0; i < prompts.Count; i++)
        {
            var paragraph = prompts[i].Paragraph;
            if (!_absoluteYs.TryGetValue(paragraph, out var absoluteY) && measure is not null)
            {
                absoluteY = measure(paragraph);
                if (!double.IsNaN(absoluteY) && !double.IsInfinity(absoluteY))
                    _absoluteYs[paragraph] = absoluteY;
            }
            result[i] = _absoluteYs.TryGetValue(paragraph, out absoluteY)
                ? absoluteY
                : double.NaN;
        }
        return result;
    }

    internal void Invalidate()
    {
        _absoluteYs.Clear();
        _promptOrder = [];
        _layoutWidth = double.NaN;
        _fontSize = double.NaN;
    }

    private bool LayoutSignatureChanged(IReadOnlyList<PromptEntry> prompts, double layoutWidth, double fontSize)
    {
        if (_promptOrder.Length != prompts.Count ||
            double.IsNaN(_layoutWidth) ||
            Math.Abs(_layoutWidth - layoutWidth) >= 0.5 ||
            double.IsNaN(_fontSize) ||
            Math.Abs(_fontSize - fontSize) >= 0.01)
            return true;

        for (var i = 0; i < prompts.Count; i++)
            if (!ReferenceEquals(_promptOrder[i], prompts[i].Paragraph))
                return true;
        return false;
    }
}
