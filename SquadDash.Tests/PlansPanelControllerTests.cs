using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SquadDash.Tests;

/// <summary>
/// Tests for <see cref="PlansPanelController"/> — panel visibility, plan insertion,
/// deduplication, watcher refresh, and the RevealCollectedPlan convenience method.
/// All tests run on a dedicated STA thread via <see cref="WpfTestContext"/> because
/// the controller creates WPF elements.
/// </summary>
[TestFixture]
internal sealed class PlansPanelControllerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Plan MakePlan(
        string planId   = "test-plan-1",
        string title    = "Test Plan",
        string status   = "approved",
        string branch   = "feature/test",
        string revision = "rev1") =>
        new(
            PlanId:          planId,
            Revision:        revision,
            Source:          PlanSource.DecomposeDecision,
            LifecycleStatus: status,
            Title:           title,
            Branch:          branch,
            Summary:         "A test plan.",
            Tasks:           [],
            ApprovalGates:   [],
            Progress:        new PlanProgress(0, 0),
            Timestamps:      new PlanTimestamps(CreatedAt: DateTimeOffset.UtcNow));

    private static (PlansPanelController controller,
                    StackPanel activePanel,
                    StackPanel completedPanel,
                    List<bool> visibilityCalls)
        BuildController()
    {
        var activePanel    = new StackPanel();
        var completedPanel = new StackPanel();
        var completedSection = new Border();
        var visibilityCalls  = new List<bool>();

        var controller = new PlansPanelController(
            activePanel:          activePanel,
            completedPanel:       completedPanel,
            completedSection:     completedSection,
            openPlan:             _ => { },
            syncBorderVisibility: v => visibilityCalls.Add(v),
            setMenuChecked:       _ => { },
            persistVisibility:    () => { });

        return (controller, activePanel, completedPanel, visibilityCalls);
    }

    // ── Hidden panel is revealed ──────────────────────────────────────────────

    [Test]
    public void RevealCollectedPlan_HiddenPanel_BecomesVisible() =>
        WpfTestContext.Run(() =>
        {
            var (ctrl, _, _, _) = BuildController();
            Assert.That(ctrl.PanelVisible, Is.False);

            ctrl.RevealCollectedPlan(MakePlan());

            Assert.That(ctrl.PanelVisible, Is.True);
        });

    // ── Visible panel stays visible ───────────────────────────────────────────

    [Test]
    public void RevealCollectedPlan_AlreadyVisible_StaysVisible() =>
        WpfTestContext.Run(() =>
        {
            var (ctrl, _, _, _) = BuildController();
            ctrl.Show();
            Assert.That(ctrl.PanelVisible, Is.True);

            ctrl.RevealCollectedPlan(MakePlan());

            Assert.That(ctrl.PanelVisible, Is.True);
        });

    // ── No row duplication ────────────────────────────────────────────────────

    [Test]
    public void RevealCollectedPlan_CalledTwiceSamePlan_NoDuplicateRows() =>
        WpfTestContext.Run(() =>
        {
            var (ctrl, activePanel, _, _) = BuildController();
            var plan = MakePlan();

            ctrl.RevealCollectedPlan(plan);
            ctrl.RevealCollectedPlan(plan);

            var borders = activePanel.Children.OfType<Border>()
                .Where(b => b.Tag is string s && s == plan.PlanId)
                .ToList();
            Assert.That(borders, Has.Count.EqualTo(1));
        });

    // ── Watcher refresh replaces stale rows ───────────────────────────────────

    [Test]
    public void OnPlanChanged_UpdatedPlan_ReplacesExistingEntry() =>
        WpfTestContext.Run(() =>
        {
            var (ctrl, activePanel, _, _) = BuildController();
            var plan = MakePlan(title: "Original");
            ctrl.RevealCollectedPlan(plan);

            var updated = plan with { Title = "Updated" };
            ctrl.OnPlanChanged(updated);

            var borders = activePanel.Children.OfType<Border>()
                .Where(b => b.Tag is string s && s == plan.PlanId)
                .ToList();
            Assert.That(borders, Has.Count.EqualTo(1));
        });

    // ── Different plans create separate rows ──────────────────────────────────

    [Test]
    public void RevealCollectedPlan_DifferentPlans_CreatesSeparateRows() =>
        WpfTestContext.Run(() =>
        {
            var (ctrl, activePanel, _, _) = BuildController();
            var planA = MakePlan(planId: "plan-a", title: "Plan A");
            var planB = MakePlan(planId: "plan-b", title: "Plan B");

            ctrl.RevealCollectedPlan(planA);
            ctrl.RevealCollectedPlan(planB);

            var tags = activePanel.Children.OfType<Border>()
                .Select(b => b.Tag as string)
                .Where(t => t is not null)
                .ToList();
            Assert.That(tags, Has.Count.EqualTo(2));
            Assert.That(tags, Does.Contain("plan-a"));
            Assert.That(tags, Does.Contain("plan-b"));
        });

    // ── SyncBorderVisibility called on reveal ─────────────────────────────────

    [Test]
    public void RevealCollectedPlan_TriggersSyncBorderVisibility() =>
        WpfTestContext.Run(() =>
        {
            var (ctrl, _, _, visibilityCalls) = BuildController();

            ctrl.RevealCollectedPlan(MakePlan());

            Assert.That(visibilityCalls, Does.Contain(true));
        });

    // ── Row Tag matches PlanId ────────────────────────────────────────────────

    [Test]
    public void BuildRow_SetsTagToPlanId() =>
        WpfTestContext.Run(() =>
        {
            var (ctrl, activePanel, _, _) = BuildController();
            var plan = MakePlan(planId: "my-unique-id");

            ctrl.Refresh([plan]);

            var row = activePanel.Children.OfType<Border>()
                .FirstOrDefault(b => b.Tag is string s && s == "my-unique-id");
            Assert.That(row, Is.Not.Null);
        });
}
