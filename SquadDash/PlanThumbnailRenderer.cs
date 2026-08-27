using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SquadDash;

/// <summary>
/// Renders a compact, read-only plan graph thumbnail using live WPF visuals.
/// </summary>
internal sealed class PlanThumbnailRenderer : IDisposable
{
    private const double BaseNodeWidth = 220;
    private const double BaseNodeHeight = 112;
    private const double BaseColumnSpacing = 360;
    private const double BaseRowSpacing = 152;
    private const double BaseBodyFontSize = 12;
    private const int DefaultTargetHeightPixels = 400;
    private static readonly TimeSpan CoalesceDelay = TimeSpan.FromMilliseconds(24);

    private readonly Dispatcher _dispatcher;
    private readonly double _targetHeightPixels;
    private readonly Border _graphHost;
    private readonly Canvas _graphCanvas;
    private readonly PlanGraphEdgeRenderingContract _edgeRenderingContract;
    private readonly Func<string, (ImageSource? Image, string Initial, Brush Accent)?>? _resolveAgentAvatar;
    private readonly object _requestGate = new();

    private CancellationTokenSource? _pendingRenderCts;
    private long _latestRequestVersion;
    private bool _disposed;

    internal PlanThumbnailRenderer(
        Dispatcher? dispatcher = null,
        double targetHeightPixels = DefaultTargetHeightPixels,
        double lineThicknessMultiplier = 1.0,
        Func<string, (ImageSource? Image, string Initial, Brush Accent)?>? resolveAgentAvatar = null)
    {
        _dispatcher = dispatcher ?? Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _targetHeightPixels = targetHeightPixels > 0 ? targetHeightPixels : DefaultTargetHeightPixels;
        _edgeRenderingContract = new PlanGraphEdgeRenderingContract(lineThicknessMultiplier);
        _resolveAgentAvatar = resolveAgentAvatar;
        _graphCanvas = new Canvas
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        _graphHost = new Border
        {
            Padding = new Thickness(10),
            Child = _graphCanvas,
            SnapsToDevicePixels = true,
        };
        CurrentImage = CreatePlaceholderBitmap();
    }

    internal ImageSource CurrentImage { get; private set; }

    internal event Action<ImageSource>? ImageUpdated;

    internal Task<ImageSource> RequestRenderAsync(Plan? plan, CancellationToken cancellationToken = default) =>
        RequestRenderAsync(plan is null ? null : PendingDecomposePlanAdapter.FromPlan(plan).Group, null, cancellationToken);

    internal Task<ImageSource> RequestRenderAsync(PendingDecomposePlan? pendingPlan, CancellationToken cancellationToken = default) =>
        RequestRenderAsync(pendingPlan?.Group, null, cancellationToken);

    internal Task<ImageSource> RequestRenderAsync(DecomposedTaskGroup? group, CancellationToken cancellationToken = default)
        => RequestRenderAsync(group, nudges: null, cancellationToken);

    internal Task<ImageSource> RequestRenderAsync(
        DecomposedTaskGroup? group,
        Dictionary<string, double>? nudges,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (group?.Tasks is not { Count: > 0 })
        {
            CurrentImage = CreatePlaceholderBitmap();
            ImageUpdated?.Invoke(CurrentImage);
            return Task.FromResult(CurrentImage);
        }

        CancellationToken token;
        long requestVersion;
        lock (_requestGate)
        {
            _pendingRenderCts?.Cancel();
            _pendingRenderCts?.Dispose();
            _pendingRenderCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            token = _pendingRenderCts.Token;
            requestVersion = ++_latestRequestVersion;
        }

        var requestedNudges = nudges is null
            ? null
            : new Dictionary<string, double>(nudges, StringComparer.Ordinal);
        return RenderCoalescedAsync(group, requestedNudges, requestVersion, token);
    }

