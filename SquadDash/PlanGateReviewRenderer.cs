using System.Windows;
using System.Windows.Documents;

namespace SquadDash;

internal static class PlanGateReviewRenderer
{
    internal static IReadOnlyList<Block> CreateReviewBlocks(
        PlanApprovalGate gate,
        IReadOnlyList<string> approvalQuestions,
        bool hasClarifiedReview)
    {
        var proofRequirements = gate.ProofRequirements ?? [];
        var blocks = new System.Collections.Generic.List<Block>();
        var reviewList = new List { MarkerStyle = TextMarkerStyle.Disc };
        reviewList.SetResourceReference(TextElement.ForegroundProperty, "BodyText");
        reviewList.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
        reviewList.Margin = new Thickness(0, 0, 0, 12);

        static string NormalizeReviewText(string text) =>
            string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        var descriptions = proofRequirements
            .Select(requirement => NormalizeReviewText(requirement.Description))
            .ToArray();
        var presentedQuestions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool IsNewQuestion(string question) =>
            !descriptions.Any(description => description.Contains(question, StringComparison.OrdinalIgnoreCase)) &&
            presentedQuestions.Add(question);

        foreach (var requirement in proofRequirements)
        {
            var proofPara = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
            var scenarioTitle = new Run(PlanHumanReviewChecklist.ResolveDisplayTitle(
                requirement.Title, requirement.RequirementId))
            {
                FontWeight = FontWeights.Bold,
            };
            scenarioTitle.SetResourceReference(TextElement.ForegroundProperty, "ImportantText");
            proofPara.Inlines.Add(scenarioTitle);
            if (!string.IsNullOrWhiteSpace(requirement.Description))
            {
                proofPara.Inlines.Add(new LineBreak());
                proofPara.Inlines.Add(new Run(requirement.Description));
            }

            // Clarifications replace the original questions; do not revive superseded wording.
            if (!hasClarifiedReview && !string.IsNullOrWhiteSpace(requirement.Question))
            {
                foreach (var question in PlanHumanReviewChecklist.SplitLegacyQuestions(requirement.Question))
                {
                    if (!IsNewQuestion(question))
                        continue;
                    proofPara.Inlines.Add(new LineBreak());
                    proofPara.Inlines.Add(new Run(question));
                }
            }
            reviewList.ListItems.Add(new ListItem(proofPara));
        }

        foreach (var question in approvalQuestions
            .Where(question => !string.IsNullOrWhiteSpace(question))
            .SelectMany(PlanHumanReviewChecklist.SplitLegacyQuestions))
        {
            if (!IsNewQuestion(question))
                continue;
            var questionPara = new Paragraph(new Run(question));
            questionPara.SetResourceReference(TextElement.ForegroundProperty, "ImportantText");
            questionPara.SetResourceReference(TextElement.FontSizeProperty, "FontSizeBody");
            // A gate-level summary is not another proof requirement.
            if (proofRequirements.Count > 0)
                blocks.Add(questionPara);
            else
                reviewList.ListItems.Add(new ListItem(questionPara));
        }

        if (reviewList.ListItems.Count > 0)
            blocks.Insert(0, reviewList);
        return blocks;
    }
}
