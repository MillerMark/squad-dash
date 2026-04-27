using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Windows.Threading;
using SquadDash.Screenshots;
using SquadDash.Screenshots.Fixtures;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class ScrollPositionFixtureLoaderTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private double _transcriptOffset;
    private double _activeRosterOffset;
    private double _inactiveRosterOffset;

    [SetUp]
    public void SetUp()
    {
        _transcriptOffset    = 0;
        _activeRosterOffset  = 0;
        _inactiveRosterOffset = 0;
    }

    private ScrollPositionFixtureLoader MakeLoader() =>
        new ScrollPositionFixtureLoader(
            getTranscriptOffset:     () => _transcriptOffset,
            setTranscriptOffset:     v  => _transcriptOffset    = v,
            getActiveRosterOffset:   () => _activeRosterOffset,
            setActiveRosterOffset:   v  => _activeRosterOffset   = v,
            getInactiveRosterOffset: () => _inactiveRosterOffset,
            setInactiveRosterOffset: v  => _inactiveRosterOffset = v,
            dispatcher:              Dispatcher.CurrentDispatcher);

    private static ScreenshotFixture MakeFixture(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var data = new Dictionary<string, JsonElement>();
        foreach (var prop in doc.RootElement.EnumerateObject())
            data[prop.Name] = prop.Value.Clone();
        return new ScreenshotFixture("test-fixture", data);
    }

    // ── KnownKeys ─────────────────────────────────────────────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void KnownKeys_ContainsAllThreeScrollKeys()
    {
        var loader = MakeLoader();

        Assert.That(loader.KnownKeys, Is.EquivalentTo(new[]
        {
            "transcriptScrollOffset",
            "activeRosterScrollOffset",
            "inactiveRosterScrollOffset"
        }));
    }

    // ── ApplyAsync — key absent ───────────────────────────────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void ApplyAsync_WithNoScrollKeys_LeavesAllOffsetsUnchanged()
    {
        _transcriptOffset    = 42;
        _activeRosterOffset  = 10;
        _inactiveRosterOffset = 5;

        var loader  = MakeLoader();
        var fixture = MakeFixture("""{"other":"value"}""");

        loader.ApplyAsync(fixture, CancellationToken.None).GetAwaiter().GetResult();

        Assert.Multiple(() =>
        {
            Assert.That(_transcriptOffset,    Is.EqualTo(42));
            Assert.That(_activeRosterOffset,  Is.EqualTo(10));
            Assert.That(_inactiveRosterOffset, Is.EqualTo(5));
        });
    }

    // ── ApplyAsync — invalid values ───────────────────────────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void ApplyAsync_WithNegativeTranscriptOffset_LeavesOffsetUnchanged()
    {
        _transcriptOffset = 99;
        var loader  = MakeLoader();
        var fixture = MakeFixture("""{"transcriptScrollOffset":-1}""");

        loader.ApplyAsync(fixture, CancellationToken.None).GetAwaiter().GetResult();

        Assert.That(_transcriptOffset, Is.EqualTo(99));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ApplyAsync_WithNegativeActiveRosterOffset_LeavesOffsetUnchanged()
    {
        _activeRosterOffset = 20;
        var loader  = MakeLoader();
        var fixture = MakeFixture("""{"activeRosterScrollOffset":-1}""");

        loader.ApplyAsync(fixture, CancellationToken.None).GetAwaiter().GetResult();

        Assert.That(_activeRosterOffset, Is.EqualTo(20));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ApplyAsync_WithNegativeInactiveRosterOffset_LeavesOffsetUnchanged()
    {
        _inactiveRosterOffset = 15;
        var loader  = MakeLoader();
        var fixture = MakeFixture("""{"inactiveRosterScrollOffset":-5}""");

        loader.ApplyAsync(fixture, CancellationToken.None).GetAwaiter().GetResult();

        Assert.That(_inactiveRosterOffset, Is.EqualTo(15));
    }

    // ── ApplyAsync — valid values ─────────────────────────────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void ApplyAsync_WithValidTranscriptOffset_SetsTranscriptOffset()
    {
        var loader  = MakeLoader();
        var fixture = MakeFixture("""{"transcriptScrollOffset":150}""");

        loader.ApplyAsync(fixture, CancellationToken.None).GetAwaiter().GetResult();

        Assert.That(_transcriptOffset, Is.EqualTo(150));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ApplyAsync_WithValidActiveRosterOffset_SetsActiveRosterOffset()
    {
        var loader  = MakeLoader();
        var fixture = MakeFixture("""{"activeRosterScrollOffset":75}""");

        loader.ApplyAsync(fixture, CancellationToken.None).GetAwaiter().GetResult();

        Assert.That(_activeRosterOffset, Is.EqualTo(75));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ApplyAsync_WithValidInactiveRosterOffset_SetsInactiveRosterOffset()
    {
        var loader  = MakeLoader();
        var fixture = MakeFixture("""{"inactiveRosterScrollOffset":30}""");

        loader.ApplyAsync(fixture, CancellationToken.None).GetAwaiter().GetResult();

        Assert.That(_inactiveRosterOffset, Is.EqualTo(30));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ApplyAsync_WithAllThreeOffsets_SetsAllThree()
    {
        var loader  = MakeLoader();
        var fixture = MakeFixture(
            """{"transcriptScrollOffset":100,"activeRosterScrollOffset":50,"inactiveRosterScrollOffset":25}""");

        loader.ApplyAsync(fixture, CancellationToken.None).GetAwaiter().GetResult();

        Assert.Multiple(() =>
        {
            Assert.That(_transcriptOffset,    Is.EqualTo(100));
            Assert.That(_activeRosterOffset,  Is.EqualTo(50));
            Assert.That(_inactiveRosterOffset, Is.EqualTo(25));
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ApplyAsync_WithZeroOffset_SetsOffset()
    {
        _transcriptOffset = 200;
        var loader  = MakeLoader();
        var fixture = MakeFixture("""{"transcriptScrollOffset":0}""");

        loader.ApplyAsync(fixture, CancellationToken.None).GetAwaiter().GetResult();

        Assert.That(_transcriptOffset, Is.EqualTo(0));
    }

    // ── RestoreAsync ──────────────────────────────────────────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void RestoreAsync_WithoutPriorApply_IsIdempotentAndDoesNotThrow()
    {
        var loader = MakeLoader();

        Assert.DoesNotThrow(() =>
            loader.RestoreAsync(CancellationToken.None).GetAwaiter().GetResult());
    }

    [Test, Apartment(ApartmentState.STA)]
    public void RestoreAsync_AfterApply_RestoresOriginalTranscriptOffset()
    {
        _transcriptOffset = 200;
        var loader  = MakeLoader();
        var fixture = MakeFixture("""{"transcriptScrollOffset":500}""");

        loader.ApplyAsync(fixture, CancellationToken.None).GetAwaiter().GetResult();
        Assert.That(_transcriptOffset, Is.EqualTo(500), "precondition: fixture offset applied");

        loader.RestoreAsync(CancellationToken.None).GetAwaiter().GetResult();

        Assert.That(_transcriptOffset, Is.EqualTo(200));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void RestoreAsync_AfterApply_RestoresOriginalRosterOffsets()
    {
        _activeRosterOffset  = 60;
        _inactiveRosterOffset = 40;
        var loader  = MakeLoader();
        var fixture = MakeFixture(
            """{"activeRosterScrollOffset":120,"inactiveRosterScrollOffset":80}""");

        loader.ApplyAsync(fixture, CancellationToken.None).GetAwaiter().GetResult();
        loader.RestoreAsync(CancellationToken.None).GetAwaiter().GetResult();

        Assert.Multiple(() =>
        {
            Assert.That(_activeRosterOffset,  Is.EqualTo(60));
            Assert.That(_inactiveRosterOffset, Is.EqualTo(40));
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void RestoreAsync_CalledTwice_IsIdempotent()
    {
        _transcriptOffset = 100;
        var loader  = MakeLoader();
        var fixture = MakeFixture("""{"transcriptScrollOffset":300}""");

        loader.ApplyAsync(fixture, CancellationToken.None).GetAwaiter().GetResult();
        loader.RestoreAsync(CancellationToken.None).GetAwaiter().GetResult();
        Assert.That(_transcriptOffset, Is.EqualTo(100), "precondition: restored after first call");

        // Second restore — no-op; offset should remain at original
        loader.RestoreAsync(CancellationToken.None).GetAwaiter().GetResult();

        Assert.That(_transcriptOffset, Is.EqualTo(100));
    }

    // ── Reuse ─────────────────────────────────────────────────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void ApplyThenRestoreThenApply_LoaderIsReusable()
    {
        _transcriptOffset = 0;
        var loader  = MakeLoader();
        var fixture = MakeFixture("""{"transcriptScrollOffset":250}""");

        // First apply/restore cycle
        loader.ApplyAsync(fixture, CancellationToken.None).GetAwaiter().GetResult();
        loader.RestoreAsync(CancellationToken.None).GetAwaiter().GetResult();
        Assert.That(_transcriptOffset, Is.EqualTo(0), "precondition: restored after first cycle");

        // Second apply — loader must be reusable
        loader.ApplyAsync(fixture, CancellationToken.None).GetAwaiter().GetResult();

        Assert.That(_transcriptOffset, Is.EqualTo(250));
    }
}
