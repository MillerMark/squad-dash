using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using CancelEventArgs = System.ComponentModel.CancelEventArgs;
using EventHandlerList = System.ComponentModel.EventHandlerList;

namespace SquadDash.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
internal sealed class AttachmentPromptFocusReturnTests
{
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void QueueIfUninterrupted_FocusRoundTripAbortsBeforeDispatch_WithoutNativeFocus(
        bool gotFocus, bool handled)
    {
        var owner = new Window();
        var first = new TextBox();
        var second = new TextBox();
        var calls = 0;
        var operation = AttachmentPromptFocusReturn.QueueIfUninterrupted(
            owner, owner.Dispatcher, () => calls++);
        try
        {
            Assert.That(operation.Status, Is.EqualTo(DispatcherOperationStatus.Pending));
            AssertSubscriptions(owner, operation, 1);
            var routedEvent = gotFocus ? Keyboard.GotKeyboardFocusEvent : Keyboard.LostKeyboardFocusEvent;

            // Feed committed WPF transitions through the real input pipeline, without
            // acquiring an HWND or depending on the desktop's keyboard-focus identity.
            ProcessFocusTransition(first, second, routedEvent, handled);
            Assert.That(operation.Status, Is.EqualTo(DispatcherOperationStatus.Aborted),
                "A -> B must cancel immediately, not merely fail a later focus-identity check.");
            AssertSubscriptions(owner, operation, 0);
            ProcessFocusTransition(second, first, routedEvent, handled);
            Pump();

            Assert.Multiple(() =>
            {
                Assert.That(calls, Is.Zero, "B -> A must not revive the cancelled handoff.");
                Assert.That(operation.Status, Is.EqualTo(DispatcherOperationStatus.Aborted));
                Assert.That(new WindowInteropHelper(owner).Handle, Is.EqualTo(IntPtr.Zero));
            });
        }
        finally
        {
            operation.Abort();
            owner.Close();
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void QueueIfUninterrupted_SameFocusNotificationDoesNotCancel(bool gotFocus)
    {
        var owner = new Window();
        var element = new TextBox();
        var calls = 0;
        var operation = AttachmentPromptFocusReturn.QueueIfUninterrupted(
            owner, owner.Dispatcher, () => calls++);
        try
        {
            ProcessFocusTransition(element, element,
                gotFocus ? Keyboard.GotKeyboardFocusEvent : Keyboard.LostKeyboardFocusEvent);
            Assert.That(operation.Status, Is.EqualTo(DispatcherOperationStatus.Pending));
            Pump();
            Assert.Multiple(() =>
            {
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(operation.Status, Is.EqualTo(DispatcherOperationStatus.Completed));
            });
            AssertSubscriptions(owner, operation, 0);
        }
        finally
        {
            operation.Abort();
            owner.Close();
        }
    }

    [Test]
    public void QueueIfUninterrupted_CompletionDetachesBeforeCallback_AndRunsOnlyOnce()
    {
        var owner = new Window();
        var calls = 0;
        DispatcherOperation? operation = null;
        operation = AttachmentPromptFocusReturn.QueueIfUninterrupted(owner, owner.Dispatcher, () =>
        {
            AssertSubscriptions(owner, operation!, 0);
            ProcessFocusTransition(new TextBox(), new TextBox(), Keyboard.GotKeyboardFocusEvent);
            calls++;
        });
        try
        {
            Assert.That(calls, Is.Zero);
            Assert.That(operation.Priority, Is.EqualTo(DispatcherPriority.Input));
            AssertSubscriptions(owner, operation, 1);
            Pump();
            Pump();
            Assert.Multiple(() =>
            {
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(operation.Status, Is.EqualTo(DispatcherOperationStatus.Completed));
                Assert.That(new WindowInteropHelper(owner).Handle, Is.EqualTo(IntPtr.Zero));
            });
            AssertSubscriptions(owner, operation, 0);
        }
        finally
        {
            operation.Abort();
            owner.Close();
        }
    }

    [TestCase("deactivated")]
    [TestCase("closing")]
    [TestCase("abort")]
    [TestCase("mouse")]
    [TestCase("wheel")]
    [TestCase("text")]
    public void QueueIfUninterrupted_InterruptionAbortsAndDetachesAllHandlers(string interruption)
    {
        var owner = new Window();
        var calls = 0;
        var closing = new CancelEventArgs { Cancel = true };
        var operation = AttachmentPromptFocusReturn.QueueIfUninterrupted(
            owner, owner.Dispatcher, () => calls++);
        try
        {
            AssertSubscriptions(owner, operation, 1);
            switch (interruption)
            {
                case "deactivated":
                    typeof(Window).GetMethod("OnDeactivated", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(owner, [EventArgs.Empty]);
                    break;
                case "closing":
                    typeof(Window).GetMethod("OnClosing", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(owner, [closing]);
                    Assert.That(closing.Cancel, Is.True, "Even vetoed closure must cancel the handoff.");
                    break;
                case "abort":
                    Assert.That(operation.Abort(), Is.True);
                    break;
                case "mouse":
                    InputManager.Current.ProcessInput(new MouseButtonEventArgs(Mouse.PrimaryDevice,
                        Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
                    break;
                case "wheel":
                    InputManager.Current.ProcessInput(new MouseWheelEventArgs(Mouse.PrimaryDevice,
                        Environment.TickCount, 120) { RoutedEvent = Mouse.PreviewMouseWheelEvent });
                    break;
                case "text":
                    InputManager.Current.ProcessInput(new TextCompositionEventArgs(Keyboard.PrimaryDevice,
                        new TextComposition(InputManager.Current, new TextBox(), "new input"))
                    { RoutedEvent = TextCompositionManager.PreviewTextInputEvent });
                    break;
                default:
                    Assert.Fail($"Unknown interruption: {interruption}");
                    break;
            }

            Assert.That(operation.Status, Is.EqualTo(DispatcherOperationStatus.Aborted));
            AssertSubscriptions(owner, operation, 0);
            Pump();
            Assert.That(calls, Is.Zero);

            var next = AttachmentPromptFocusReturn.QueueIfUninterrupted(
                owner, owner.Dispatcher, () => calls++);
            try
            {
                AssertSubscriptions(owner, next, 1);
                Pump();
                Assert.That(calls, Is.EqualTo(1), "Cancellation must not poison the next handoff.");
                AssertSubscriptions(owner, next, 0);
            }
            finally
            {
                next.Abort();
            }
            Assert.That(new WindowInteropHelper(owner).Handle, Is.EqualTo(IntPtr.Zero));
        }
        finally
        {
            operation.Abort();
            owner.Close();
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void QueueIfUninterrupted_ShutdownAbortsAndDetachesAllHandlers(bool alreadyShutdown)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var owner = new Window();
            DispatcherOperation? operation = null;
            try
            {
                var dispatcher = owner.Dispatcher;
                var calls = 0;
                if (alreadyShutdown)
                    dispatcher.InvokeShutdown();
                operation = AttachmentPromptFocusReturn.QueueIfUninterrupted(
                    owner, dispatcher, () => calls++);
                if (!alreadyShutdown)
                {
                    AssertSubscriptions(owner, operation, 1);
                    dispatcher.InvokeShutdown();
                }
                Assert.Multiple(() =>
                {
                    Assert.That(dispatcher.HasShutdownFinished, Is.True);
                    Assert.That(operation.Status, Is.EqualTo(DispatcherOperationStatus.Aborted));
                    Assert.That(calls, Is.Zero);
                    Assert.That(new WindowInteropHelper(owner).Handle, Is.EqualTo(IntPtr.Zero));
                });
                AssertSubscriptions(owner, operation, 0);
            }
            catch (Exception error)
            {
                failure = error;
            }
            finally
            {
                operation?.Abort();
                owner.Close();
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.That(thread.Join(TimeSpan.FromSeconds(10)), Is.True, "Dispatcher shutdown must complete.");
        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void ProcessFocusTransition(
        IInputElement oldFocus, IInputElement newFocus, RoutedEvent routedEvent, bool handled = false) =>
        InputManager.Current.ProcessInput(new KeyboardFocusChangedEventArgs(
            Keyboard.PrimaryDevice, Environment.TickCount, oldFocus, newFocus)
        {
            RoutedEvent = routedEvent,
            Handled = handled
        });

    private static void AssertSubscriptions(Window owner, DispatcherOperation operation, int expected)
    {
        // Inspect the actual WPF publishers: a no-op callback alone cannot prove
        // that cancelled/completed operations released their global input handlers.
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        var windowEvents = (EventHandlerList?)typeof(Window).GetField("_events", instance)!.GetValue(owner);
        Delegate? WindowHandler(string key) => windowEvents?[
            typeof(Window).GetField(key, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!];
        var inputHandlers = (Tuple<PreProcessInputEventHandler, Delegate[]>?)typeof(InputManager)
            .GetField("_preProcessInput", instance)!.GetValue(InputManager.Current);
        var subscriptions = new (string Name, Delegate? Handler)[]
        {
            ("Window.Deactivated", WindowHandler("EVENT_DEACTIVATED")),
            ("Window.Closing", WindowHandler("EVENT_CLOSING")),
            ("Dispatcher.ShutdownStarted", (Delegate?)typeof(Dispatcher)
                .GetField("ShutdownStarted", instance)!.GetValue(owner.Dispatcher)),
            ("InputManager.PreProcessInput", inputHandlers?.Item1),
            ("DispatcherOperation.Aborted", (Delegate?)typeof(DispatcherOperation)
                .GetField("_aborted", instance)!.GetValue(operation))
        };
        Assert.Multiple(() =>
        {
            foreach (var (name, handler) in subscriptions)
            {
                var count = handler?.GetInvocationList().Count(callback =>
                    callback.Method.DeclaringType?.DeclaringType == typeof(AttachmentPromptFocusReturn)) ?? 0;
                Assert.That(count, Is.EqualTo(expected), name);
            }
        });
    }

    [Test]
    public void Queue_ChangedContextRejectsBeforeSchedulingOrChangingLogicalFocus()
    {
        var prompt = new RichTextBox();
        var owner = new Window();
        var calls = 0;
        try
        {
            AttachmentPromptFocusReturn.Queue(owner, prompt, () => { calls++; return false; });
            Pump();
            Assert.Multiple(() =>
            {
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(FocusManager.GetFocusedElement(owner), Is.Null);
                Assert.That(new WindowInteropHelper(owner).Handle, Is.EqualTo(IntPtr.Zero));
            });
        }
        finally
        {
            owner.Close();
        }
    }

    [TestCase("hidden")]
    [TestCase("disabled")]
    [TestCase("minimized")]
    public void Queue_UnavailableOwnerRejectsWithoutDeferring(string state)
    {
        var prompt = new RichTextBox();
        var owner = new Window { Content = prompt, Width = 200, Height = 100, ShowInTaskbar = false };
        var calls = 0;
        try
        {
            owner.Show();
            if (state == "hidden")
                owner.Hide();
            else if (state == "disabled")
                owner.IsEnabled = false;
            else
                owner.WindowState = WindowState.Minimized;
            Pump();
            var focused = Keyboard.FocusedElement;
            AttachmentPromptFocusReturn.Queue(owner, prompt, () => { calls++; return true; });
            Pump();
            Assert.Multiple(() =>
            {
                Assert.That(calls, Is.EqualTo(1), "Unavailable owners must not enqueue a second eligibility check.");
                Assert.That(Keyboard.FocusedElement, Is.SameAs(focused));
            });
        }
        finally
        {
            owner.Close();
        }
    }

    [Test]
    public void Queue_ShutdownDispatcherRejectsWithoutEvaluatingContext()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var prompt = new RichTextBox();
                var owner = new Window();
                owner.Close();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
                AttachmentPromptFocusReturn.Queue(owner, prompt,
                    () => throw new AssertionException("Shutdown must reject before inspecting live context."));
            }
            catch (Exception error)
            {
                failure = error;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.That(thread.Join(TimeSpan.FromSeconds(10)), Is.True);
        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Test]
    [Category("NativeFocus")]
    public void Queue_EligibleOwnerReturnsExactPromptOnlyOnDispatcherTurn()
    {
        WithForegroundOwner((owner, prompt, previous, _) =>
        {
            var calls = 0;
            AttachmentPromptFocusReturn.Queue(owner, prompt, () => { calls++; return true; });
            Assert.Multiple(() =>
            {
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(Keyboard.FocusedElement, Is.SameAs(previous), "Queue must not move keyboard focus synchronously.");
            });
            Pump();
            Assert.Multiple(() =>
            {
                Assert.That(calls, Is.EqualTo(3), "Both queued context checks must run on an eligible handoff.");
                Assert.That(Keyboard.FocusedElement, Is.SameAs(prompt));
                Assert.That(prompt.IsKeyboardFocused, Is.True);
                Assert.That(NativeGetForegroundWindow(), Is.EqualTo(new WindowInteropHelper(owner).Handle));
            });
        });
    }

    [Test]
    [Category("NativeFocus")]
    public void Queue_InterveningChoiceOrUnavailableTargetMustNotStealFocus(
        [Values("context", "focus", "focus-round-trip", "key", "mouse", "wheel", "text",
            "rehost", "hidden", "disabled", "not-focusable", "closing", "foreground")] string change)
    {
        WithForegroundOwner((owner, prompt, previous, other) =>
        {
            var eligible = true;
            var sibling = new Window { Width = 200, Height = 100, ShowInTaskbar = false };
            System.ComponentModel.CancelEventHandler veto = (_, e) => e.Cancel = true;
            try
            {
                AttachmentPromptFocusReturn.Queue(owner, prompt, () => eligible);
                Assert.That(Keyboard.FocusedElement, Is.SameAs(previous));
                switch (change)
                {
                    case "context":
                        eligible = false;
                        break;
                    case "focus":
                    case "focus-round-trip":
                        Assert.That(Keyboard.Focus(other), Is.SameAs(other));
                        if (change == "focus-round-trip")
                            Assert.That(Keyboard.Focus(previous), Is.SameAs(previous));
                        break;
                    case "key":
                        InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice,
                            PresentationSource.FromVisual(previous)!, Environment.TickCount, Key.A)
                        { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                        break;
                    case "mouse":
                        InputManager.Current.ProcessInput(new MouseButtonEventArgs(Mouse.PrimaryDevice,
                            Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
                        break;
                    case "wheel":
                        InputManager.Current.ProcessInput(new MouseWheelEventArgs(Mouse.PrimaryDevice,
                            Environment.TickCount, 120) { RoutedEvent = Mouse.PreviewMouseWheelEvent });
                        break;
                    case "text":
                        InputManager.Current.ProcessInput(new TextCompositionEventArgs(Keyboard.PrimaryDevice,
                            new TextComposition(InputManager.Current, previous, "new input"))
                        { RoutedEvent = TextCompositionManager.PreviewTextInputEvent });
                        break;
                    case "rehost":
                        ((Panel)prompt.Parent).Children.Remove(prompt);
                        sibling.Content = prompt;
                        break;
                    case "hidden":
                        prompt.Visibility = Visibility.Hidden;
                        break;
                    case "disabled":
                        prompt.IsEnabled = false;
                        break;
                    case "not-focusable":
                        prompt.Focusable = false;
                        break;
                    case "closing":
                        owner.Closing += veto;
                        owner.Close();
                        Assert.That(owner.IsVisible, Is.True);
                        break;
                    case "foreground":
                        sibling.Show();
                        sibling.Activate();
                        Assert.That(NativeGetForegroundWindow(), Is.EqualTo(new WindowInteropHelper(sibling).Handle),
                            "The competing window must actually acquire foreground before testing loss of foreground.");
                        break;
                }
                var chosenFocus = Keyboard.FocusedElement;
                Assert.That(chosenFocus, Is.Not.SameAs(prompt));
                Pump();
                Assert.Multiple(() =>
                {
                    Assert.That(Keyboard.FocusedElement, Is.SameAs(chosenFocus),
                        $"The queued handoff must respect {change}.");
                    Assert.That(prompt.IsKeyboardFocused, Is.False);
                });
            }
            finally
            {
                owner.Closing -= veto;
                sibling.Close();
            }
        });
    }

    private static void WithForegroundOwner(Action<Window, RichTextBox, TextBox, TextBox> test)
    {
        var prompt = new RichTextBox();
        var previous = new TextBox();
        var other = new TextBox();
        var panel = new StackPanel();
        panel.Children.Add(previous);
        panel.Children.Add(other);
        panel.Children.Add(prompt);
        var owner = new Window { Content = panel, Width = 300, Height = 200, ShowInTaskbar = false };
        try
        {
            owner.Show();
            owner.Activate();
            Keyboard.Focus(previous);
            Pump();
            var handle = new WindowInteropHelper(owner).Handle;
            if (handle == IntPtr.Zero || NativeGetForegroundWindow() != handle || !owner.IsActive)
                Assert.Inconclusive($"Native precondition unavailable: owner=0x{handle:X}, " +
                    $"foreground=0x{NativeGetForegroundWindow():X}. Queued focus behavior was not exercised.");
            Assert.That(Keyboard.FocusedElement, Is.SameAs(previous));
            test(owner, prompt, previous, other);
        }
        finally
        {
            owner.Close();
            Pump();
        }
    }

    private static void Pump() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static extern IntPtr NativeGetForegroundWindow();
}
