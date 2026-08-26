using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class CopyButtonTests
{
    [Test]
    public void ExplicitStyleReplacement_StillConstrainsIconToLiveFontSize()
    {
        var app = Application.Current ?? new Application();
        var resources = app.Resources;
        var oldFontSize = resources.Contains("FontSizeNormal") ? resources["FontSizeNormal"] : null;
        var oldCopyIcon = resources.Contains("CopyButton") ? resources["CopyButton"] : null;
        var oldCopiedIcon = resources.Contains("CopiedFeedback") ? resources["CopiedFeedback"] : null;

        try
        {
            resources["FontSizeNormal"] = 14.0;
            resources["CopyButton"] = new Viewbox { Child = new Border { Width = 100, Height = 120 } };
            resources["CopiedFeedback"] = new Viewbox { Child = new Border { Width = 80, Height = 80 } };

            var button = new CopyButton
            {
                // Reproduces chromed windows that intentionally replace the implicit
                // CopyButton style with ThemedButtonStyle or QuickReplyButtonStyle.
                Style = new Style(typeof(Button)),
                FontSize = 18,
            };
            var icon = (FrameworkElement)button.Content;

            Assert.Multiple(() =>
            {
                Assert.That(BindingOperations.GetBinding(icon, FrameworkElement.WidthProperty), Is.Not.Null);
                Assert.That(BindingOperations.GetBinding(icon, FrameworkElement.HeightProperty), Is.Not.Null);
                Assert.That(icon.Width, Is.EqualTo(18));
                Assert.That(icon.Height, Is.EqualTo(18));
            });

            button.FontSize = 24;

            Assert.Multiple(() =>
            {
                Assert.That(icon.Width, Is.EqualTo(24));
                Assert.That(icon.Height, Is.EqualTo(24));
            });
        }
        finally
        {
            RestoreResource(resources, "FontSizeNormal", oldFontSize);
            RestoreResource(resources, "CopyButton", oldCopyIcon);
            RestoreResource(resources, "CopiedFeedback", oldCopiedIcon);
        }
    }

    [Test]
    public void ClickThenDismissFeedback_ChangesToCopiedIconAndRestoresCopyIcon()
    {
        var app = Application.Current ?? new Application();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var resources = app.Resources;
        var oldFontSize = resources.Contains("FontSizeNormal") ? resources["FontSizeNormal"] : null;
        var oldCopyIcon = resources.Contains("CopyButton") ? resources["CopyButton"] : null;
        var oldCopiedIcon = resources.Contains("CopiedFeedback") ? resources["CopiedFeedback"] : null;

        try
        {
            resources["FontSizeNormal"] = 14.0;
            resources["CopyButton"] = new Viewbox { Tag = "copy" };
            resources["CopiedFeedback"] = new Viewbox { Tag = "copied" };
            var button = new TestCopyButton();

            button.InvokeClick();

            Assert.Multiple(() =>
            {
                Assert.That(button.IsCopied, Is.True);
                Assert.That(((FrameworkElement)button.Content).Tag, Is.EqualTo("copied"));
            });

            button.DismissFeedback();

            Assert.Multiple(() =>
            {
                Assert.That(button.IsCopied, Is.False);
                Assert.That(((FrameworkElement)button.Content).Tag, Is.EqualTo("copy"));
            });
        }
        finally
        {
            RestoreResource(resources, "FontSizeNormal", oldFontSize);
            RestoreResource(resources, "CopyButton", oldCopyIcon);
            RestoreResource(resources, "CopiedFeedback", oldCopiedIcon);
        }
    }

    private static void RestoreResource(ResourceDictionary resources, string key, object? value)
    {
        if (value is null)
            resources.Remove(key);
        else
            resources[key] = value;
    }

    private sealed class TestCopyButton : CopyButton
    {
        internal void InvokeClick() => OnClick();
    }
}
