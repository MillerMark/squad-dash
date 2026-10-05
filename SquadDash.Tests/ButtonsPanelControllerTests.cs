using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
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

    [Test]
    public void ReorderDragVisuals_ApplyAndResetBorderAndImportantBorderOnly() {
        _button.BorderThickness = new Thickness(1, 2, 3, 4);
        _button.Padding = new Thickness(8);
        _button.Resources["InputBorder"] = Brushes.DimGray;
        _button.Resources["ImportantText"] = Brushes.OrangeRed;
        _button.SetResourceReference(Control.BorderBrushProperty, "InputBorder");
        if (_button.Content is not TextBlock)
            Assert.Fail("Button content should be a TextBlock.");
        var caption = (TextBlock)_button.Content;
        _button.Resources["LabelText"] = Brushes.DimGray;
        caption.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");

        InvokePrivate("ApplyDraggedButtonVisuals", _button);
        Assert.Multiple(() => {
            Assert.That(_button.BorderBrush, Is.EqualTo(Brushes.OrangeRed));
            Assert.That(_button.BorderThickness, Is.EqualTo(new Thickness(2, 4, 6, 8)));
            Assert.That(_button.Padding, Is.EqualTo(new Thickness(7, 6, 5, 4)));
            Assert.That(caption.Foreground, Is.EqualTo(Brushes.DimGray));
        });
        SetControllerState("_pressedButton", _button);
        InvokePrivate("ResetReorderVisuals");

        Assert.Multiple(() => {
            Assert.That(_button.BorderBrush, Is.EqualTo(Brushes.DimGray));
            Assert.That(_button.BorderThickness, Is.EqualTo(new Thickness(1, 2, 3, 4)));
            Assert.That(_button.Padding, Is.EqualTo(new Thickness(8)));
            Assert.That(caption.Foreground, Is.EqualTo(Brushes.DimGray));
        });
    }

    [Test]
    public void ReorderDragVisuals_ResetRestoresStyleBackedBorderValues() {
        _button.ClearValue(Control.BorderBrushProperty);
        _button.ClearValue(Control.BorderThicknessProperty);
        _button.ClearValue(Control.PaddingProperty);
        _button.Resources["ImportantText"] = Brushes.OrangeRed;

        InvokePrivate("ApplyDraggedButtonVisuals", _button);
        SetControllerState("_pressedButton", _button);
        InvokePrivate("ResetReorderVisuals");

        Assert.Multiple(() => {
            Assert.That(_button.ReadLocalValue(Control.BorderBrushProperty), Is.EqualTo(DependencyProperty.UnsetValue));
            Assert.That(_button.ReadLocalValue(Control.BorderThicknessProperty), Is.EqualTo(DependencyProperty.UnsetValue));
            Assert.That(_button.ReadLocalValue(Control.PaddingProperty), Is.EqualTo(DependencyProperty.UnsetValue));
        });
    }

    [Test]
    public void ReorderDragVisuals_ResetRestoresDynamicBorderBrushExpression() {
        _button.Resources["InputBorder"] = Brushes.DimGray;
        _button.Resources["ImportantText"] = Brushes.OrangeRed;
        _button.SetResourceReference(Control.BorderBrushProperty, "InputBorder");

        InvokePrivate("ApplyDraggedButtonVisuals", _button);
        SetControllerState("_pressedButton", _button);
        InvokePrivate("ResetReorderVisuals");
        Assert.That(_button.BorderBrush, Is.EqualTo(Brushes.DimGray));

        _button.Resources["InputBorder"] = Brushes.DodgerBlue;
        Assert.That(_button.BorderBrush, Is.EqualTo(Brushes.DodgerBlue),
            "Reset must restore the dynamic resource expression, not a resolved brush snapshot.");
    }

    [Test]
    public void GetDockWidthHint_UsesLongestVisibleCaptionAndOnlyEmptyFloor() {
        var shortCaption = new ButtonDefinition(Guid.NewGuid(),
            new ButtonContent("A", "tip", "echo never-run", ButtonCommandType.RawCommand), 0, 1);
        var plain = new ButtonDefinition(Guid.NewGuid(),
            new ButtonContent("WWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWW", "tip", "echo never-run", ButtonCommandType.RawCommand), 1, 1);
        var bold = new ButtonDefinition(Guid.NewGuid(),
            new ButtonContent("**WWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWW**", "tip", "echo never-run", ButtonCommandType.RawCommand), 2, 1);
        var fontSensitive = new ButtonDefinition(Guid.NewGuid(),
            new ButtonContent("iiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiiii", "tip", "echo never-run", ButtonCommandType.RawCommand), 3, 1);
        _controller.Refresh(new[] { shortCaption });
        var shortWidth = _controller.GetDockWidthHint() ?? 0;

        _controller.Refresh(new[] { plain });
        var plainWidth = _controller.GetDockWidthHint() ?? 0;

        _controller.Refresh(new[] { bold });
        var boldWidth = _controller.GetDockWidthHint() ?? 0;

        _controller.Refresh(new[] { fontSensitive });
        var renderedFontWidth = _controller.GetDockWidthHint() ?? 0;
        var renderedButton = _list.Children.OfType<Button>().Single();
        renderedButton.FontFamily = new FontFamily("Courier New");
        renderedButton.FontSize += 6;
        var overriddenFontWidth = _controller.GetDockWidthHint() ?? 0;

        _controller.Refresh(new[] { bold });
        _controller.SetFilter("not-present");
        var emptyFilteredWidth = _controller.GetDockWidthHint() ?? 0;

        Assert.Multiple(() => {
            Assert.That(shortWidth, Is.LessThan(185), "Non-empty lists must not be floored to the empty-state minimum.");
            Assert.That(emptyFilteredWidth, Is.EqualTo(185));
            Assert.That(boldWidth, Is.GreaterThan(plainWidth));
            Assert.That(overriddenFontWidth, Is.Not.EqualTo(renderedFontWidth), "Width hint must follow the current rendered font.");
        });
    }

    [Test]
    public void GetDockWidthHint_AtHintWidth_DoesNotWrapOrClipCaptionWithVisibleScrollbar() {
        var definitions = Enumerable.Range(0, 24).Select(index => new ButtonDefinition(
            Guid.NewGuid(),
            new ButtonContent(index == 0
                    ? "**WWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWW**"
                    : $"Button {index:D2}",
                "tip", "echo never-run", ButtonCommandType.RawCommand),
            index, 1)).ToArray();

        var list = new StackPanel();
        var panel = new Border {
            Width = 500,
            Height = 210,
            Padding = new Thickness(2, 8, 2, 12),
            BorderThickness = new Thickness(1)
        };
        var root = new Grid { Width = 500, Height = 210 };
        var scrollViewer = new ScrollViewer {
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Height = 190,
            Content = list
        };
        panel.Child = scrollViewer;
        root.Children.Add(panel);
        root.Resources["ThemedButtonStyle"] = CreateThemedButtonStyle();
        root.Resources["FontSizeNormal"] = 13d;

        var controller = new ButtonsPanelController(list, panel, new TextBox(), () => { }, _ => { }, _ => { }, _ => { });
        controller.Refresh(definitions);

        var widest = list.Children.OfType<Button>().Single(button => ReferenceEquals(button.Tag, definitions[0]));
        widest.FontFamily = new FontFamily("Courier New");
        widest.FontSize = 19;

        var hint = controller.GetDockWidthHint() ?? 0;
        panel.Width = hint;
        root.Width = hint;
        root.Measure(new Size(hint, root.Height));
        root.Arrange(new Rect(0, 0, hint, root.Height));
        root.UpdateLayout();

        var visibleButtons = list.Children.OfType<Button>().ToArray();
        var maxCaptionWidth = visibleButtons.Max(button => MeasureNaturalCaptionWidth((TextBlock)button.Content));
        Assert.That(hint, Is.EqualTo(Math.Ceiling(maxCaptionWidth + 30)),
            "Preferred width must remain exact caption width + 30.");

        foreach (var button in visibleButtons) {
            var caption = (TextBlock)button.Content;
            var naturalWidth = MeasureNaturalCaptionWidth(caption);
            Assert.That(caption.ActualWidth, Is.GreaterThanOrEqualTo(naturalWidth - 1),
                "At the returned minimum width, captions must not wrap or clip.");
        }
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

    private void InvokePrivate(string methodName, params object[] args) {
        var method = typeof(ButtonsPanelController).GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(ButtonsPanelController).FullName, methodName);
        method.Invoke(_controller, args);
    }

    private static double MeasureNaturalCaptionWidth(TextBlock caption) {
        caption.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return caption.DesiredSize.Width;
    }

    private static Style CreateThemedButtonStyle() {
        const string xaml = """
                            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                   xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                   TargetType="Button">
                                <Setter Property="Background" Value="Transparent" />
                                <Setter Property="Foreground" Value="Black" />
                                <Setter Property="BorderBrush" Value="Black" />
                                <Setter Property="BorderThickness" Value="1" />
                                <Setter Property="Padding" Value="8,4" />
                                <Setter Property="Template">
                                    <Setter.Value>
                                        <ControlTemplate TargetType="Button">
                                            <Border Background="{TemplateBinding Background}"
                                                    BorderBrush="{TemplateBinding BorderBrush}"
                                                    BorderThickness="{TemplateBinding BorderThickness}"
                                                    CornerRadius="3"
                                                    Padding="{TemplateBinding Padding}">
                                                <ContentPresenter HorizontalAlignment="Center"
                                                                  VerticalAlignment="Center"
                                                                  TextElement.Foreground="{TemplateBinding Foreground}" />
                                            </Border>
                                        </ControlTemplate>
                                    </Setter.Value>
                                </Setter>
                            </Style>
                            """;
        return (Style)XamlReader.Parse(xaml);
    }
}
