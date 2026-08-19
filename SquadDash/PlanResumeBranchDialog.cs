using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SquadDash;

internal sealed class PlanResumeBranchDialog : ChromedWindow
{
    internal PlanResumeBranchAction Choice { get; private set; } = PlanResumeBranchAction.Cancel;

    internal PlanResumeBranchDialog(PlanResumeBranchSituation situation, Window? owner = null)
        : base(captionHeight: 36, resizeMode: ResizeMode.NoResize)
    {
        Title = "Choose Which Work to Resume With";
        Width = 700;
        SizeToContent = SizeToContent.Height;
        MinWidth = 580;
        MaxHeight = 760;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (owner is not null) Owner = owner;

        var root = new StackPanel { Margin = new Thickness(24) };
        var outerBorder = ApplyOuterBorder();
        outerBorder.Child = root;

        var heading = new TextBlock
        {
            Text = "This plan belongs to a different branch",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6),
        };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "ImportantText");
        root.Children.Add(heading);

        var intro = new TextBlock
        {
            Text = "Before Resume changes branches, choose whether the work you have now should come with you.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 18),
        };
        intro.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        root.Children.Add(intro);

        var branchGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        branchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        branchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) });
        branchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        branchGrid.Children.Add(BuildBranchCard(
            "YOU ARE HERE",
            situation.CurrentBranch,
            ShortCommit(situation.CurrentHead),
            "Current work",
            0));

        var relationshipMark = new TextBlock
        {
            Text = situation.Relationship == PlanResumeBranchRelationship.Diverged ? "↙  ↘" : "⟷",
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        relationshipMark.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        Grid.SetColumn(relationshipMark, 1);
        branchGrid.Children.Add(relationshipMark);

        branchGrid.Children.Add(BuildBranchCard(
            "PLAN RESUMES HERE",
            situation.PlanBranch,
            ShortCommit(situation.PlanHead),
            "Plan branch",
            2));
        root.Children.Add(branchGrid);

        var relationship = new Border
        {
            CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 0, 0, 18),
            Child = new TextBlock
            {
                Text = RelationshipText(situation),
                TextWrapping = TextWrapping.Wrap,
            },
        };
        relationship.SetResourceReference(Border.BackgroundProperty, "PanelBackground");
        relationship.SetResourceReference(Border.BorderBrushProperty, "LineColor");
        ((TextBlock)relationship.Child).SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        root.Children.Add(relationship);

        if (situation.Relationship is PlanResumeBranchRelationship.CurrentAhead or
            PlanResumeBranchRelationship.Diverged)
        {
            var bringLabel = situation.Relationship == PlanResumeBranchRelationship.CurrentAhead
                ? "Bring Current Work & Resume"
                : "Combine Branches & Resume…";
            var bringDetail = situation.Relationship == PlanResumeBranchRelationship.CurrentAhead
                ? $"Fast-forward the plan branch by {situation.CurrentOnlyCommitCount} commit(s), then resume there. No merge conflict is possible."
                : "Merge the current branch into the plan branch, then resume. If Git reports a conflict, SquadDash will abort the merge and leave both branches unchanged.";
            var bring = BuildChoiceButton(
                bringLabel,
                bringDetail,
                recommended: PlanResumeBranchPolicy.RecommendedAction(situation) == PlanResumeBranchAction.BringCurrentWork);
            bring.IsEnabled = situation.CanBringCurrentWork;
            if (!bring.IsEnabled)
                bring.ToolTip = ToolTipHelper.MakeThemedToolTip(
                    "Commit or stash the uncommitted files before combining diverged branches.");
            bring.Click += (_, _) => Complete(PlanResumeBranchAction.BringCurrentWork);
            root.Children.Add(bring);
        }

        var switchLabel = situation.Relationship is PlanResumeBranchRelationship.PlanAhead or
            PlanResumeBranchRelationship.SameCommit
                ? "Switch to Plan Branch & Resume"
                : "Switch Without Combining";
        var switchDetail = situation.Relationship switch
        {
            PlanResumeBranchRelationship.PlanAhead =>
                "The plan branch already contains the current branch's commits. Switch and resume.",
            PlanResumeBranchRelationship.SameCommit =>
                "Both branch names point to the same commit. Switch names and resume.",
            _ => $"Resume on {situation.PlanBranch}. Current work remains preserved on {situation.CurrentBranch}.",
        };
        var switchOnly = BuildChoiceButton(
            switchLabel,
            switchDetail,
            recommended: PlanResumeBranchPolicy.RecommendedAction(situation) == PlanResumeBranchAction.SwitchOnly);
        switchOnly.Click += (_, _) => Complete(PlanResumeBranchAction.SwitchOnly);
        root.Children.Add(switchOnly);

        var cancel = new Button
        {
            Content = "Cancel",
            Width = 100,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
            IsCancel = true,
        };
        cancel.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        cancel.Click += (_, _) => Complete(PlanResumeBranchAction.Cancel);
        root.Children.Add(cancel);
    }

    private static Border BuildBranchCard(
        string eyebrow,
        string branch,
        string commit,
        string description,
        int column)
    {
        var content = new StackPanel();
        var eyebrowText = new TextBlock
        {
            Text = eyebrow,
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 5),
        };
        eyebrowText.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        content.Children.Add(eyebrowText);

        var branchText = new TextBlock
        {
            Text = branch,
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };
        branchText.SetResourceReference(TextBlock.ForegroundProperty, "ImportantText");
        content.Children.Add(branchText);

        var detail = new TextBlock
        {
            Text = $"{description}  •  {commit}",
            Margin = new Thickness(0, 5, 0, 0),
        };
        detail.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        content.Children.Add(detail);

        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1.5),
            Padding = new Thickness(13, 11, 13, 11),
            Child = content,
        };
        card.SetResourceReference(Border.BackgroundProperty, "CardSurface");
        card.SetResourceReference(Border.BorderBrushProperty, "LineColor");
        Grid.SetColumn(card, column);
        return card;
    }

    private static Button BuildChoiceButton(string label, string detail, bool recommended)
    {
        var content = new StackPanel { Margin = new Thickness(2) };
        var title = new TextBlock
        {
            Text = recommended ? label + "  (Recommended)" : label,
            FontWeight = FontWeights.SemiBold,
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "ImportantText");
        content.Children.Add(title);
        var explanation = new TextBlock
        {
            Text = detail,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0),
        };
        explanation.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
        content.Children.Add(explanation);

        var button = new Button
        {
            Content = content,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 0, 0, 9),
            IsDefault = recommended,
        };
        button.SetResourceReference(Control.StyleProperty, "ThemedButtonStyle");
        return button;
    }

    private void Complete(PlanResumeBranchAction choice)
    {
        Choice = choice;
        DialogResult = choice != PlanResumeBranchAction.Cancel;
    }

    private static string ShortCommit(string commit) =>
        commit[..Math.Min(8, commit.Length)];

    private static string RelationshipText(PlanResumeBranchSituation situation) =>
        situation.Relationship switch
        {
            PlanResumeBranchRelationship.CurrentAhead =>
                $"Your current branch was built from the plan branch and has {situation.CurrentOnlyCommitCount} newer commit(s). You can safely bring all of them into the plan.",
            PlanResumeBranchRelationship.PlanAhead =>
                $"The plan branch is {situation.PlanOnlyCommitCount} commit(s) ahead and already contains the current branch's history.",
            PlanResumeBranchRelationship.SameCommit =>
                "The branches have different names but currently point to the same commit.",
            _ =>
                $"The branches diverged: {situation.CurrentOnlyCommitCount} commit(s) exist only on the current branch and {situation.PlanOnlyCommitCount} only on the plan branch.",
        };
}
