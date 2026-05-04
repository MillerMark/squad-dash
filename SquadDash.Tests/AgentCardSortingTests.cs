using System.Linq;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class AgentCardSortingTests {

    // -------------------------------------------------------------------------
    // Group assignment
    // -------------------------------------------------------------------------

    [Test]
    public void LeadAgent_AssignedToGroup0() {
        var (group, _, _) = AgentCardSorting.ComputeSortKey(true, false, [], "Squad");
        Assert.That(group, Is.EqualTo(0));
    }

    [Test]
    public void RosterAgent_AssignedToGroup1() {
        var (group, _, _) = AgentCardSorting.ComputeSortKey(false, false, [], "Arjun");
        Assert.That(group, Is.EqualTo(1));
    }

    [Test]
    public void DynamicAgent_AssignedToGroup3() {
        var (group, _, _) = AgentCardSorting.ComputeSortKey(false, true, [], "Temp");
        Assert.That(group, Is.EqualTo(3));
    }

    // -------------------------------------------------------------------------
    // Group ordering: lead < roster < dynamic regardless of activation time
    // -------------------------------------------------------------------------

    [Test]
    public void LeadAgent_SortsBeforeRosterAgent_RegardlessOfActivationTime() {
        var lead = AgentCardSorting.ComputeSortKey(true, false, [9_000_000L], "Squad");
        var roster = AgentCardSorting.ComputeSortKey(false, false, [1L], "Arjun");
        Assert.That(lead, Is.LessThan(roster));
    }

    [Test]
    public void RosterAgent_SortsBeforeDynamicAgent_RegardlessOfActivationTime() {
        var roster = AgentCardSorting.ComputeSortKey(false, false, [1L], "Arjun");
        var dynamic = AgentCardSorting.ComputeSortKey(false, true, [9_000_000L], "Temp");
        Assert.That(roster, Is.LessThan(dynamic));
    }

    // -------------------------------------------------------------------------
    // Never-activated agents → SortTicks = long.MaxValue (rightmost in group)
    // -------------------------------------------------------------------------

    [Test]
    public void RosterAgent_WithNoThreads_HasMaxSortTicks() {
        var (_, sortTicks, _) = AgentCardSorting.ComputeSortKey(false, false, [], "Arjun");
        Assert.That(sortTicks, Is.EqualTo(long.MaxValue));
    }

    [Test]
    public void DynamicAgent_WithNoThreads_HasMaxSortTicks() {
        var (_, sortTicks, _) = AgentCardSorting.ComputeSortKey(false, true, [], "Temp");
        Assert.That(sortTicks, Is.EqualTo(long.MaxValue));
    }

    [Test]
    public void RosterAgent_WithNoThreads_SortsRightOf_ActivatedRosterAgent() {
        var activated = AgentCardSorting.ComputeSortKey(false, false, [1_000L], "Arjun");
        var idle = AgentCardSorting.ComputeSortKey(false, false, [], "Lyra");
        Assert.That(activated, Is.LessThan(idle));
    }

    [Test]
    public void DynamicAgent_WithNoThreads_SortsRightOf_ActivatedDynamicAgent() {
        var activated = AgentCardSorting.ComputeSortKey(false, true, [1_000L], "Temp1");
        var idle = AgentCardSorting.ComputeSortKey(false, true, [], "Temp2");
        Assert.That(activated, Is.LessThan(idle));
    }

    // -------------------------------------------------------------------------
    // Most-recently-activated sorts LEFT (smaller SortTicks) within a group
    // -------------------------------------------------------------------------

    [Test]
    public void RosterAgent_MoreRecentActivation_SortsLeftOf_OlderActivation() {
        var recent = AgentCardSorting.ComputeSortKey(false, false, [2_000L], "Newer");
        var older = AgentCardSorting.ComputeSortKey(false, false, [1_000L], "Older");
        Assert.That(recent, Is.LessThan(older));
    }

    [Test]
    public void DynamicAgent_MoreRecentActivation_SortsLeftOf_OlderActivation() {
        var recent = AgentCardSorting.ComputeSortKey(false, true, [2_000L], "Newer");
        var older = AgentCardSorting.ComputeSortKey(false, true, [1_000L], "Older");
        Assert.That(recent, Is.LessThan(older));
    }

    // -------------------------------------------------------------------------
    // Multiple threads: sort key is based on the MOST RECENT thread (max ticks)
    // -------------------------------------------------------------------------

    [Test]
    public void RosterAgent_MultipleThreads_SortKeyMatchesSingleThread_AtMaxTicks() {
        var multi = AgentCardSorting.ComputeSortKey(false, false, [500L, 1_000L, 750L], "Multi");
        var single = AgentCardSorting.ComputeSortKey(false, false, [1_000L], "Single");
        // Both should produce the same SortTicks because max is 1 000 in both cases.
        Assert.That(multi.SortTicks, Is.EqualTo(single.SortTicks));
    }

    [Test]
    public void RosterAgent_MultipleThreads_SortsLeftOf_AgentWhoseMaxTicksIsOlder() {
        var manyThreadsHighMax = AgentCardSorting.ComputeSortKey(false, false, [100L, 200L, 3_000L], "High");
        var singleThreadLowMax = AgentCardSorting.ComputeSortKey(false, false, [2_000L], "Low");
        Assert.That(manyThreadsHighMax, Is.LessThan(singleThreadLowMax));
    }

    // -------------------------------------------------------------------------
    // Sort-formula regression: must use (long.MaxValue - ticks), NOT negation
    //
    // The old implementation negated the tick value.  Negation is wrong because:
    //   • It places activated agents at large negative sort keys and never-activated
    //     agents at long.MaxValue, creating a large discontinuity in the key space
    //     that can interact unexpectedly with tie-breaking on the Name component.
    //   • More critically, for tick values > long.MaxValue / 2 the unchecked negation
    //     overflows into positive territory, flipping the relative order of two
    //     recently-activated agents (the more recent one would sort FURTHER RIGHT).
    //
    // The correct formula keeps all keys in the non-negative range [0, long.MaxValue]
    // and preserves a consistent ordering for any valid DateTimeOffset.UtcTicks value.
    // -------------------------------------------------------------------------

    [Test]
    public void SortTicks_Formula_Is_LongMaxValueMinusTicks() {
        const long ticks = 638_500_000_000_000_000L; // realistic DateTimeOffset.UtcTicks (~2024)
        var (_, sortTicks, _) = AgentCardSorting.ComputeSortKey(false, false, [ticks], "Arjun");
        Assert.That(sortTicks, Is.EqualTo(long.MaxValue - ticks));
    }

    [Test]
    public void SortTicks_IsNonNegative_ForAnyReasonableTickValue() {
        long[] samples = [
            1L,
            1_000_000L,
            638_500_000_000_000_000L,       // ~2024
            long.MaxValue / 2 + 1L,         // value that negation would overflow
            long.MaxValue - 1L,
        ];
        Assert.Multiple(() => {
            foreach (var ticks in samples) {
                var (_, sortTicks, _) = AgentCardSorting.ComputeSortKey(false, false, [ticks], "A");
                Assert.That(sortTicks, Is.GreaterThanOrEqualTo(0L),
                    $"SortTicks should be non-negative for ticks={ticks}");
            }
        });
    }

    [Test]
    public void SortTicks_CorrectFormula_PreservesOrdering_AcrossFullTickRange() {
        // Validates that the formula (long.MaxValue - ticks) correctly orders agents
        // across a wide range of tick values, including values close to long.MaxValue.
        // A more-recently-activated agent always has a SMALLER sort key than an
        // older one — ensuring it renders leftmost in the roster panel.
        long[] tickSamples = [
            1L,
            1_000L,
            638_500_000_000_000_000L,   // ~2024 date
            long.MaxValue / 2,
            long.MaxValue - 2L,
            long.MaxValue - 1L,
        ];

        // For every consecutive pair (older, newer), verify newer has a smaller sort key.
        Assert.Multiple(() => {
            for (var i = 0; i < tickSamples.Length - 1; i++) {
                var olderTicks = tickSamples[i];
                var newerTicks = tickSamples[i + 1];
                var (_, olderKey, _) = AgentCardSorting.ComputeSortKey(false, false, [olderTicks], "Older");
                var (_, newerKey, _) = AgentCardSorting.ComputeSortKey(false, false, [newerTicks], "Newer");
                Assert.That(newerKey, Is.LessThan(olderKey),
                    $"A more-recently-activated agent (ticks={newerTicks}) should have a smaller " +
                    $"sort key than an older one (ticks={olderTicks}).");
            }
        });
    }

    // -------------------------------------------------------------------------
    // Regression: Bug 1 — sort key must use GetThreadLastActivityAt (which
    // returns LastObservedActivityAt when set) rather than thread.StartedAt.
    //
    // Before the fix, the caller passed t.StartedAt.UtcTicks.  That meant an
    // agent whose thread was *created* a long time ago but has had very recent
    // *activity* (LastObservedActivityAt >> StartedAt) was pushed to the right
    // even though it should be the leftmost card.
    //
    // After the fix, GetAgentCardBucketSortKey passes
    //   GetThreadLastActivityAt(t).UtcTicks
    // which returns LastObservedActivityAt when it is set, and falls back to
    // StartedAt (and then CompletedAt) otherwise.
    //
    // These tests validate the contract at the ComputeSortKey boundary: ticks
    // that represent last-activity time (not thread-creation time) must be used
    // as the sort input.
    // -------------------------------------------------------------------------

    [Test]
    public void Bug1_Regression_AgentWithRecentLastObservedActivityAt_SortsLeftOf_AgentWithMoreRecentStartedAt() {
        // AgentA: thread was created a long time ago (tStartedAtOld) but has
        // recent observed activity (tLastActivityRecent).
        // → fixed caller passes GetThreadLastActivityAt = tLastActivityRecent.
        //
        // AgentB: thread was created more recently (tStartedAtMedium) with no
        // LastObservedActivityAt set.
        // → caller passes GetThreadLastActivityAt = tStartedAtMedium (fallback).
        //
        // tStartedAtOld < tStartedAtMedium < tLastActivityRecent
        // After fix: AgentA tick = tLastActivityRecent  → sorts LEFT  ✓
        // Before fix: AgentA tick = tStartedAtOld        → sorts RIGHT ✗

        const long tStartedAtOld       = 1_000L; // AgentA.StartedAt  (old; what the buggy caller passed — not used by the fix)
        const long tStartedAtMedium    = 2_000L; // AgentB.StartedAt  (= its GetThreadLastActivityAt)
        const long tLastActivityRecent = 3_000L; // AgentA.LastObservedActivityAt (what the fixed caller now passes)
        _ = tStartedAtOld; // intentionally not passed: the fixed caller ignores StartedAt when LastObservedActivityAt is set

        // Fixed caller: passes last-activity ticks, not StartedAt ticks.
        var agentA = AgentCardSorting.ComputeSortKey(false, false, [tLastActivityRecent], "AgentA");
        var agentB = AgentCardSorting.ComputeSortKey(false, false, [tStartedAtMedium],   "AgentB");

        Assert.That(agentA, Is.LessThan(agentB),
            "AgentA has recent LastObservedActivityAt and should sort LEFT of AgentB " +
            "whose StartedAt is more recent than AgentA's StartedAt but older than its activity.");
    }

    [Test]
    public void Bug1_Regression_ConfirmsBuggedBehavior_UsingStartedAt_GivesWrongOrder() {
        // If the caller had continued to pass t.StartedAt.UtcTicks (the bug), the
        // sort would use the creation time, not the activity time.  AgentA's old
        // StartedAt would push it RIGHT even though its activity is the most recent.
        // This test documents the incorrect pre-fix order so the regression is clear.

        const long tStartedAtOld     = 1_000L; // AgentA.StartedAt  (what the buggy caller used)
        const long tStartedAtMedium  = 2_000L; // AgentB.StartedAt  (same with or without fix)

        // Buggy caller: passes StartedAt ticks for both agents.
        var agentA_buggy = AgentCardSorting.ComputeSortKey(false, false, [tStartedAtOld],    "AgentA");
        var agentB       = AgentCardSorting.ComputeSortKey(false, false, [tStartedAtMedium], "AgentB");

        // Before the fix, AgentA (with the older StartedAt) incorrectly sorted RIGHT.
        Assert.That(agentB, Is.LessThan(agentA_buggy),
            "EXPECTED BUGGY BEHAVIOR: when the wrong field (StartedAt) is used, AgentA's " +
            "old creation time makes it sort RIGHT of AgentB despite its more-recent activity.");
    }

    // -------------------------------------------------------------------------
    // Regression: Bug 2 — placeholder threads must be excluded from the sort-key
    // tick list before calling ComputeSortKey.
    //
    // When the user clicks a roster agent card, MainWindow creates a placeholder
    // thread with IsPlaceholderThread = true and StartedAt = DateTimeOffset.Now.
    // Before the fix, this placeholder was included in the tick list, so its
    // freshly-minted "now" timestamp became the sort key — making the clicked
    // agent jump to the leftmost slot regardless of actual work activity.
    //
    // After the fix, GetAgentCardBucketSortKey filters with
    //   .Where(t => !t.IsPlaceholderThread)
    // before selecting ticks, so placeholder timestamps are invisible to the sorter.
    // -------------------------------------------------------------------------

    [Test]
    public void Bug2_Regression_PlaceholderThreadExcluded_AgentSortsRightOf_AgentWithMoreRecentRealActivity() {
        // AgentA: one real thread (old activity) + one placeholder thread (StartedAt = now).
        // After fix: placeholder is filtered by the caller; only [tRealOld] is passed.
        //
        // AgentB: one real thread with medium activity.
        //
        // tRealOld < tRealMedium << tPlaceholderNow
        // After fix:  AgentA tick = tRealOld    → sorts RIGHT of AgentB  ✓
        // Before fix: AgentA tick = max(tRealOld, tPlaceholderNow)
        //                         = tPlaceholderNow → sorts LEFT of AgentB ✗

        const long tRealOld        = 1_000L;     // AgentA's real thread last-activity
        const long tRealMedium     = 5_000L;     // AgentB's real thread last-activity
        const long tPlaceholderNow = 9_999_999L; // AgentA's placeholder thread StartedAt (≈ now)

        // Fixed caller: placeholder filtered out; only real-thread tick for AgentA.
        _ = tPlaceholderNow; // documented above — not passed after the fix
        var agentA = AgentCardSorting.ComputeSortKey(false, false, [tRealOld],    "AgentA");
        var agentB = AgentCardSorting.ComputeSortKey(false, false, [tRealMedium], "AgentB");

        Assert.That(agentB, Is.LessThan(agentA),
            "AgentA has an older real-thread activity than AgentB. Its placeholder thread " +
            "(StartedAt=now) is excluded, so it correctly sorts RIGHT of AgentB.");
    }

    [Test]
    public void Bug2_Regression_ConfirmsBuggedBehavior_IncludingPlaceholder_FalselyAdvancesAgentLeft() {
        // If the placeholder thread is not filtered (the bug), its freshly-minted
        // StartedAt becomes the max tick for AgentA, pulling it to the left even
        // though its real thread activity is the oldest on the board.
        // This test documents the incorrect pre-fix order so the regression is clear.

        const long tRealOld        = 1_000L;     // AgentA's real thread last-activity
        const long tRealMedium     = 5_000L;     // AgentB's real thread last-activity
        const long tPlaceholderNow = 9_999_999L; // AgentA's placeholder thread StartedAt (≈ now)

        // Buggy caller: placeholder NOT filtered; both real and placeholder ticks passed for AgentA.
        var agentA_buggy = AgentCardSorting.ComputeSortKey(false, false, [tRealOld, tPlaceholderNow], "AgentA");
        var agentB       = AgentCardSorting.ComputeSortKey(false, false, [tRealMedium],               "AgentB");

        // Before the fix, AgentA's placeholder inflated its sort key and it incorrectly
        // sorted LEFT of AgentB despite having stale real-thread activity.
        Assert.That(agentA_buggy, Is.LessThan(agentB),
            "EXPECTED BUGGY BEHAVIOR: when the placeholder thread is included, its 'now' " +
            "StartedAt timestamp inflates AgentA's sort key and it incorrectly sorts LEFT " +
            "of AgentB even though AgentA's real activity is older.");
    }

    // -------------------------------------------------------------------------
    // Full end-to-end sort order
    // -------------------------------------------------------------------------

    [Test]
    public void RetiredAgent_AssignedToGroup4() {
        var (group, _, _) = AgentCardSorting.ComputeSortKey(false, false, [], "Alice", isRetired: true);
        Assert.That(group, Is.EqualTo(4));
    }

    [Test]
    public void RetiredAgent_SortsAfterDynamicAgent() {
        var dynamic  = AgentCardSorting.ComputeSortKey(false, true,  [], "Temp");
        var retired  = AgentCardSorting.ComputeSortKey(false, false, [], "Alice", isRetired: true);
        Assert.That(dynamic, Is.LessThan(retired));
    }

    [Test]
    public void RetiredAgents_SortedAlphabeticallyByName() {
        var bravo = AgentCardSorting.ComputeSortKey(false, false, [], "Bravo", isRetired: true);
        var alpha = AgentCardSorting.ComputeSortKey(false, false, [], "Alpha", isRetired: true);
        Assert.That(alpha, Is.LessThan(bravo));
    }

    [Test]
    public void RetiredAgent_AlphaSort_NotAffectedByThreadActivity() {
        // A retired agent with lots of recent activity should still sort by name only.
        var retiredBusy = AgentCardSorting.ComputeSortKey(false, false, [9_000_000L], "Zara", isRetired: true);
        var retiredIdle = AgentCardSorting.ComputeSortKey(false, false, [],           "Andy", isRetired: true);
        Assert.That(retiredIdle, Is.LessThan(retiredBusy), "Andy < Zara alphabetically despite Zara having threads");
    }

    // -------------------------------------------------------------------------
    // Full end-to-end sort order
    // -------------------------------------------------------------------------

    [Test]
    public void FullSort_ProducesExpectedOrder() {
        // Expected slot order (left → right):
        //   [0] lead              group 0, SortTicks = 0
        //   [1] rosterRecent      group 1, MaxValue - 2000 (smallest in group)
        //   [2] rosterOlder       group 1, MaxValue - 1000
        //   [3] rosterIdle        group 1, MaxValue (never activated)
        //   [4] dynamicRecent     group 3, MaxValue - 3000 (smallest in group)
        //   [5] dynamicIdle       group 3, MaxValue (never activated)
        //   [6] retiredA          group 4, "Andy"   (alphabetically first)
        //   [7] retiredB          group 4, "Zara"   (alphabetically last)
        var lead = AgentCardSorting.ComputeSortKey(true, false, [2_000L], "Squad");
        var rosterRecent = AgentCardSorting.ComputeSortKey(false, false, [2_000L], "Arjun");
        var rosterOlder = AgentCardSorting.ComputeSortKey(false, false, [1_000L], "Lyra");
        var rosterIdle = AgentCardSorting.ComputeSortKey(false, false, [], "Talia");
        var dynamicRecent = AgentCardSorting.ComputeSortKey(false, true, [3_000L], "Temp1");
        var dynamicIdle = AgentCardSorting.ComputeSortKey(false, true, [], "Temp2");
        var retiredA = AgentCardSorting.ComputeSortKey(false, false, [], "Andy", isRetired: true);
        var retiredB = AgentCardSorting.ComputeSortKey(false, false, [], "Zara", isRetired: true);

        var sorted = new[] { rosterIdle, dynamicRecent, lead, rosterOlder, dynamicIdle, rosterRecent, retiredB, retiredA }
            .Order()
            .ToList();

        Assert.Multiple(() => {
            Assert.That(sorted[0], Is.EqualTo(lead),          "slot 0: lead agent");
            Assert.That(sorted[1], Is.EqualTo(rosterRecent),  "slot 1: most-recently-activated roster agent");
            Assert.That(sorted[2], Is.EqualTo(rosterOlder),   "slot 2: older roster agent");
            Assert.That(sorted[3], Is.EqualTo(rosterIdle),    "slot 3: never-activated roster agent");
            Assert.That(sorted[4], Is.EqualTo(dynamicRecent), "slot 4: most-recently-activated dynamic agent");
            Assert.That(sorted[5], Is.EqualTo(dynamicIdle),   "slot 5: never-activated dynamic agent");
            Assert.That(sorted[6], Is.EqualTo(retiredA),      "slot 6: retired agent Andy (alpha first)");
            Assert.That(sorted[7], Is.EqualTo(retiredB),      "slot 7: retired agent Zara (alpha last)");
        });
    }
}
