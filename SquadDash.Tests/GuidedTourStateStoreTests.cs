using System.IO;
using SquadDash.GuidedTours;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class GuidedTourStateStoreTests
{
    // ── Reset() ──────────────────────────────────────────────────────────────

    [Test]
    public void Reset_WhenStateIsPopulated_ClearsAllFields()
    {
        using var workspace = new TestWorkspace();
        var store = new GuidedTourStateStore(workspace.RootPath);

        store.Offered         = true;
        store.SkippedFirstRun = true;
        store.MarkCompleted("tour-a");
        store.MarkCompleted("tour-b");
        store.RecordTourNavAdvance();
        store.RecordTourNavAdvance();

        store.Reset();

        Assert.Multiple(() =>
        {
            Assert.That(store.Offered,                Is.False);
            Assert.That(store.SkippedFirstRun,        Is.False);
            Assert.That(store.IsCompleted("tour-a"),  Is.False);
            Assert.That(store.IsCompleted("tour-b"),  Is.False);
            Assert.That(store.TourNavAdvanceCount,    Is.EqualTo(0));
        });
    }

    [Test]
    public void Reset_PersistsToDisk_ReloadedStoreIsAlsoClean()
    {
        using var workspace = new TestWorkspace();
        var store = new GuidedTourStateStore(workspace.RootPath);

        store.Offered = true;
        store.MarkCompleted("tour-x");
        store.RecordTourNavAdvance();
        store.Reset();

        var reloaded = new GuidedTourStateStore(workspace.RootPath);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Offered,               Is.False);
            Assert.That(reloaded.IsCompleted("tour-x"), Is.False);
            Assert.That(reloaded.TourNavAdvanceCount,   Is.EqualTo(0));
        });
    }

    // ── OfferGuidedTourOnFirstRun guard: Offered flag ────────────────────────

    [Test]
    public void Offered_DefaultsToFalse_AllowingFirstRunOffer()
    {
        using var workspace = new TestWorkspace();
        var store = new GuidedTourStateStore(workspace.RootPath);

        Assert.That(store.Offered, Is.False);
    }

    [Test]
    public void Offered_SetToTrue_PersistsAcrossStoreInstances_BlockingSubsequentRun()
    {
        // Simulates the OfferGuidedTourOnFirstRun guard: once Offered is true on disk a
        // second store instance (e.g. after an app restart) also sees Offered = true.
        using var workspace = new TestWorkspace();
        var firstRun = new GuidedTourStateStore(workspace.RootPath);

        Assert.That(firstRun.Offered, Is.False, "should be offerable on the first run");
        firstRun.Offered = true;

        var subsequentRun = new GuidedTourStateStore(workspace.RootPath);
        Assert.That(subsequentRun.Offered, Is.True, "second run must see Offered = true and be suppressed");
    }

    [Test]
    public void Offered_SetToTrue_ThenReset_AllowsOfferAgain()
    {
        using var workspace = new TestWorkspace();
        var store = new GuidedTourStateStore(workspace.RootPath);

        store.Offered = true;
        store.Reset();

        Assert.That(store.Offered, Is.False, "after reset the tour should be offerable again");
    }

    // ── Team-debounce timer re-check idempotency ─────────────────────────────
    //
    // TeamRefreshDebounceTimer_Tick calls OfferGuidedTourOnFirstRun at the end of
    // every tick.  OfferGuidedTourOnFirstRun early-returns when Offered is already
    // true, so the store must not flush to disk on a no-op assignment and must hold
    // its value steady across multiple timer firings.

    [Test]
    public void Offered_SetToSameValue_DoesNotFlushToDisk()
    {
        using var workspace = new TestWorkspace();
        var store           = new GuidedTourStateStore(workspace.RootPath);
        var stateFilePath   = Path.Combine(workspace.RootPath, "guided-tour-state.json");

        store.Offered = true;
        var fileTimeBefore = File.GetLastWriteTimeUtc(stateFilePath);

        // Small sleep so a flush would produce a different timestamp.
        System.Threading.Thread.Sleep(20);
        store.Offered = true; // setting the same value — must be a no-op

        var fileTimeAfter = File.GetLastWriteTimeUtc(stateFilePath);

        Assert.That(fileTimeAfter, Is.EqualTo(fileTimeBefore),
            "no flush should occur when Offered is set to the same value");
    }

    [Test]
    public void Offered_WhenAlreadyTrue_MultipleDebounceRetriggers_FlagRemainsTrue()
    {
        // Simulates three debounce-timer ticks arriving after the first-run offer was
        // already shown.  The guard in OfferGuidedTourOnFirstRun (if Offered return)
        // means Offered must stay true throughout.
        using var workspace = new TestWorkspace();
        var store = new GuidedTourStateStore(workspace.RootPath);

        store.Offered = true;

        for (int i = 0; i < 3; i++)
        {
            if (store.Offered) continue; // OfferGuidedTourOnFirstRun early-return path
            store.Offered = true;        // only reached on a true first run
        }

        Assert.That(store.Offered, Is.True);
    }

    // ── MarkCompleted / MarkUncompleted / IsCompleted ────────────────────────

    [Test]
    public void MarkCompleted_ThenIsCompleted_ReturnsTrue()
    {
        using var workspace = new TestWorkspace();
        var store = new GuidedTourStateStore(workspace.RootPath);

        store.MarkCompleted("intro-tour");

        Assert.That(store.IsCompleted("intro-tour"), Is.True);
    }

    [Test]
    public void IsCompleted_UnknownTourId_ReturnsFalse()
    {
        using var workspace = new TestWorkspace();
        var store = new GuidedTourStateStore(workspace.RootPath);

        Assert.That(store.IsCompleted("nonexistent-tour"), Is.False);
    }

    [Test]
    public void MarkUncompleted_AfterMarkCompleted_ReturnsFalse()
    {
        using var workspace = new TestWorkspace();
        var store = new GuidedTourStateStore(workspace.RootPath);

        store.MarkCompleted("reversible-tour");
        store.MarkUncompleted("reversible-tour");

        Assert.That(store.IsCompleted("reversible-tour"), Is.False);
    }

    [Test]
    public void MarkCompleted_MultipleTours_AllPersistAcrossReload()
    {
        using var workspace = new TestWorkspace();
        var store = new GuidedTourStateStore(workspace.RootPath);

        store.MarkCompleted("tour-alpha");
        store.MarkCompleted("tour-beta");
        store.MarkCompleted("tour-gamma");

        var reloaded = new GuidedTourStateStore(workspace.RootPath);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.IsCompleted("tour-alpha"), Is.True);
            Assert.That(reloaded.IsCompleted("tour-beta"),  Is.True);
            Assert.That(reloaded.IsCompleted("tour-gamma"), Is.True);
        });
    }

    // ── RecordTourNavAdvance ─────────────────────────────────────────────────

    [Test]
    public void TourNavAdvanceCount_DefaultsToZero()
    {
        using var workspace = new TestWorkspace();
        var store = new GuidedTourStateStore(workspace.RootPath);

        Assert.That(store.TourNavAdvanceCount, Is.EqualTo(0));
    }

    [Test]
    public void RecordTourNavAdvance_IncrementsCountOnEachCall()
    {
        using var workspace = new TestWorkspace();
        var store = new GuidedTourStateStore(workspace.RootPath);

        store.RecordTourNavAdvance();
        store.RecordTourNavAdvance();
        store.RecordTourNavAdvance();

        Assert.That(store.TourNavAdvanceCount, Is.EqualTo(3));
    }

    [Test]
    public void RecordTourNavAdvance_PersistsToDisk()
    {
        using var workspace = new TestWorkspace();
        var store = new GuidedTourStateStore(workspace.RootPath);

        store.RecordTourNavAdvance();
        store.RecordTourNavAdvance();

        var reloaded = new GuidedTourStateStore(workspace.RootPath);
        Assert.That(reloaded.TourNavAdvanceCount, Is.EqualTo(2));
    }

    // ── Missing / corrupt file tolerance ─────────────────────────────────────

    [Test]
    public void Constructor_MissingFile_StartsWithDefaultState()
    {
        using var workspace = new TestWorkspace();
        var store = new GuidedTourStateStore(workspace.RootPath);

        Assert.Multiple(() =>
        {
            Assert.That(store.Offered,             Is.False);
            Assert.That(store.SkippedFirstRun,     Is.False);
            Assert.That(store.TourNavAdvanceCount, Is.EqualTo(0));
        });
    }

    [Test]
    public void Constructor_CorruptFile_FallsBackToDefaultState()
    {
        using var workspace = new TestWorkspace();
        File.WriteAllText(
            Path.Combine(workspace.RootPath, "guided-tour-state.json"),
            "{ this is not valid json !! }");

        var store = new GuidedTourStateStore(workspace.RootPath);

        Assert.Multiple(() =>
        {
            Assert.That(store.Offered,             Is.False);
            Assert.That(store.SkippedFirstRun,     Is.False);
            Assert.That(store.TourNavAdvanceCount, Is.EqualTo(0));
        });
    }
}
