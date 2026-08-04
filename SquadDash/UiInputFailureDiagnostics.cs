using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace SquadDash;

/// <summary>
/// Captures the pointer route immediately before WPF changes keyboard focus so
/// framework-only focus failures can be tied back to the responsible SquadDash
/// control and native window state.
/// </summary>
internal static class UiInputFailureDiagnostics
{
    private static readonly object Sync = new();
    private static long _nextSequence;
    private static UiInputBreadcrumb? _lastPointerPress;

    internal static void RecordPreProcessInput(PreProcessInputEventArgs args)
    {
        try
        {
            if (args.StagingItem.Input is not MouseButtonEventArgs mouseArgs ||
                mouseArgs.ButtonState != MouseButtonState.Pressed)
            {
                return;
            }

            var directlyOver = mouseArgs.MouseDevice.DirectlyOver ?? Mouse.DirectlyOver;
            var originalSource = mouseArgs.OriginalSource as DependencyObject;
            var routeStart = originalSource ?? directlyOver as DependencyObject;
            var button = FindAncestor<ButtonBase>(routeStart);
            var window = FindOwningWindow(button ?? routeStart);
            var position = DescribePointerPosition(mouseArgs, window, routeStart);

            var breadcrumb = new UiInputBreadcrumb(
                Sequence: Interlocked.Increment(ref _nextSequence),
                CapturedAtUtc: DateTimeOffset.UtcNow,
                Input: $"mouse-{mouseArgs.ChangedButton.ToString().ToLowerInvariant()}-down timestamp={mouseArgs.Timestamp} {position}",
                OriginalSource: DescribeElement(originalSource),
                DirectlyOver: DescribeElement(directlyOver as DependencyObject),
                Button: DescribeElement(button),
                Route: DescribeRoute(routeStart),
                Window: DescribeWindow(window),
                PostProcessCompleted: false);

            lock (Sync)
                _lastPointerPress = breadcrumb;
        }
        catch
        {
            // Diagnostics must never interfere with input processing.
        }
    }

    internal static void RecordPostProcessInput(ProcessInputEventArgs args)
    {
        try
        {
            if (args.StagingItem.Input is not MouseButtonEventArgs mouseArgs ||
                mouseArgs.ButtonState != MouseButtonState.Pressed)
            {
                return;
            }

            lock (Sync)
            {
                if (_lastPointerPress is not null)
                    _lastPointerPress = _lastPointerPress with { PostProcessCompleted = true };
            }
        }
        catch
        {
            // Diagnostics must never interfere with input processing.
        }
    }

    internal static string BuildSnapshot(Exception exception, Application? application)
    {
        try
        {
            UiInputBreadcrumb? breadcrumb;
            lock (Sync)
                breadcrumb = _lastPointerPress;

            var builder = new StringBuilder();
            builder.AppendLine("UI input/focus diagnostic context:");
            builder.AppendLine($"Classification: {ClassifyExceptionText(exception.ToString())}");
            builder.AppendLine("Capture point: SquadDash DispatcherUnhandledException, before emergency save and error UI changed focus or window state.");
            builder.AppendLine("Interpretation: a framework-only stack does not establish a framework bug or harmlessness; correlate it with the input route and HWND state below.");

            if (breadcrumb is null)
            {
                builder.AppendLine("Last pointer press: unavailable");
            }
            else
            {
                var age = DateTimeOffset.UtcNow - breadcrumb.CapturedAtUtc;
                builder.AppendLine($"Last pointer press: sequence={breadcrumb.Sequence} ageMs={Math.Max(0, age.TotalMilliseconds):0} postProcessCompleted={breadcrumb.PostProcessCompleted}");
                builder.AppendLine($"  input={breadcrumb.Input}");
                builder.AppendLine($"  button={breadcrumb.Button}");
                builder.AppendLine($"  originalSource={breadcrumb.OriginalSource}");
                builder.AppendLine($"  directlyOver={breadcrumb.DirectlyOver}");
                builder.AppendLine($"  route={breadcrumb.Route}");
                builder.AppendLine($"  owningWindow={breadcrumb.Window}");
            }

            builder.AppendLine($"Current keyboard focus: {DescribeElement(Keyboard.FocusedElement as DependencyObject)}");
            builder.AppendLine($"Current mouse capture: {DescribeElement(Mouse.Captured as DependencyObject)}");
            builder.AppendLine($"Current directly-over: {DescribeElement(Mouse.DirectlyOver as DependencyObject)}");

            var dispatcher = application?.Dispatcher;
            builder.AppendLine(
                dispatcher is null
                    ? "Dispatcher: unavailable"
                    : $"Dispatcher: shutdownStarted={dispatcher.HasShutdownStarted} shutdownFinished={dispatcher.HasShutdownFinished}");

            AppendWindows(builder, application);
            AppendPresentationSources(builder);
            return builder.ToString().TrimEnd();
        }
        catch (Exception diagnosticException)
        {
            return $"UI input/focus diagnostic context unavailable: {diagnosticException.GetType().Name}: {Sanitize(diagnosticException.Message)}";
        }
    }

