using System.Threading;

namespace SquadDash.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
internal sealed class TextDisplayWindowTests
{
    [Test]
    public void UpdateContent_ReplacesDisplayedText()
    {
        var window = new TextDisplayWindow("GitHub Copilot Sign-in", "Original steps");

        window.UpdateContent("Updated steps");

        Assert.Multiple(() =>
        {
            Assert.That(window.Title, Is.EqualTo("GitHub Copilot Sign-in"));
            Assert.That(window.DisplayedContent, Is.EqualTo("Updated steps"));
        });
    }

    [Test]
    public void UpdateContent_WithSameText_RemainsStable()
    {
        var window = new TextDisplayWindow("GitHub Copilot Sign-in", "Sign in once");

        window.UpdateContent("Sign in once");

        Assert.That(window.DisplayedContent, Is.EqualTo("Sign in once"));
    }
}
