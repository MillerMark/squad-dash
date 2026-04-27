using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using SquadDash.Screenshots;
using SquadDash.Screenshots.Fixtures;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class QuickReplyFixtureLoaderTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Ensures <see cref="Application.Current"/> is non-null so that
    /// <see cref="Application.TryFindResource"/> calls inside the loader do not throw.
    /// Must be called on an STA thread.
    /// </summary>
    private static void EnsureApplication()
    {
        if (Application.Current is null)
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
    }

    private static QuickReplyFixtureLoader MakeLoader(TranscriptThreadState thread) =>
        new QuickReplyFixtureLoader(
            getCoordinatorThread: () => thread,
            dispatcher:           Dispatcher.CurrentDispatcher);

    private static TranscriptThreadState MakeThread() =>
        new TranscriptThreadState(
            threadId:  "coordinator",
            kind:      TranscriptThreadKind.Coordinator,
            title:     "Coordinator",
            startedAt: DateTimeOffset.UtcNow);

    private static ScreenshotFixture MakeFixture(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var data = new Dictionary<string, JsonElement>();
        foreach (var prop in doc.RootElement.EnumerateObject())
            data[prop.Name] = prop.Value.Clone();
        return new ScreenshotFixture("test-fixture", data);
    }

    // ── ApplyAsync ────────────────────────────────────────────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void ApplyAsync_WithValidOptionsArray_AddsOneBlockToDocument()
    {
        // Arrange
        EnsureApplication();
        var thread  = MakeThread();
        var loader  = MakeLoader(thread);
        var fixture = MakeFixture("""{"options":["Yes","No","Maybe"]}""");

        // Act
        loader.ApplyAsync(fixture, CancellationToken.None).GetAwaiter().GetResult();

        // Assert
        Assert.That(thread.Document.Blocks.Count, Is.EqualTo(1));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ApplyAsync_WithMissingOptionsKey_AddsNothingToDocument()
    {
        // Arrange
        EnsureApplication();
        var thread  = MakeThread();
        var loader  = MakeLoader(thread);
        var fixture = MakeFixture("""{"other":"value"}""");

        // Act
        loader.ApplyAsync(fixture, CancellationToken.None).GetAwaiter().GetResult();

        // Assert
        Assert.That(thread.Document.Blocks.Count, Is.EqualTo(0));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ApplyAsync_WithEmptyOptionsArray_AddsNothingToDocument()
    {
        // Arrange
        EnsureApplication();
        var thread  = MakeThread();
        var loader  = MakeLoader(thread);
        var fixture = MakeFixture("""{"options":[]}""");

        // Act
        loader.ApplyAsync(fixture, CancellationToken.None).GetAwaiter().GetResult();

        // Assert
        Assert.That(thread.Document.Blocks.Count, Is.EqualTo(0));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ApplyAsync_WithReasonKeyPresent_DoesNotThrow()
    {
        // Arrange
        EnsureApplication();
        var thread  = MakeThread();
        var loader  = MakeLoader(thread);
        var fixture = MakeFixture("""{"options":["Accept","Decline"],"reason":"Please confirm your choice"}""");

        // Act / Assert — reason caption added above buttons; no exception expected
        Assert.DoesNotThrow(() =>
            loader.ApplyAsync(fixture, CancellationToken.None).GetAwaiter().GetResult());
    }

    // ── RestoreAsync ──────────────────────────────────────────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void RestoreAsync_WithoutPriorApply_IsIdempotentAndDoesNotThrow()
    {
        // Arrange
        EnsureApplication();
        var thread = MakeThread();
        var loader = MakeLoader(thread);

        // Act / Assert
        Assert.DoesNotThrow(() =>
            loader.RestoreAsync(CancellationToken.None).GetAwaiter().GetResult());
    }

    [Test, Apartment(ApartmentState.STA)]
    public void RestoreAsync_AfterApply_RemovesAddedBlockAndRestoresEmptyDocument()
    {
        // Arrange
        EnsureApplication();
        var thread  = MakeThread();
        var loader  = MakeLoader(thread);
        var fixture = MakeFixture("""{"options":["OK"]}""");
        loader.ApplyAsync(fixture, CancellationToken.None).GetAwaiter().GetResult();
        Assert.That(thread.Document.Blocks.Count, Is.EqualTo(1), "precondition: block was added");

        // Act
        loader.RestoreAsync(CancellationToken.None).GetAwaiter().GetResult();

        // Assert
        Assert.That(thread.Document.Blocks.Count, Is.EqualTo(0));
    }

    // ── Reuse ─────────────────────────────────────────────────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void ApplyThenRestoreThenApply_LoaderIsReusable()
    {
        // Arrange
        EnsureApplication();
        var thread  = MakeThread();
        var loader  = MakeLoader(thread);
        var fixture = MakeFixture("""{"options":["Option A","Option B"]}""");

        // Act — first apply/restore cycle
        loader.ApplyAsync(fixture, CancellationToken.None).GetAwaiter().GetResult();
        loader.RestoreAsync(CancellationToken.None).GetAwaiter().GetResult();
        Assert.That(thread.Document.Blocks.Count, Is.EqualTo(0), "precondition: restored after first cycle");

        // Act — second apply cycle
        loader.ApplyAsync(fixture, CancellationToken.None).GetAwaiter().GetResult();

        // Assert
        Assert.That(thread.Document.Blocks.Count, Is.EqualTo(1));
    }
}