    internal static string ClassifyExceptionText(string exceptionText)
    {
        if (exceptionText.Contains("HwndKeyboardInputProvider", StringComparison.Ordinal) &&
            exceptionText.Contains("AcquireFocus", StringComparison.Ordinal))
        {
            return "WPF native keyboard-focus acquisition failed during input routing; use the button route and HWND state below to locate the application trigger.";
        }

        return "Unhandled dispatcher input failure; application trigger is not identifiable from the framework stack alone.";
    }

    internal static string DescribeElement(DependencyObject? element)
    {
        if (element is null)
            return "(none)";

        try
        {
            var parts = new List<string> { element.GetType().Name };
            if (element is FrameworkElement frameworkElement)
            {
                if (!string.IsNullOrWhiteSpace(frameworkElement.Name))
                    parts.Add($"name={Quote(frameworkElement.Name)}");

                var automationId = AutomationProperties.GetAutomationId(frameworkElement);
                if (!string.IsNullOrWhiteSpace(automationId))
                    parts.Add($"automationId={Quote(automationId)}");

                parts.Add($"loaded={frameworkElement.IsLoaded}");
                parts.Add($"visible={frameworkElement.IsVisible}");
                parts.Add($"enabled={frameworkElement.IsEnabled}");
                parts.Add($"focusWithin={frameworkElement.IsKeyboardFocusWithin}");
            }

            if (element is ContentControl contentControl && contentControl.Content is string content && !string.IsNullOrWhiteSpace(content))
                parts.Add($"content={Quote(content)}");

            return string.Join(" ", parts);
        }
        catch (Exception ex)
        {
            return $"{element.GetType().Name} (description failed: {ex.GetType().Name})";
        }
    }

    private static void AppendWindows(StringBuilder builder, Application? application)
    {
        builder.AppendLine("Application windows:");
        if (application is null)
        {
            builder.AppendLine("  (application unavailable)");
            return;
        }

        var count = 0;
        foreach (Window window in application.Windows)
        {
            builder.AppendLine($"  - {DescribeWindow(window)}");
            count++;
        }

        if (count == 0)
            builder.AppendLine("  (none)");
    }

    private static void AppendPresentationSources(StringBuilder builder)
    {
        builder.AppendLine("Presentation sources:");
        var count = 0;
        foreach (PresentationSource source in PresentationSource.CurrentSources)
        {
            try
            {
                if (source is HwndSource hwndSource)
                {
                    builder.AppendLine(
                        $"  - HwndSource hwnd={FormatHandle(hwndSource.Handle)} disposed={hwndSource.IsDisposed} " +
                        $"root={DescribeElement(hwndSource.RootVisual)}");
                }
                else
                {
                    builder.AppendLine($"  - {source.GetType().Name} root={DescribeElement(source.RootVisual)}");
                }
            }
            catch (Exception ex)
            {
                builder.AppendLine($"  - {source.GetType().Name} (description failed: {ex.GetType().Name})");
            }

            count++;
        }

        if (count == 0)
            builder.AppendLine("  (none)");
    }

