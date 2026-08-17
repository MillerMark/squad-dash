using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace SquadDash;

/// <summary>
/// Themed, readable confirmation for AI-recommended changes to a human-review contract.
/// </summary>
internal sealed class HumanReviewClarificationDialog : ChromedWindow
{
    private HumanReviewClarificationDialog(HumanReviewClarificationDialogPresentation presentation)
        : base(captionHeight: 34, resizeMode: ResizeMode.CanResizeWithGrip)
    {
        Title = "Update Human Review";
        Width = 680;
        Height = 540;
        MinWidth = 520;
        MinHeight = 400;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var contentArea = ApplyOuterBorder("AppSurface", Title);
        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentArea.Child = root;

        var document = BuildDocument(presentation);
        var viewer = new FlowDocumentScrollViewer
        {
            Document = document,
            IsToolBarVisible = false,
            IsSelectionEnabled = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(14, 12, 14, 12),
        };
        viewer.SetResourceReference(FlowDocumentScrollViewer.BackgroundProperty, "CardSurface");
        viewer.SetResourceReference(FlowDocumentScrollViewer.ForegroundProperty, "BodyText");

        var viewerBorder = new Border
        {
            Child = viewer,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            ClipToBounds = true,
        };
        viewerBorder.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");
        Grid.SetRow(viewerBorder, 0);
        root.Children.Add(viewerBorder);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        Grid.SetRow(buttons, 1);
        root.Children.Add(buttons);

        var keepButton = CreateActionButton(
            presentation.SecondaryActionLabel, isDefault: false, isCancel: true);
        keepButton.Margin = new Thickness(0, 0, 12, 0);
        keepButton.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };
        buttons.Children.Add(keepButton);

        var applyButton = CreateActionButton(
            presentation.PrimaryActionLabel, isDefault: true, isCancel: false);
        applyButton.Click += (_, _) =>
        {
            DialogResult = true;
            Close();
        };
        buttons.Children.Add(applyButton);

        PreviewKeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape) return;
            DialogResult = false;
            Close();
        };
    }

    internal static bool Show(
        Window owner,
        PlanHumanReviewClarificationResponse response,
        PlanHumanReviewChecklistItem item)
    {
        var presentation = HumanReviewClarificationDialogPresentation.Create(response, item);
        return Show(owner, presentation);
    }

    internal static bool Show(Window owner, HumanReviewClarificationDialogPresentation presentation)
    {
        var dialog = new HumanReviewClarificationDialog(presentation) { Owner = owner };
        return dialog.ShowDialog() == true;
    }

    private static Button CreateActionButton(string label, bool isDefault, bool isCancel)
    {
        var button = new Button
        {
            Content = label,
            MinWidth = 170,
            Height = 42,
            Padding = new Thickness(18, 0, 18, 0),
            IsDefault = isDefault,
            IsCancel = isCancel,
        };
        button.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        button.SetResourceReference(Control.FontSizeProperty, "FontSizeBody");
        return button;
    }

    private static FlowDocument BuildDocument(HumanReviewClarificationDialogPresentation presentation)
    {
        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            TextAlignment = TextAlignment.Left,
        };
        document.SetResourceReference(FlowDocument.ForegroundProperty, "BodyText");
        document.SetResourceReference(FlowDocument.FontSizeProperty, "FontSizeBody");

        AddHeading(document, "Current requirement");
        AddMarkdown(document, presentation.CurrentRequirement);

        AddHeading(document, "Proposed change");
        document.Blocks.Add(new BlockUIContainer(BuildChangeBadge(presentation))
        {
            Margin = new Thickness(0, 2, 0, 8),
        });
        AddMarkdown(document, presentation.ProposedChange);

        AddHeading(document, "Reason");
        AddMarkdown(document, presentation.Reason);
        return document;
    }

    private static void AddHeading(FlowDocument document, string text)
    {
        var heading = new Paragraph(new Run(text))
        {
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, document.Blocks.Count == 0 ? 0 : 16, 0, 6),
        };
        heading.SetResourceReference(TextElement.FontSizeProperty, "FontSizeMedium");
        heading.SetResourceReference(TextElement.ForegroundProperty, "LabelText");
        document.Blocks.Add(heading);
    }

    private static void AddMarkdown(FlowDocument destination, string markdown)
    {
        var source = MarkdownFlowDocumentBuilder.BuildInbox(markdown);
        while (source.Blocks.FirstBlock is { } block)
        {
            source.Blocks.Remove(block);
            block.Margin = new Thickness(0, 0, 0, 4);
            destination.Blocks.Add(block);
        }
    }

    private static FrameworkElement BuildChangeBadge(HumanReviewClarificationDialogPresentation presentation)
    {
        var icon = BuildChangeIcon(presentation.ChangeIcon);
        var label = new TextBlock
        {
            Text = presentation.PrimaryActionLabel,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(9, 0, 0, 0),
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "LabelText");
        label.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBody");

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(icon);
        row.Children.Add(label);
        return row;
    }

    private static FrameworkElement BuildChangeIcon(HumanReviewClarificationChangeIcon kind)
    {
        const double size = 28;
        var resourceKey = kind switch
        {
            HumanReviewClarificationChangeIcon.Edit => "ToolIcon_edit",
            HumanReviewClarificationChangeIcon.Remove => "ImageEditorXIcon",
            HumanReviewClarificationChangeIcon.Check => "PlanApprovalResolvedCheck",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
        var resource = Application.Current.TryFindResource(resourceKey);
        if (resource is ImageSource imageSource)
            return new Image { Source = imageSource, Width = size, Height = size, Stretch = Stretch.Uniform };
        if (resource is FrameworkElement element)
        {
            element.Width = size;
            element.Height = size;
            element.VerticalAlignment = VerticalAlignment.Center;
            return element;
        }

        return new TextBlock
        {
            Text = kind == HumanReviewClarificationChangeIcon.Remove ? "✕" : "✓",
            FontSize = 22,
            Width = size,
            Height = size,
            TextAlignment = TextAlignment.Center,
        };
    }
}
