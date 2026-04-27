using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Threading;
using SquadDash.Screenshots;
using SquadDash.Screenshots.Fixtures;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class PromptTextFixtureLoaderTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private PromptTextFixtureLoader MakeLoader(TextBox promptTextBox) =>
        new PromptTextFixtureLoader(
            promptTextBox: promptTextBox,
            dispatcher:    Dispatcher.CurrentDispatcher);

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
    public void KnownKeys_ContainsPromptTextKey()
    {
        var loader = MakeLoader(new TextBox());

        Assert.That(loader.KnownKeys, Is.EqualTo(new[] { "promptText" }));
    }

    // ── ApplyAsync — key absent ───────────────────────────────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void ApplyAsync_WithMissingPromptTextKey_LeavesTextUnchanged()
    {
        var tb      = new TextBox { Text = "original" };
        var loader  = MakeLoader(tb);
        var fixture = MakeFixture("""{"other":"value"}""");

        loader.ApplyAsync(fixture, CancellationToken.None).GetAwaiter().GetResult();

        Assert.That(tb.Text, Is.EqualTo("original"));
    }

    // ── ApplyAsync — null value ───────────────────────────────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void ApplyAsync_WithNullPromptTextValue_LeavesTextUnchanged()
    {
        var tb      = new TextBox { Text = "original" };
        var loader  = MakeLoader(tb);
        var fixture = MakeFixture("""{"promptText":null}""");

        loader.ApplyAsync(fixture, CancellationToken.None).GetAwaiter().GetResult();

        Assert.That(tb.Text, Is.EqualTo("original"));
    }

    // ── ApplyAsync — valid text ───────────────────────────────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void ApplyAsync_WithValidPromptText_SetsTextBoxText()
    {
        var tb      = new TextBox { Text = string.Empty };
        var loader  = MakeLoader(tb);
        var fixture = MakeFixture("""{"promptText":"Hello, world!"}""");

        loader.ApplyAsync(fixture, CancellationToken.None).GetAwaiter().GetResult();

        Assert.That(tb.Text, Is.EqualTo("Hello, world!"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ApplyAsync_WithEmptyPromptText_SetsTextBoxToEmpty()
    {
        var tb      = new TextBox { Text = "some existing text" };
        var loader  = MakeLoader(tb);
        var fixture = MakeFixture("""{"promptText":""}""");

        loader.ApplyAsync(fixture, CancellationToken.None).GetAwaiter().GetResult();

        Assert.That(tb.Text, Is.EqualTo(string.Empty));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ApplyAsync_WithMultilinePromptText_SetsFullText()
    {
        var tb      = new TextBox { Text = string.Empty, AcceptsReturn = true };
        var loader  = MakeLoader(tb);
        var fixture = MakeFixture("""{"promptText":"line1\nline2"}""");

        loader.ApplyAsync(fixture, CancellationToken.None).GetAwaiter().GetResult();

        Assert.That(tb.Text, Is.EqualTo("line1\nline2"));
    }

    // ── RestoreAsync ──────────────────────────────────────────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void RestoreAsync_WithoutPriorApply_IsIdempotentAndDoesNotThrow()
    {
        var loader = MakeLoader(new TextBox());

        Assert.DoesNotThrow(() =>
            loader.RestoreAsync(CancellationToken.None).GetAwaiter().GetResult());
    }

    [Test, Apartment(ApartmentState.STA)]
    public void RestoreAsync_AfterApply_RestoresOriginalText()
    {
        var tb      = new TextBox { Text = "original text" };
        var loader  = MakeLoader(tb);
        var fixture = MakeFixture("""{"promptText":"fixture text"}""");

        loader.ApplyAsync(fixture, CancellationToken.None).GetAwaiter().GetResult();
        Assert.That(tb.Text, Is.EqualTo("fixture text"), "precondition: fixture text applied");

        loader.RestoreAsync(CancellationToken.None).GetAwaiter().GetResult();

        Assert.That(tb.Text, Is.EqualTo("original text"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void RestoreAsync_AfterApply_WithEmptyOriginal_RestoresEmptyText()
    {
        var tb      = new TextBox { Text = string.Empty };
        var loader  = MakeLoader(tb);
        var fixture = MakeFixture("""{"promptText":"some prompt"}""");

        loader.ApplyAsync(fixture, CancellationToken.None).GetAwaiter().GetResult();
        loader.RestoreAsync(CancellationToken.None).GetAwaiter().GetResult();

        Assert.That(tb.Text, Is.EqualTo(string.Empty));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void RestoreAsync_CalledTwice_IsIdempotent()
    {
        var tb      = new TextBox { Text = "original" };
        var loader  = MakeLoader(tb);
        var fixture = MakeFixture("""{"promptText":"fixture"}""");

        loader.ApplyAsync(fixture, CancellationToken.None).GetAwaiter().GetResult();
        loader.RestoreAsync(CancellationToken.None).GetAwaiter().GetResult();

        // Second restore should be a no-op — text stays as restored original
        loader.RestoreAsync(CancellationToken.None).GetAwaiter().GetResult();

        Assert.That(tb.Text, Is.EqualTo("original"));
    }

    // ── Reuse ─────────────────────────────────────────────────────────────────

    [Test, Apartment(ApartmentState.STA)]
    public void ApplyThenRestoreThenApply_LoaderIsReusable()
    {
        var tb      = new TextBox { Text = "original" };
        var loader  = MakeLoader(tb);
        var fixture = MakeFixture("""{"promptText":"fixture"}""");

        // First apply/restore cycle
        loader.ApplyAsync(fixture, CancellationToken.None).GetAwaiter().GetResult();
        loader.RestoreAsync(CancellationToken.None).GetAwaiter().GetResult();
        Assert.That(tb.Text, Is.EqualTo("original"), "precondition: restored after first cycle");

        // Second apply cycle — loader must be reusable
        loader.ApplyAsync(fixture, CancellationToken.None).GetAwaiter().GetResult();

        Assert.That(tb.Text, Is.EqualTo("fixture"));
    }
}
