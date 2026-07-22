using System.Windows;
using SquadDash.GuidedTours;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class NativeDragContinuationPolicyTests
{
    [Test]
    public void ShouldCancel_WhenEscapePressed_ReturnsTrue()
    {
        Assert.That(
            NativeDragContinuationPolicy.ShouldCancel(true, DragDropKeyStates.LeftMouseButton),
            Is.True);
    }

    // Releasing the left button is the normal "drop" signal — WPF sets
    // Action = Drop by default.  We must NOT cancel here.
    [TestCase(DragDropKeyStates.None)]
    [TestCase(DragDropKeyStates.ControlKey)]
    public void ShouldCancel_WhenLeftButtonReleased_ReturnsFalse(DragDropKeyStates keyStates)
    {
        Assert.That(NativeDragContinuationPolicy.ShouldCancel(false, keyStates), Is.False);
    }

    [Test]
    public void ShouldCancel_WhileLeftButtonStillPressed_ReturnsFalse()
    {
        Assert.That(
            NativeDragContinuationPolicy.ShouldCancel(
                false,
                DragDropKeyStates.LeftMouseButton | DragDropKeyStates.ControlKey),
            Is.False);
    }
}
