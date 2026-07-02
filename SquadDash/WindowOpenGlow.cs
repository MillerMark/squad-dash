using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace SquadDash;

/// <summary>
/// Reusable helper that plays a glow-then-fade border entrance animation on a WPF
/// <see cref="Border"/> element. Mirrors the callout tour-entry animation in
/// <c>FrmUltimateCallout.StartTourEntryAnimation</c> / <c>InitTourGlow</c>, but
/// adapted for <see cref="Border"/> (BorderThickness / BorderBrush) instead of
/// <see cref="System.Windows.Shapes.Path"/> (StrokeThickness / Stroke).
/// </summary>
public static class WindowOpenGlow {
    /// <summary>
    /// Plays a glow-then-fade border entrance animation on the given Border element.
    /// Starts with a thick, saturated/bright border + drop-shadow glow, then fades
    /// both to the normal resting border over <paramref name="durationSeconds"/> seconds.
    /// </summary>
    /// <param name="border">The WPF Border to animate.</param>
    /// <param name="glowColor">The bright/saturated start color for the border and glow.</param>
    /// <param name="restBrush">The normal resting BorderBrush to fade to.</param>
    /// <param name="startThickness">Border thickness at the start of the animation (default 2.5).</param>
    /// <param name="endThickness">Border thickness at the end of the animation (default 1.5).</param>
    /// <param name="durationSeconds">Total animation duration (default 1.8s).</param>
    public static void Animate(Border border, Color glowColor, SolidColorBrush restBrush,
        double startThickness = 2.5, double endThickness = 1.5, double durationSeconds = 1.8) {
        var duration = new Duration(TimeSpan.FromSeconds(durationSeconds));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        // Set initial state immediately (before any animation begins).
        border.BorderThickness = new Thickness(startThickness);
        var animBrush = new SolidColorBrush(glowColor);
        border.BorderBrush = animBrush;

        // ── A. Drop-shadow glow fade-out ─────────────────────────────────────────
        var glowEffect = new DropShadowEffect {
            Color = glowColor,
            ShadowDepth = 0,
            BlurRadius = 28,
            Opacity = 1.0,
            RenderingBias = RenderingBias.Performance
        };
        border.Effect = glowEffect;

        var blurAnim = new DoubleAnimation(28, 0, duration) { EasingFunction = ease };
        blurAnim.Completed += (_, _) => border.Effect = null;
        glowEffect.BeginAnimation(DropShadowEffect.BlurRadiusProperty, blurAnim);

        // ── B. Border thickness fade-out ─────────────────────────────────────────
        var thicknessAnim = new ThicknessAnimation(
            new Thickness(startThickness),
            new Thickness(endThickness),
            duration) { EasingFunction = ease };
        thicknessAnim.Completed += (_, _) => {
            border.BeginAnimation(Border.BorderThicknessProperty, null);
            border.BorderThickness = new Thickness(endThickness);
        };
        border.BeginAnimation(Border.BorderThicknessProperty, thicknessAnim);

        // ── C. Border color fade-out ──────────────────────────────────────────────
        var colorAnim = new ColorAnimation(glowColor, restBrush.Color, duration) { EasingFunction = ease };
        colorAnim.Completed += (_, _) => border.BorderBrush = restBrush;
        animBrush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnim);
    }
}
