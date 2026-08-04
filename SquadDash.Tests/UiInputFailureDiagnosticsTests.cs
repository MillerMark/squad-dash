using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class UiInputFailureDiagnosticsTests
{
    [Test]
    public void ClassifyExceptionText_IdentifiesNativeFocusAcquisitionFailure()
    {
        var result = UiInputFailureDiagnostics.ClassifyExceptionText(
            "at System.Windows.Interop.HwndKeyboardInputProvider.AcquireFocus(Boolean checkOnly)");

        Assert.That(result, Does.Contain("keyboard-focus acquisition failed"));
        Assert.That(result, Does.Contain("button route and HWND state"));
    }

    [Test]
    public void ClassifyExceptionText_DoesNotClaimCauseForGenericDispatcherFailure()
    {
        var result = UiInputFailureDiagnostics.ClassifyExceptionText("System.InvalidOperationException");

        Assert.That(result, Does.Contain("not identifiable"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void DescribeElement_IncludesStableButtonIdentityAndState()
    {
        var button = new Button
        {
            Name = "ApproveButton",
            Content = "Approve and continue",
            IsEnabled = false,
        };
        AutomationProperties.SetAutomationId(button, "Approval.Accept");

        var result = UiInputFailureDiagnostics.DescribeElement(button);

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("Button"));
            Assert.That(result, Does.Contain("name=\"ApproveButton\""));
            Assert.That(result, Does.Contain("automationId=\"Approval.Accept\""));
            Assert.That(result, Does.Contain("content=\"Approve and continue\""));
            Assert.That(result, Does.Contain("enabled=False"));
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void BuildSnapshot_ExplainsDispatcherHandlingAndIncludesWindowInventory()
    {
        var result = UiInputFailureDiagnostics.BuildSnapshot(
            new NullReferenceException("focus failed"),
            application: null);

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.StartWith("UI input/focus diagnostic context:"));
            Assert.That(result, Does.Contain("SquadDash DispatcherUnhandledException"));
            Assert.That(result, Does.Contain("does not establish a framework bug or harmlessness"));
            Assert.That(result, Does.Contain("Last pointer press:"));
            Assert.That(result, Does.Contain("Application windows:"));
            Assert.That(result, Does.Contain("Presentation sources:"));
        });
    }
}
