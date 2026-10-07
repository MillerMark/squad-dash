using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace SquadDash.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
internal sealed class SteeringTargetDialogTests {
    [Test]
    public void KeyboardNavigation_MovesFocusWithoutSelecting_ThenSpaceSelectsAndEnterAccepts() {
        var targets = CreateTargets();
        var owner = CreateOwner();
        var dialog = new SteeringTargetDialog(targets, targets[1]) { Owner = owner };
        try {
            dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(() => {
                    var radios = Descendants(dialog).OfType<RadioButton>().ToArray();
                    Assert.Multiple(() => {
                        Assert.That(radios, Has.Length.EqualTo(3));
                        Assert.That(radios[1].IsChecked, Is.True);
                        Assert.That(radios[1].IsKeyboardFocused, Is.True);
                    });

                    RaisePreviewKey(dialog, Key.Tab);
                    Assert.Multiple(() => {
                        Assert.That(radios[2].IsKeyboardFocused, Is.True);
                        Assert.That(radios[1].IsChecked, Is.True,
                            "Moving focus must not change the selected target.");
                        Assert.That(radios[2].IsChecked, Is.False);
                    });

                    // KeyEventArgs cannot synthesize the global Keyboard.Modifiers value
                    // consulted by the dialog, so exercise Shift+Tab's reverse traversal
                    // branch directly without injecting process-wide keyboard input.
                    MoveFocusBackward(dialog);
                    Assert.Multiple(() => {
                        Assert.That(radios[1].IsKeyboardFocused, Is.True);
                        Assert.That(radios[1].IsChecked, Is.True);
                    });

                    RaisePreviewKey(dialog, Key.Tab);
                    RaisePreviewKey(dialog, Key.Space);
                    Assert.That(radios[2].IsChecked, Is.True);

                    RaisePreviewKey(dialog, Key.Enter);
                }));

            var result = dialog.ShowDialog();

            Assert.Multiple(() => {
                Assert.That(result, Is.True);
                Assert.That(dialog.SelectedTarget, Is.SameAs(targets[2]));
            });
        }
        finally {
            dialog.Close();
            owner.Close();
        }
    }

    [Test]
    public void Escape_CancelsWithoutReturningASelection() {
        var targets = CreateTargets();
        var owner = CreateOwner();
        var dialog = new SteeringTargetDialog(targets, targets[0]) { Owner = owner };
        try {
            dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(() => RaisePreviewKey(dialog, Key.Escape)));

            var result = dialog.ShowDialog();

            Assert.Multiple(() => {
                Assert.That(result, Is.False);
                Assert.That(dialog.SelectedTarget, Is.Null);
            });
        }
        finally {
            dialog.Close();
            owner.Close();
        }
    }

    private static SteeringTargetCandidate[] CreateTargets() => [
        Candidate("coordinator", "Coordinator", parentId: null, stableOrder: 0),
        Candidate("named", "Named agent", parentId: "coordinator", stableOrder: 1),
        Candidate("temporary", "Temporary agent", parentId: "named", stableOrder: 2),
    ];

    private static SteeringTargetCandidate Candidate(
        string id,
        string displayName,
        string? parentId,
        int stableOrder) =>
        new(
            Id: id,
            DisplayName: displayName,
            SessionId: $"{id}-session",
            ParentId: parentId,
            StableOrder: stableOrder,
            IsRunning: true,
            DirectLastActivityAt: DateTimeOffset.UtcNow,
            ActiveToolCount: 0,
            IsWaitingOnChild: false);

    private static Window CreateOwner() {
        var owner = new Window {
            Width = 1,
            Height = 1,
            ShowInTaskbar = false,
        };
        owner.Show();
        return owner;
    }

    private static void RaisePreviewKey(
        UIElement target,
        Key key) {
        var source = PresentationSource.FromVisual(target)
            ?? throw new InvalidOperationException("The dialog has no presentation source.");
        target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        });
    }

    private static void MoveFocusBackward(SteeringTargetDialog dialog) {
        var method = typeof(SteeringTargetDialog).GetMethod(
            "MoveRadioFocus",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(SteeringTargetDialog), "MoveRadioFocus");
        method.Invoke(dialog, [-1]);
    }

    private static IEnumerable<DependencyObject> Descendants(object root) {
        if (root is not DependencyObject dependencyObject)
            yield break;

        yield return dependencyObject;
        foreach (var child in LogicalTreeHelper.GetChildren(dependencyObject)) {
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

}