    private static string DescribeWindow(Window? window)
    {
        if (window is null)
            return "(none)";

        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            var source = handle == IntPtr.Zero ? null : HwndSource.FromHwnd(handle);
            return $"{window.GetType().Name} title={Quote(window.Title)} hwnd={FormatHandle(handle)} " +
                   $"sourcePresent={source is not null} sourceDisposed={source?.IsDisposed.ToString() ?? "n/a"} " +
                   $"loaded={window.IsLoaded} visible={window.IsVisible} active={window.IsActive} enabled={window.IsEnabled} " +
                   $"focusWithin={window.IsKeyboardFocusWithin} state={window.WindowState} " +
                   $"bounds=({window.Left:0.#},{window.Top:0.#},{window.ActualWidth:0.#},{window.ActualHeight:0.#})";
        }
        catch (Exception ex)
        {
            return $"{window.GetType().Name} (description failed: {ex.GetType().Name}: {Sanitize(ex.Message)})";
        }
    }

    private static string DescribePointerPosition(MouseButtonEventArgs args, Window? window, DependencyObject? routeStart)
    {
        try
        {
            if (window is not null)
            {
                var windowPoint = args.GetPosition(window);
                var screenPoint = window.PointToScreen(windowPoint);
                return $"window=({windowPoint.X:0.#},{windowPoint.Y:0.#}) screen=({screenPoint.X:0.#},{screenPoint.Y:0.#})";
            }

            if (routeStart is Visual visual && routeStart is IInputElement inputElement)
            {
                var localPoint = args.GetPosition(inputElement);
                var screenPoint = visual.PointToScreen(localPoint);
                return $"local=({localPoint.X:0.#},{localPoint.Y:0.#}) screen=({screenPoint.X:0.#},{screenPoint.Y:0.#})";
            }
        }
        catch
        {
        }

        return "position=(unavailable)";
    }

    private static string DescribeRoute(DependencyObject? start)
    {
        var route = new List<string>();
        var current = start;
        for (var i = 0; current is not null && i < 10; i++)
        {
            route.Add(DescribeElement(current));
            current = GetParent(current);
        }

        return route.Count == 0 ? "(none)" : string.Join(" <- ", route);
    }

    private static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
    {
        var current = start;
        for (var i = 0; current is not null && i < 30; i++)
        {
            if (current is T match)
                return match;
            current = GetParent(current);
        }

        return null;
    }

    private static Window? FindOwningWindow(DependencyObject? element)
    {
        try
        {
            return element is null ? null : Window.GetWindow(element);
        }
        catch
        {
            return null;
        }
    }

    private static DependencyObject? GetParent(DependencyObject element)
    {
        try
        {
            if (element is Visual or Visual3D)
            {
                var visualParent = VisualTreeHelper.GetParent(element);
                if (visualParent is not null)
                    return visualParent;
            }

            if (element is ContentElement contentElement)
            {
                var contentParent = ContentOperations.GetParent(contentElement);
                if (contentParent is not null)
                    return contentParent;
            }

            return element switch
            {
                FrameworkElement frameworkElement => frameworkElement.Parent ?? frameworkElement.TemplatedParent,
                FrameworkContentElement frameworkContentElement => frameworkContentElement.Parent,
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    private static string Quote(string? value) => $"\"{Sanitize(value)}\"";

    private static string Sanitize(string? value)
    {
        var compact = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        return compact.Length <= 160 ? compact : compact[..157] + "...";
    }

    private static string FormatHandle(IntPtr handle) => handle == IntPtr.Zero ? "0" : $"0x{handle.ToInt64():X}";

    private sealed record UiInputBreadcrumb(
        long Sequence,
        DateTimeOffset CapturedAtUtc,
        string Input,
        string OriginalSource,
        string DirectlyOver,
        string Button,
        string Route,
        string Window,
        bool PostProcessCompleted);
}
