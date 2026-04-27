using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Windows.Threading;
using SquadDash.Screenshots;
using SquadDash.Screenshots.Fixtures;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class BackgroundTaskFixtureLoaderTests
{
    private IReadOnlyList<SquadBackgroundAgentInfo> _agents = Array.Empty<SquadBackgroundAgentInfo>();
    private int _refreshCount;

    [SetUp]
    public void SetUp()
    {
        _agents       = Array.Empty<SquadBackgroundAgentInfo>();
        _refreshCount = 0;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private BackgroundTaskFixtureLoader MakeLoader() =>
        new BackgroundTaskFixtureLoader(
            getBackgroundAgents: () => _agents,
            setBackgroundAgents: list => _agents = list,
            refreshDisplay:      () => _refreshCount++,
            dispatcher:          Dispatcher.CurrentDispatcher);

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
    public void ApplyAsync_WithValidTasksArray_AddsCorrectNumberOfItems()
    {
        // Arrange
        var loader  = MakeLoader();
        var fixture = MakeFixture("""{"tasks":[{"title":"Task 1","status":"running"},{"title":"Task 2","status":"completed"}]}""");

        // Act
        loader.ApplyAsync(fixture, CancellationToken.None).GetAwaiter().GetResult();

        // Assert
        Assert.That(_agents.Count, Is.EqualTo(2));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ApplyAsync_WithMissingTasksKey_AddsNothing()
    {
        // Arrange
        var loader  = MakeLoader();
        var fixture = MakeFixture("""{"other":"value"}""");

        // Act
        loader.ApplyAsync(fixture, CancellationToken.None).GetAwaiter().GetResult();

        // Assert
        Assert.That(_agents, Is.Empty);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ApplyAsync_WithEmptyTasksArray_AddsNothing()
    {
        // Arrange
        var loader  = MakeLoader();
        var fixture = MakeFixture("""{"tasks":[]}""");

        // Act
        loader.ApplyAsync(fixture, CancellationToken.None).GetAwaiter().GetResult();

        // Assert
        Assert.That(_agents, Is.Empty);
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ApplyAsync_WithMultipleTasksIncludingDifferentStatuses_HandlesAllThreeStatuses()
    {
        // Arrange
        var loader  = MakeLoader();
        var fixture = MakeFixture(
            """{"tasks":[{"title":"Running","status":"running"},{"title":"Completed","status":"completed"},{"title":"Failed","status":"failed"}]}""");

        // Act
        loader.ApplyAsync(fixture, CancellationToken.None).GetAwaiter().GetResult();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(_agents.Count, Is.EqualTo(3));
            Assert.That(_agents[0].Status, Is.EqualTo("running"));
            Assert.That(_agents[1].Status, Is.EqualTo("completed"));
            Assert.That(_agents[2].Status, Is.EqualTo("failed"));
            // completed/failed tasks receive a CompletedAt timestamp; running tasks do not
            Assert.That(_agents[0].CompletedAt, Is.Null,     "running task must have null CompletedAt");
            Assert.That(_agents[1].CompletedAt, Is.Not.Null, "completed task must have CompletedAt set");
            Assert.That(_agents[2].CompletedAt, Is.Not.Null, "failed task must have CompletedAt set");
        });
    }

    // ── RestoreAsync ──────────────────────────────────────────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void RestoreAsync_WithoutPriorApply_IsIdempotentAndDoesNotThrow()
    {
        // Arrange
        var loader = MakeLoader();

        // Act / Assert
        Assert.DoesNotThrow(() =>
            loader.RestoreAsync(CancellationToken.None).GetAwaiter().GetResult());
    }

    [Test, Apartment(ApartmentState.STA)]
    public void RestoreAsync_AfterApply_RestoresOriginalAgentList()
    {
        // Arrange
        _agents = [new SquadBackgroundAgentInfo { AgentId = "original-agent" }];
        var loader  = MakeLoader();
        var fixture = MakeFixture("""{"tasks":[{"title":"Synthetic","status":"running"}]}""");
        loader.ApplyAsync(fixture, CancellationToken.None).GetAwaiter().GetResult();
        Assert.That(_agents.Count, Is.EqualTo(2), "precondition: synthetic item prepended");

        // Act
        loader.RestoreAsync(CancellationToken.None).GetAwaiter().GetResult();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(_agents.Count, Is.EqualTo(1));
            Assert.That(_agents[0].AgentId, Is.EqualTo("original-agent"));
        });
    }

    // ── Reuse ─────────────────────────────────────────────────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void ApplyThenRestoreThenApply_LoaderIsReusable()
    {
        // Arrange
        var loader  = MakeLoader();
        var fixture = MakeFixture("""{"tasks":[{"title":"Task A","status":"running"}]}""");

        // Act — first apply/restore cycle
        loader.ApplyAsync(fixture, CancellationToken.None).GetAwaiter().GetResult();
        loader.RestoreAsync(CancellationToken.None).GetAwaiter().GetResult();
        Assert.That(_agents, Is.Empty, "precondition: restored to empty after first cycle");

        // Act — second apply cycle
        loader.ApplyAsync(fixture, CancellationToken.None).GetAwaiter().GetResult();

        // Assert
        Assert.That(_agents.Count, Is.EqualTo(1));
    }
}
