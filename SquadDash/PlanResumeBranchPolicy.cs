namespace SquadDash;

internal enum PlanResumeBranchRelationship
{
    SameCommit,
    CurrentAhead,
    PlanAhead,
    Diverged,
}

internal enum PlanResumeBranchAction
{
    Cancel,
    SwitchOnly,
    BringCurrentWork,
}

internal sealed record PlanResumeBranchSituation(
    string CurrentBranch,
    string PlanBranch,
    string CurrentHead,
    string PlanHead,
    PlanResumeBranchRelationship Relationship,
    int CurrentOnlyCommitCount,
    int PlanOnlyCommitCount,
    bool WorktreeIsClean)
{
    internal bool CanBringCurrentWork =>
        Relationship == PlanResumeBranchRelationship.CurrentAhead ||
        Relationship == PlanResumeBranchRelationship.SameCommit ||
        Relationship == PlanResumeBranchRelationship.PlanAhead ||
        Relationship == PlanResumeBranchRelationship.Diverged && WorktreeIsClean;
}

internal static class PlanResumeBranchPolicy
{
    internal static PlanResumeBranchRelationship Classify(
        bool planIsAncestorOfCurrent,
        bool currentIsAncestorOfPlan)
    {
        if (planIsAncestorOfCurrent && currentIsAncestorOfPlan)
            return PlanResumeBranchRelationship.SameCommit;
        if (planIsAncestorOfCurrent)
            return PlanResumeBranchRelationship.CurrentAhead;
        if (currentIsAncestorOfPlan)
            return PlanResumeBranchRelationship.PlanAhead;
        return PlanResumeBranchRelationship.Diverged;
    }

    internal static PlanResumeBranchAction RecommendedAction(PlanResumeBranchSituation situation) =>
        situation.Relationship switch
        {
            PlanResumeBranchRelationship.CurrentAhead => PlanResumeBranchAction.BringCurrentWork,
            PlanResumeBranchRelationship.PlanAhead => PlanResumeBranchAction.SwitchOnly,
            PlanResumeBranchRelationship.SameCommit => PlanResumeBranchAction.SwitchOnly,
            PlanResumeBranchRelationship.Diverged when situation.WorktreeIsClean =>
                PlanResumeBranchAction.BringCurrentWork,
            _ => PlanResumeBranchAction.SwitchOnly,
        };
}
