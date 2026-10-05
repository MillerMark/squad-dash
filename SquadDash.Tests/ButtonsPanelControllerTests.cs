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
    private StackPanel _list = null!;

    [SetUp]
    public void SetUp() {
        _executed.Clear();
        _list = new StackPanel();
        _controller = new ButtonsPanelController(_list, new StackPanel(), new TextBox(),
            () => { }, _ => { }, _ => { }, _executed.Add);
        _definition = new ButtonDefinition(Guid.NewGuid(),
            new ButtonContent("Test", "Test only", "echo never-run", ButtonCommandType.RawCommand), 0, 1);
        _controller.Refresh(new[] { _definition });
        _button = _list.Children.OfType<Button>().Single();
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

    [TestCase(false)]
    [TestCase(true)]
    public async Task CommitReorderAsync_SaveFails_ReportsErrorAndReloadsPersistedOrderAfterSettling(
        bool synchronousFailure) {
        var original = CreateOrder();
        var expectedOrder = original.Select(item => item.Id).ToArray();
        var previewOrder = new[] { expectedOrder[1], expectedOrder[2], expectedOrder[0] };
        var persisted = new[] {
            original[2] with { SortOrder = 0 },
            original[0] with { SortOrder = 1 },
            original[1] with { SortOrder = 2 }
        };
        _controller.Refresh(original);
        var failure = new IOException("Controlled order persistence failure.");
        var save = new TaskCompletionSource<IReadOnlyList<ButtonDefinition>>();
        var reload = new TaskCompletionSource<IReadOnlyList<ButtonDefinition>>();
        var rollbackSettled = new TaskCompletionSource();
        var persistedSettled = new TaskCompletionSource();
        var rollbackStarted = new TaskCompletionSource();
        var reloadStarted = new TaskCompletionSource();
        var persistedSettleStarted = new TaskCompletionSource();
        var errors = new List<Exception>();
        var events = new List<string>();
        var waitCount = 0;
        _controller.SaveOrderAsync = (expected, proposed) => {
            Assert.Multiple(() => {
                Assert.That(expected, Is.EqualTo(expectedOrder));
                Assert.That(proposed, Is.EqualTo(previewOrder));
            });
            events.Add("save");
            if (synchronousFailure) throw failure;
            return save.Task;
        };
        _controller.ReloadOrderAsync = () => {
            events.Add("reload");
            reloadStarted.SetResult();
            return reload.Task;
        };
        _controller.ReportReorderError = error => {
            errors.Add(error);
            events.Add("report");
        };

        var commit = _controller.CommitReorderAsync(expectedOrder, previewOrder,
            _controller.SaveOrderAsync, _controller.ReloadOrderAsync, _controller.ReportReorderError,
            () => events.Add("cancel"), () => {
                if (++waitCount == 1) {
                    events.Add("rollback-wait");
                    rollbackStarted.SetResult();
                    return rollbackSettled.Task;
                }
                events.Add("persisted-wait");
                persistedSettleStarted.SetResult();
                return persistedSettled.Task;
            });
        if (!synchronousFailure) save.SetException(failure);
        await rollbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _controller.RequestExecution(_definition);
        Assert.Multiple(() => {
            Assert.That(commit.IsCompleted, Is.False);
            Assert.That(errors, Is.Empty, "Reporting follows rollback settling.");
            Assert.That(reloadStarted.Task.IsCompleted, Is.False);
            Assert.That(_controller.ViewModel.Buttons, Is.EqualTo(original));
            Assert.That(_executed, Is.Empty);
        });

        rollbackSettled.SetResult();
        await reloadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Multiple(() => {
            Assert.That(errors, Is.EqualTo(new[] { failure }));
            Assert.That(ButtonsUiErrors.IsExpected(errors.Single()), Is.True);
            Assert.That(commit.IsCompleted, Is.False);
        });
        reload.SetResult(persisted);
        await persistedSettleStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _controller.RequestExecution(_definition);
        Assert.Multiple(() => {
            Assert.That(_controller.ViewModel.Buttons, Is.EqualTo(persisted));
            Assert.That(commit.IsCompleted, Is.False, "Commit includes persisted-order settling.");
            Assert.That(_executed, Is.Empty);
        });

        persistedSettled.SetResult();
        await commit.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Multiple(() => {
            Assert.That(_controller.ViewModel.Buttons, Is.EqualTo(persisted));
            Assert.That(_controller.ViewModel.Buttons.Select(item => item.Id), Is.Not.EqualTo(previewOrder));
            Assert.That(_list.Children.OfType<Button>().Select(item => item.Tag), Is.EqualTo(persisted),
                "The panel must rebuild from the reloaded order, not the initial or preview order.");
            Assert.That(_controller.ViewModel.LoadError, Is.Null);
            Assert.That(events, Is.EqualTo(new[] {
                "save", "cancel", "rollback-wait", "report", "reload", "persisted-wait"
            }));
        });
        _controller.RequestExecution(persisted[0]);
        Assert.That(_executed, Is.EqualTo(new[] { persisted[0] }));
    }

    [Test]
    public async Task CommitReorderAsync_SaveSucceeds_PublishesSavedOrderAndAwaitsSettling() {
        var original = CreateOrder();
        var saved = original.Reverse().Select((item, index) => item with { SortOrder = index }).ToArray();
        _controller.Refresh(original);
        var save = new TaskCompletionSource<IReadOnlyList<ButtonDefinition>>();
        var settled = new TaskCompletionSource();
        var settleStarted = new TaskCompletionSource();
        var commit = _controller.CommitReorderAsync(
            original.Select(item => item.Id).ToArray(), saved.Select(item => item.Id).ToArray(),
            (_, _) => save.Task,
            () => throw new AssertionException("Successful commit must not reload."),
            _ => Assert.Fail("Successful commit must not report an error."),
            () => Assert.Fail("Successful commit must not cancel the gesture."),
            () => {
                settleStarted.SetResult();
                return settled.Task;
            });
        Assert.That(_controller.ViewModel.Buttons, Is.EqualTo(original));

        save.SetResult(saved);
        await settleStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _controller.RequestExecution(_definition);
        Assert.Multiple(() => {
            Assert.That(_controller.ViewModel.Buttons, Is.EqualTo(saved));
            Assert.That(_controller.ViewModel.LoadError, Is.Null);
            Assert.That(commit.IsCompleted, Is.False);
            Assert.That(_list.Children.OfType<Button>().Select(item => item.Tag), Is.EqualTo(original));
            Assert.That(_executed, Is.Empty);
        });

        settled.SetResult();
        await commit.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(_list.Children.OfType<Button>().Select(item => item.Tag), Is.EqualTo(saved));
        _controller.RequestExecution(saved[0]);
        Assert.That(_executed, Is.EqualTo(new[] { saved[0] }));
    }

    [Test]
    public async Task CommitReorderAsync_ReloadFails_ReportsBothErrorsAndShowsLoadError() {
        var original = CreateOrder();
        _controller.Refresh(original);
        var failure = new IOException("Controlled save failure.");
        var reloadFailure = new IOException("Controlled reload failure.");
        var errors = new List<Exception>();

        await _controller.CommitReorderAsync(
            original.Select(item => item.Id).ToArray(), original.Reverse().Select(item => item.Id).ToArray(),
            (_, _) => Task.FromException<IReadOnlyList<ButtonDefinition>>(failure),
            () => Task.FromException<IReadOnlyList<ButtonDefinition>>(reloadFailure),
            errors.Add, () => { }, () => Task.CompletedTask);

        Assert.Multiple(() => {
            Assert.That(errors, Is.EqualTo(new[] { failure, reloadFailure }));
            Assert.That(_controller.ViewModel.Buttons, Is.EqualTo(original));
            Assert.That(_controller.ViewModel.LoadError, Is.EqualTo($"Unable to reload buttons: {reloadFailure.Message}"));
            Assert.That(_list.Children.OfType<TextBlock>().Single().Text, Is.EqualTo(_controller.ViewModel.LoadError));
            Assert.That(_list.Children.OfType<Button>(), Is.Empty);
        });
    }

    private ButtonDefinition[] CreateOrder() => [
        _definition,
        _definition with { Id = Guid.NewGuid(), SortOrder = 1 },
        _definition with { Id = Guid.NewGuid(), SortOrder = 2 }
    ];

    private void SetControllerState(string fieldName, object? value) {
        // Seed only the gate inputs: this does not simulate native input or claim commit/recovery coverage.
        var field = typeof(ButtonsPanelController).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(ButtonsPanelController).FullName, fieldName);
        field.SetValue(_controller, value);
    }
}
