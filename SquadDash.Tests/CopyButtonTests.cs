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
        var button = new CopyButton
        {
            Resources = CreateResources(
                new Viewbox { Child = new Border { Width = 100, Height = 120 } },
                new Viewbox { Child = new Border { Width = 80, Height = 80 } }),
            // Reproduces chromed windows that intentionally replace the implicit
            // CopyButton style with ThemedButtonStyle or QuickReplyButtonStyle.
            Style = new Style(typeof(Button)),
            FontSize = 18,
            CopyTooltip = "Copy test content",
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

    [Test]
    public void ClickThenDismissFeedback_ChangesToCopiedIconAndRestoresCopyIcon()
    {
        var button = new TestCopyButton
        {
            Resources = CreateResources(
                new Viewbox { Tag = "copy" },
                new Viewbox { Tag = "copied" }),
            CopyTooltip = "Copy test content",
        };

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

    private static ResourceDictionary CreateResources(object copyIcon, object copiedIcon)
    {
        return new ResourceDictionary
        {
            ["FontSizeNormal"] = 14.0,
            ["CopyButton"] = copyIcon,
            ["CopiedFeedback"] = copiedIcon,
        };
    }

    private sealed class TestCopyButton : CopyButton
    {
        internal void InvokeClick() => OnClick();
    }
}
