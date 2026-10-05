using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
internal sealed class ButtonsPanelControllerTests {
    private readonly List<ButtonDefinition> _executed = [];
    private ButtonsPanelController _controller = null!;
    private ButtonDefinition _definition = null!;
    private Button _button = null!;

    [SetUp]
    public void SetUp() {
        _executed.Clear();
        var list = new StackPanel();
        _controller = new ButtonsPanelController(list, new StackPanel(), new TextBox(),
            () => { }, _ => { }, _ => { }, _executed.Add);
        _definition = new ButtonDefinition(Guid.NewGuid(),
            new ButtonContent("Test", "Test only", "echo never-run", ButtonCommandType.RawCommand), 0, 1);
        _controller.Refresh(new[] { _definition });
        _button = list.Children.OfType<Button>().Single();
    }

    [Test]
    public void BuildButton_NormalClick_RequestsExecutionExactlyOnce() {
        var click = new RoutedEventArgs(Button.ClickEvent, _button);

        _button.RaiseEvent(click);

        Assert.Multiple(() => {
            Assert.That(click.Handled, Is.True);
            Assert.That(_executed, Is.EqualTo(new[] { _definition }));
        });
    }

    [TestCase("dragging")]
    [TestCase("settling")]
    [TestCase("saving")]
    public void RequestExecution_AndRoutedClick_AreSuppressedOnlyWhileControllerStateIsActive(string phase) {
        if (phase == "dragging") {
            var order = new[] { _definition.Id };
            var gesture = new ButtonReorderGesture(order, order, _definition.Id, 0, 0, 1, 1);
            Assert.That(gesture.Move(ButtonReorderGesture.HoldInterval, 2, 2, true, true), Is.True);
            SetControllerState("_reorderGesture", gesture);
        } else {
            SetControllerState(phase == "settling" ? "_settling" : "_savingOrder", true);
        }

        _controller.RequestExecution(_definition);
        var click = new RoutedEventArgs(Button.ClickEvent, _button);
        _button.RaiseEvent(click);

        Assert.Multiple(() => {
            Assert.That(click.Handled, Is.True, "The real controller Click handler must be reached.");
            Assert.That(_executed, Is.Empty);
        });

        if (phase == "dragging")
            SetControllerState("_reorderGesture", null);
        else
            SetControllerState(phase == "settling" ? "_settling" : "_savingOrder", false);

        _controller.RequestExecution(_definition);
        Assert.That(_executed, Is.EqualTo(new[] { _definition }));
        _button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, _button));
        Assert.That(_executed, Is.EqualTo(new[] { _definition, _definition }));
    }

    private void SetControllerState(string fieldName, object? value) {
        // Seed only the gate inputs: this does not simulate native input or claim commit/recovery coverage.
        var field = typeof(ButtonsPanelController).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(ButtonsPanelController).FullName, fieldName);
        field.SetValue(_controller, value);
    }
}