    private async Task<ImageSource> RenderCoalescedAsync(
        DecomposedTaskGroup group,
        IReadOnlyDictionary<string, double>? nudges,
        long requestVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(CoalesceDelay, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return CurrentImage;
        }

        BitmapSource bitmap;
        try
        {
            bitmap = await _dispatcher.InvokeAsync(
                () => RenderOnUiThread(group, nudges, cancellationToken),
                DispatcherPriority.Render,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return CurrentImage;
        }
        catch (Exception ex)
        {
            SquadDashTrace.Write(TraceCategory.Inbox, $"Plan thumbnail render failed: {ex}");
            bitmap = CreatePlaceholderBitmap();
        }

        lock (_requestGate)
        {
            if (requestVersion != _latestRequestVersion)
                return CurrentImage;
        }

        CurrentImage = bitmap;
        ImageUpdated?.Invoke(bitmap);
        return bitmap;
    }

    private BitmapSource RenderOnUiThread(
        DecomposedTaskGroup group,
        IReadOnlyDictionary<string, double>? nudges,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (graphWidth, graphHeight) = BuildGraphVisualTree(group, nudges);
        if (graphWidth <= 0 || graphHeight <= 0)
            return CreatePlaceholderBitmap();

        _graphHost.Measure(new Size(graphWidth, graphHeight));
        _graphHost.Arrange(new Rect(0, 0, graphWidth, graphHeight));
        _graphHost.UpdateLayout();

        var pixelWidth = Math.Max(1, (int)Math.Ceiling(graphWidth));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(graphHeight));
        var sourceBitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
        sourceBitmap.Render(_graphHost);
        sourceBitmap.Freeze();

        var targetHeight = Math.Max(1, (int)Math.Round(_targetHeightPixels));
        if (pixelHeight == targetHeight)
            return sourceBitmap;

        var scale = targetHeight / (double)pixelHeight;
        var transformed = new TransformedBitmap(sourceBitmap, new ScaleTransform(scale, scale));
        transformed.Freeze();
        return transformed;
    }

    private (double Width, double Height) BuildGraphVisualTree(
        DecomposedTaskGroup group,
        IReadOnlyDictionary<string, double>? nudges)
    {
        _graphCanvas.Children.Clear();

        var tasks = group.Tasks.Where(task => !string.IsNullOrWhiteSpace(task.Id)).ToArray();
        if (tasks.Length == 0)
            return (0, 0);

        var scaleFactor = ResolveBodyFontSize() / BaseBodyFontSize;
        var nodeWidth = BaseNodeWidth * scaleFactor;
        var nodeHeight = BaseNodeHeight * scaleFactor;
        var columnSpacing = BaseColumnSpacing * scaleFactor;
        var rowSpacing = BaseRowSpacing * scaleFactor;
        var graphTop = 52 * scaleFactor;
        var avatarChipSize = _resolveAgentAvatar is not null
            ? Math.Round(BaseNodeHeight * 0.375 * scaleFactor)
            : 0.0;

        var levels = ValidationShieldPresenter.ComputeValidationAwareLevels(tasks, group.Validations ?? []);
        var originalIndexByTaskId = tasks
            .Select((task, index) => (task.Id, index))
            .ToDictionary(item => item.Id, item => item.index, StringComparer.Ordinal);

        var columns = tasks
            .GroupBy(task => levels[task.Id])
            .OrderBy(column => column.Key)
            .ToArray();
        var attachedValidationCountByTaskId = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var validation in group.Validations ?? [])
        {
            var taskAnchor = ValidationShieldPresenter.InferTaskAnchor(validation, tasks);
            if (taskAnchor?.TaskId is not { Length: > 0 } taskId ||
                !originalIndexByTaskId.ContainsKey(taskId))
                continue;
            attachedValidationCountByTaskId[taskId] =
                attachedValidationCountByTaskId.GetValueOrDefault(taskId) + 1;
        }

        var positions = new Dictionary<string, Point>(StringComparer.Ordinal);

        foreach (var column in columns)
        {
            var x = 42 * scaleFactor + column.Key * columnSpacing;
            var y = graphTop;

            var stageHeader = new TextBlock
            {
                Text = $"Stage {column.Key + 1}",
                Width = nodeWidth,
                TextAlignment = TextAlignment.Center,
                FontWeight = FontWeights.SemiBold,
                Foreground = ResolveBrush("TitleText", AgentStatusCard.IsDarkTheme ? Color.FromRgb(0xE5, 0xD5, 0xC0) : Color.FromRgb(0x53, 0x37, 0x1E)),
            };
            stageHeader.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeHeading");
            Canvas.SetLeft(stageHeader, x);
            Canvas.SetTop(stageHeader, 8 * scaleFactor);
            _graphCanvas.Children.Add(stageHeader);

            var orderedTasks = column.OrderBy(task => originalIndexByTaskId[task.Id]).ToArray();
            for (var row = 0; row < orderedTasks.Length; row++)
            {
                var task = orderedTasks[row];
                var hasAvatars = avatarChipSize > 0 && task.AgentAssignments is { Count: > 0 };
                if (hasAvatars)
                    y += avatarChipSize;

                var nudgedY = y;
                if (nudges?.TryGetValue(task.Id, out var nudgeLineHeights) == true &&
                    nudgeLineHeights != 0.0)
                {
                    var fontSizeBody = scaleFactor * BaseBodyFontSize;
                    nudgedY = y + nudgeLineHeights * fontSizeBody;
                    var minY = row == 0
                        ? graphTop
                        : positions[orderedTasks[row - 1].Id].Y + nodeHeight + nodeHeight * 0.1 * scaleFactor;
                    nudgedY = Math.Max(nudgedY, minY);
                }

                positions[task.Id] = new Point(x, nudgedY);
                var attachedCount = attachedValidationCountByTaskId.GetValueOrDefault(task.Id);
                y += ValidationShieldPresenter.ComputeAttachedTaskSpacing(
                    attachedCount, nodeHeight, rowSpacing, scaleFactor);
            }
        }

