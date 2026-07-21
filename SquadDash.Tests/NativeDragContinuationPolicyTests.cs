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

    [TestCase(DragDropKeyStates.None)]
    [TestCase(DragDropKeyStates.ControlKey)]
    public void ShouldCancel_WhenLeftButtonReleased_ReturnsTrue(DragDropKeyStates keyStates)
    {
        Assert.That(NativeDragContinuationPolicy.ShouldCancel(false, keyStates), Is.True);
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
