using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace SquadDash;

internal static class AttachmentPromptFocusReturn
{
    internal static void Queue(Window invokingOwner, RichTextBox prompt, Func<bool> canReturnFocus)
    {
        var dispatcher = prompt.Dispatcher;
        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished ||
            !canReturnFocus() || !IsAvailable(invokingOwner) ||
            !WindowHelper.IsForegroundWindow(invokingOwner))
        {
            TraceSkipped();
            return;
        }

        var promptHost = Window.GetWindow(prompt);
        var previousFocus = Keyboard.FocusedElement;
        // Finish native destruction and the handled OK/Enter input route first.
        // A later user focus choice always wins over this one-shot handoff.
        QueueIfUninterrupted(invokingOwner, dispatcher, () =>
        {
            if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished ||
                !canReturnFocus() || !IsAvailable(invokingOwner) ||
                !WindowHelper.IsForegroundWindow(invokingOwner) ||
                !ReferenceEquals(Keyboard.FocusedElement, previousFocus) ||
                !IsAvailable(promptHost) || !ReferenceEquals(Window.GetWindow(prompt), promptHost) ||
                !prompt.IsLoaded || !prompt.IsVisible || !prompt.IsEnabled || !prompt.Focusable)
            {
                TraceSkipped();
                return;
            }

            // The transcript/attachment owner and the prompt can be in different
            // floating hosts. Never substitute the main window or another text box.
            if (!ReferenceEquals(promptHost, invokingOwner) && !promptHost!.Activate())
            {
                SquadDashTrace.Write(TraceCategory.UI, "Attachment notes accepted: prompt host activation was declined.");
                return;
            }
            if (!canReturnFocus() || !WindowHelper.IsForegroundWindow(promptHost!))
            {
                TraceSkipped();
                return;
            }

            FocusManager.SetFocusedElement(FocusManager.GetFocusScope(prompt), prompt);
            if (!ReferenceEquals(Keyboard.Focus(prompt), prompt))
                SquadDashTrace.Write(TraceCategory.UI, "Attachment notes accepted: prompt keyboard focus was declined.");
        });
    }

    // Keep pending-operation cancellation independent of native foreground eligibility.
    internal static DispatcherOperation QueueIfUninterrupted(
        Window invokingOwner, Dispatcher dispatcher, Action returnFocus)
    {
        dispatcher.VerifyAccess();
        invokingOwner.VerifyAccess();
        var inputManager = InputManager.Current;
        DispatcherOperation? operation = null;

        void Detach()
        {
            invokingOwner.Deactivated -= OnInterrupted;
            invokingOwner.Closing -= OnClosing;
            dispatcher.ShutdownStarted -= OnInterrupted;
            inputManager.PreProcessInput -= OnInput;
            if (operation is not null)
                operation.Aborted -= OnAborted;
        }
        void Cancel()
        {
            Detach();
            operation?.Abort();
            TraceSkipped();
        }
        void OnInterrupted(object? sender, EventArgs e) => Cancel();
        void OnClosing(object? sender, CancelEventArgs e) => Cancel();
        void OnAborted(object? sender, EventArgs e)
        {
            Detach();
            TraceSkipped();
        }
        void OnInput(object sender, PreProcessInputEventArgs e)
        {
            var input = e.StagingItem.Input;
            var routedEvent = input.RoutedEvent;
            // Observe committed transitions, not just the focus identity at dispatch:
            // a programmatic A -> B -> A round trip must still cancel the handoff.
            if ((input is KeyboardFocusChangedEventArgs focus &&
                 (routedEvent == Keyboard.GotKeyboardFocusEvent ||
                  routedEvent == Keyboard.LostKeyboardFocusEvent) &&
                 !ReferenceEquals(focus.OldFocus, focus.NewFocus)) ||
                routedEvent == Keyboard.PreviewKeyDownEvent ||
                routedEvent == Mouse.PreviewMouseDownEvent ||
                routedEvent == Mouse.PreviewMouseWheelEvent ||
                routedEvent == TextCompositionManager.PreviewTextInputEvent)
                Cancel();
        }

        operation = dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            Detach();
            returnFocus();
        }));
        invokingOwner.Deactivated += OnInterrupted;
        invokingOwner.Closing += OnClosing;
        dispatcher.ShutdownStarted += OnInterrupted;
        inputManager.PreProcessInput += OnInput;
        operation.Aborted += OnAborted;
        if (operation.Status == DispatcherOperationStatus.Aborted)
            OnAborted(operation, EventArgs.Empty);
        return operation;
    }

    private static bool IsAvailable(Window? window) =>
        window is { IsVisible: true, IsEnabled: true } && window.WindowState != WindowState.Minimized;

    private static void TraceSkipped() =>
        SquadDashTrace.Write(TraceCategory.UI, "Attachment notes accepted: prompt focus return no longer eligible.");
}