        foreach (var target in tasks.OrderBy(task => originalIndexByTaskId[task.Id]))
        {
            if (!positions.TryGetValue(target.Id, out var targetPosition))
                continue;

            var dependencyIndex = 0;
            foreach (var dependencyId in target.DependsOn.OrderBy(id => id, StringComparer.Ordinal))
            {
                if (!positions.TryGetValue(dependencyId, out var sourcePosition))
                    continue;

                var from = new Point(sourcePosition.X + nodeWidth, sourcePosition.Y + nodeHeight / 2.0);
                var to = new Point(targetPosition.X, targetPosition.Y + nodeHeight / 2.0);
                _edgeRenderingContract.AddConnector(
                    _graphCanvas,
                    from,
                    to,
                    arrowHead: true,
                    skipCount: dependencyIndex,
                    dashed: false);
                dependencyIndex++;
            }
        }

        foreach (var task in tasks.OrderBy(task => originalIndexByTaskId[task.Id]))
        {
            if (!positions.TryGetValue(task.Id, out var position))
                continue;

            var card = CreateTaskCard(task, nodeWidth, nodeHeight, scaleFactor);
            Canvas.SetLeft(card, position.X);
            Canvas.SetTop(card, position.Y);
            _graphCanvas.Children.Add(card);

            if (avatarChipSize > 0 && task.AgentAssignments is { Count: > 0 })
            {
                var avatarPanel = new StackPanel { Orientation = Orientation.Horizontal };
                foreach (var assignment in task.AgentAssignments)
                {
                    var info = _resolveAgentAvatar!(assignment.AgentHandle);
                    var chip = CreateAvatarChip(info, avatarChipSize, assignment.AgentHandle, scaleFactor);
                    avatarPanel.Children.Add(chip);
                }
                Canvas.SetLeft(avatarPanel, position.X + 4 * scaleFactor);
                Canvas.SetTop(avatarPanel, position.Y - avatarChipSize);
                Panel.SetZIndex(avatarPanel, 35);
                _graphCanvas.Children.Add(avatarPanel);
            }
        }

        var maxX = positions.Values.Max(point => point.X);
        var maxY = positions.Values.Max(point => point.Y);
        var contentWidth = maxX + nodeWidth + 52 * scaleFactor;
        var contentHeight = maxY + nodeHeight + 28 * scaleFactor;

