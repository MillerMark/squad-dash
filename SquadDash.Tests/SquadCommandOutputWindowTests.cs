using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SquadDash.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
internal sealed class SquadCommandOutputWindowTests {
    [Test]
    public void ProgressBar_RendersThemeTroughAndAnimatedAccentAcrossThemeChanges() {
        var window = CreateWindow();
        try {
            window.Show();
            var bar = Descendants<ProgressBar>(window).Single();
            Color? previousTrough = null;
            foreach (var theme in new[] { "Dark", "Light", "Dark" }) {
                window.Resources.MergedDictionaries.Clear();
                window.Resources.MergedDictionaries.Add(LoadTheme(theme));
                window.UpdateLayout();
                Pump(TimeSpan.FromMilliseconds(350));

                var trough = ((SolidColorBrush)window.FindResource("TrackSurface")).Color;
                var accent = ((SolidColorBrush)window.FindResource("ActionLinkText")).Color;
                var movingIndicator = (Border)bar.Template.FindName("MovingIndicator", bar);
                var transform = (TranslateTransform)movingIndicator.RenderTransform;
                var pixels = RenderPixels(bar);
                Assert.Multiple(() => {
                    Assert.That(trough, Is.Not.EqualTo(previousTrough), "Switching themes must recolor the rendered trough.");
                    Assert.That(CountColor(pixels, trough), Is.GreaterThan(600), "The actual pixels, not just Background, must use the theme trough.");
                    Assert.That(CountColor(pixels, accent), Is.GreaterThan(50), "The moving bar must render the theme accent.");
                    Assert.That(transform.HasAnimatedProperties, Is.True);
                });
                var position = transform.X;
                Pump(TimeSpan.FromMilliseconds(100));
                Assert.That(transform.X, Is.Not.EqualTo(position), "The indeterminate bar must keep moving.");
                previousTrough = trough;
            }
        }
        finally {
            window.Close();
        }
    }

    [Test]
    public void LiveOutput_ReplacesPlaceholderScrollsAndIgnoresLateCallbacksAfterCompletion() {
        var window = CreateWindow();
        try {
            window.Show();
            window.AppendOutputLine("first");
            for (var i = 0; i < 100; i++)
                window.AppendOutputLine($"line {i}");
            window.UpdateLayout();
            Pump(TimeSpan.FromMilliseconds(50));
            var output = Descendants<RichTextBox>(window).Single();
            Assert.Multiple(() => {
                Assert.That(GetText(output), Does.StartWith("first" + Environment.NewLine));
                Assert.That(GetText(output), Does.Not.Contain("Executing command"));
                Assert.That(GetText(output), Does.EndWith("line 99" + Environment.NewLine));
                Assert.That(output.VerticalOffset, Is.GreaterThan(0));
                Assert.That(output.VerticalOffset + output.ViewportHeight,
                    Is.GreaterThanOrEqualTo(output.ExtentHeight - 1));
            });

            window.SetBusy(false);
            window.SetOutput("Exit code: 7" + Environment.NewLine + "Execution error: failed");
            window.AppendOutputLine("late notification");
            Assert.That(GetText(output), Is.EqualTo("Exit code: 7" + Environment.NewLine + "Execution error: failed"));
        }
        finally {
            window.Close();
        }
    }

    [Test]
    public void BusyLabel_UpdatesElapsedTimeAndStopsOnCompletionAndClose() {
        var window = CreateWindow();
        try {
            window.Show();
            var label = Descendants<TextBlock>(window).Single(text => text.Text.StartsWith("Executing command"));
            var timer = (DispatcherTimer)typeof(SquadCommandOutputWindow)
                .GetField("_elapsedTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            Assert.That(label.Text, Is.EqualTo("Executing command\u2026 0:00"));
            PumpUntil(() => label.Text != "Executing command\u2026 0:00");
            Assert.That(label.Text, Does.StartWith("Executing command\u2026 0:"));

            window.SetBusy(false);
            Assert.Multiple(() => {
                Assert.That(timer.IsEnabled, Is.False);
                Assert.That(label.Text, Is.Empty);
                Assert.That(label.Visibility, Is.EqualTo(Visibility.Collapsed));
            });
            window.SetBusy(true);
            Assert.That(timer.IsEnabled, Is.True);
            window.Close();
            window.SetBusy(true);
            Assert.Multiple(() => {
                Assert.That(timer.IsEnabled, Is.False);
                Assert.That(label.Text, Is.Empty);
            });
        }
        finally {
            window.Close();
        }
    }

    [Test]
    public void Progress_FromWorkerThreadAppendsOnWindowDispatcher() {
        var window = CreateWindow();
        var previousContext = SynchronizationContext.Current;
        try {
            window.Show();
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
            var received = false;
            var progress = new Progress<string>(line => {
                window.Dispatcher.VerifyAccess();
                window.AppendOutputLine(line);
                received = true;
            });
            Task.Run(() => ((IProgress<string>)progress).Report("worker output")).GetAwaiter().GetResult();
            PumpUntil(() => received);
            Assert.That(GetText(Descendants<RichTextBox>(window).Single()), Is.EqualTo("worker output" + Environment.NewLine));
        }
        finally {
            SynchronizationContext.SetSynchronizationContext(previousContext);
            window.Close();
        }
    }

    private static SquadCommandOutputWindow CreateWindow() {
        var window = new SquadCommandOutputWindow("Command feedback", "Executing command\u2026", isBusy: true) {
            ShowActivated = false,
            ShowInTaskbar = false
        };
        window.Resources.MergedDictionaries.Add(LoadTheme("Dark"));
        window.Resources["MonoFont"] = new FontFamily("Consolas");
        window.Resources["FontSizeNormal"] = 14.0;
        return window;
    }

    private static ResourceDictionary LoadTheme(string theme) {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "SquadDash")))
            directory = directory.Parent;
        var source = directory?.FullName ?? throw new DirectoryNotFoundException("SquadDash source not found.");
        using var stream = File.OpenRead(Path.Combine(source, "SquadDash", "Themes", theme + ".xaml"));
        return (ResourceDictionary)XamlReader.Load(stream);
    }

    // TextRange always serializes a trailing "\r\n" for the implicit paragraph/document end,
    // regardless of what was actually appended; strip exactly that one so assertions can compare
    // against the text that was actually written (mirroring the old TextBox.Text semantics).
    private static string GetText(RichTextBox box) {
        var text = new TextRange(box.Document.ContentStart, box.Document.ContentEnd).Text;
        return text.EndsWith("\r\n", StringComparison.Ordinal) ? text[..^2] : text;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static byte[] RenderPixels(FrameworkElement element) {
        var width = (int)element.ActualWidth;
        var height = (int)element.ActualHeight;
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }

    private static int CountColor(byte[] pixels, Color color) {
        var count = 0;
        for (var i = 0; i < pixels.Length; i += 4)
            if (pixels[i] == color.B && pixels[i + 1] == color.G && pixels[i + 2] == color.R && pixels[i + 3] == color.A)
                count++;
        return count;
    }

    private static void Pump(TimeSpan duration) {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
        timer.Tick += (_, _) => {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void PumpUntil(Func<bool> condition) {
        var elapsed = Stopwatch.StartNew();
        while (!condition() && elapsed.Elapsed < TimeSpan.FromSeconds(5))
            Pump(TimeSpan.FromMilliseconds(20));
        Assert.That(condition(), Is.True, "The dispatcher did not deliver the expected update.");
    }
}