        _graphCanvas.Width = contentWidth;
        _graphCanvas.Height = contentHeight;
        _graphHost.Width = contentWidth + _graphHost.Padding.Left + _graphHost.Padding.Right;
        _graphHost.Height = contentHeight + _graphHost.Padding.Top + _graphHost.Padding.Bottom;
        _graphHost.Background = ResolveBrush("PlanStageCanvasBackground", AgentStatusCard.IsDarkTheme ? Color.FromRgb(0x1D, 0x1A, 0x18) : Color.FromRgb(0xFA, 0xF7, 0xF2));
        _graphHost.BorderBrush = ResolveBrush("SubtleBorder", AgentStatusCard.IsDarkTheme ? Color.FromRgb(0x47, 0x3D, 0x35) : Color.FromRgb(0xD7, 0xC7, 0xB5));
        _graphHost.BorderThickness = new Thickness(1);
        return (_graphHost.Width, _graphHost.Height);
    }

    private Border CreateTaskCard(DecomposedSubTask task, double width, double height, double scaleFactor)
    {
        var titleText = string.IsNullOrWhiteSpace(task.Title)
            ? task.Id
            : task.Title!;
        var description = task.Description ?? string.Empty;
        if (description.Length > 140)
            description = description[..137] + "...";

        var stack = new StackPanel
        {
            Margin = new Thickness(8 * scaleFactor, 7 * scaleFactor, 8 * scaleFactor, 6 * scaleFactor),
        };

        var title = new TextBlock
        {
            Text = titleText,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontWeight = FontWeights.SemiBold,
            Foreground = ResolveBrush("ImportantText", AgentStatusCard.IsDarkTheme ? Color.FromRgb(0xE5, 0xD5, 0xC0) : Color.FromRgb(0x53, 0x37, 0x1E)),
        };
        title.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeNormal");
        stack.Children.Add(title);

        if (!string.IsNullOrWhiteSpace(description))
        {
            var body = new TextBlock
            {
                Text = description,
                Margin = new Thickness(0, 4 * scaleFactor, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 44 * scaleFactor,
                Foreground = ResolveBrush("LabelText", AgentStatusCard.IsDarkTheme ? Color.FromRgb(0xD8, 0xC8, 0xB0) : Color.FromRgb(0x3C, 0x2B, 0x1E)),
            };
            body.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
            stack.Children.Add(body);
        }

        return new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(8 * scaleFactor),
            BorderThickness = new Thickness(1),
            BorderBrush = ResolveBrush("SubtleBorder", AgentStatusCard.IsDarkTheme ? Color.FromRgb(0x47, 0x3D, 0x35) : Color.FromRgb(0xD7, 0xC7, 0xB5)),
            Background = ResolveBrush("CardSurface", AgentStatusCard.IsDarkTheme ? Color.FromRgb(0x24, 0x20, 0x1D) : Color.FromRgb(0xFF, 0xFB, 0xF5)),
            Child = stack,
        };
    }

    private FrameworkElement CreateAvatarChip(
        (ImageSource? Image, string Initial, Brush Accent)? info,
        double chipSize,
        string agentHandle,
        double scaleFactor)
    {
        UIElement content;
        if (info?.Image is { } image)
        {
            var cropFraction = 0.15;
            var innerScale = 1.0 / (1.0 - 2 * cropFraction);
            var innerSize = chipSize * innerScale;
            var offset = (innerSize - chipSize) / 2;
            var img = new Image
            {
                Source = image,
                Width = innerSize,
                Height = innerSize,
                Stretch = Stretch.UniformToFill,
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            var clipGrid = new Grid
            {
                Width = chipSize - 4,
                Height = chipSize - 4,
                ClipToBounds = true,
            };
            img.Margin = new Thickness(-offset);
            clipGrid.Children.Add(img);
            content = clipGrid;
        }
        else
        {
            var initial = info?.Initial ?? (agentHandle.Length > 0
                ? agentHandle[0].ToString().ToUpperInvariant()
                : "?");
            var tb = new TextBlock
            {
                Text = initial,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold,
                FontSize = chipSize * 0.5,
            };
            if (info?.Accent is { } accent)
                tb.Foreground = accent;
            else
                tb.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
            content = tb;
        }

        var chip = new Border
        {
            Width = chipSize,
            Height = chipSize,
            CornerRadius = new CornerRadius(3),
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Gray,
            ClipToBounds = true,
            Margin = new Thickness(0, 0, 3 * scaleFactor, 0),
            Child = content is Grid ? content : new Viewbox { Child = (UIElement)content },
        };
        chip.SetResourceReference(Border.BackgroundProperty, "CardSurface");
        return chip;
    }

    private BitmapSource CreatePlaceholderBitmap()
    {
        var height = Math.Max(1, (int)Math.Round(_targetHeightPixels));
        var width = Math.Max(1, (int)Math.Round(height * 1.6));
        var background = ResolveColor("CardSurface", AgentStatusCard.IsDarkTheme ? Color.FromRgb(0x24, 0x20, 0x1D) : Color.FromRgb(0xF4, 0xEE, 0xE5));
        var border = ResolveColor("SubtleBorder", AgentStatusCard.IsDarkTheme ? Color.FromRgb(0x47, 0x3D, 0x35) : Color.FromRgb(0xD7, 0xC7, 0xB5));
        var text = ResolveColor("SubtleText", AgentStatusCard.IsDarkTheme ? Color.FromRgb(0xB2, 0xA7, 0x99) : Color.FromRgb(0x6F, 0x60, 0x51));

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(background), new Pen(new SolidColorBrush(border), 1), new Rect(0, 0, width, height));
            var message = "Rendering plan thumbnail...";
            var formatted = new FormattedText(
                message,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                14,
                new SolidColorBrush(text),
                1.0);
            var point = new Point(
                Math.Max(10, (width - formatted.Width) / 2),
                Math.Max(10, (height - formatted.Height) / 2));
            context.DrawText(formatted, point);
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private double ResolveBodyFontSize() =>
        Application.Current?.Resources["FontSizeBody"] is double size && size > 0
            ? size
            : BaseBodyFontSize;

    private Brush ResolveBrush(string resourceKey, Color fallbackColor)
    {
        if (Application.Current?.Resources[resourceKey] is Brush brush)
            return brush;
        return new SolidColorBrush(fallbackColor);
    }

    private Color ResolveColor(string resourceKey, Color fallbackColor)
    {
        if (Application.Current?.Resources[resourceKey] is SolidColorBrush brush)
            return brush.Color;
        return fallbackColor;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PlanThumbnailRenderer));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        lock (_requestGate)
        {
            _pendingRenderCts?.Cancel();
            _pendingRenderCts?.Dispose();
            _pendingRenderCts = null;
        }
    }
}
