using System;
using System.Threading;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Documents;
using NUnit.Framework;

namespace SquadDash.Tests;

/// <summary>
/// Tests the ShouldCollapse() helper on both thinking-block view types.
/// WPF controls require an STA thread.
/// </summary>
[TestFixture]
[Apartment(ApartmentState.STA)]
internal sealed class TranscriptThinkingBlockPinTests {

    // ── Factory helpers (construct minimal WPF objects) ──────────────────

    private static TranscriptThinkingBlockView MakeThinkingBlock() {
        // TranscriptTurnView requires a TranscriptThreadState and a Section/Block list.
        // We only need the block itself, so we supply nulls for the owner-turn fields
        // that aren't touched by ShouldCollapse().
        var turn = MakeTurn();
        return new TranscriptThinkingBlockView(
            turn,
            sequence: 1,
            headerTextBlock: new TextBlock(),
            expander: new Expander(),
            contentPanel: new StackPanel());
    }

    private static TranscriptThoughtBlockView MakeThoughtBlock() {
        var turn = MakeTurn();
        return new TranscriptThoughtBlockView(
            turn,
            sequence: 1,
            headerTextBlock: new TextBlock(),
            expander: new Expander(),
            contentPanel: new StackPanel());
    }

    private static TranscriptTurnView MakeTurn() {
        // TranscriptTurnView constructor:
        //   (TranscriptThreadState ownerThread, string prompt, DateTimeOffset startedAt,
        //    Section narrativeSection, IReadOnlyList<Block> topLevelBlocks)
        var threadState = new TranscriptThreadState(
            threadId: "test-thread",
            kind: TranscriptThreadKind.Coordinator,
            title: "Test",
            startedAt: DateTimeOffset.Now);
        return new TranscriptTurnView(
            threadState,
            prompt: string.Empty,
            startedAt: DateTimeOffset.Now,
            narrativeSection: new Section(),
            topLevelBlocks: Array.Empty<Block>());
    }

    // ── TranscriptThinkingBlockView ──────────────────────────────────────

    [Test]
    public void ThinkingBlock_ByDefault_ShouldCollapse_IsTrue() {
        var block = MakeThinkingBlock();
        Assert.That(block.ShouldCollapse(), Is.True);
    }

    [Test]
    public void ThinkingBlock_WhenUserPinnedOpen_ShouldCollapse_IsFalse() {
        var block = MakeThinkingBlock();
        block.UserPinnedOpen = true;
        Assert.That(block.ShouldCollapse(), Is.False);
    }

    [Test]
    public void ThinkingBlock_WhenPinnedThenUnpinned_ShouldCollapse_IsTrue() {
        var block = MakeThinkingBlock();
        block.UserPinnedOpen = true;
        block.UserPinnedOpen = false;
        Assert.That(block.ShouldCollapse(), Is.True);
    }

    // ── TranscriptThoughtBlockView ───────────────────────────────────────

    [Test]
    public void ThoughtBlock_ByDefault_ShouldCollapse_IsTrue() {
        var block = MakeThoughtBlock();
        Assert.That(block.ShouldCollapse(), Is.True);
    }

    [Test]
    public void ThoughtBlock_WhenUserPinnedOpen_ShouldCollapse_IsFalse() {
        var block = MakeThoughtBlock();
        block.UserPinnedOpen = true;
        Assert.That(block.ShouldCollapse(), Is.False);
    }

    [Test]
    public void ThoughtBlock_WhenPinnedThenUnpinned_ShouldCollapse_IsTrue() {
        var block = MakeThoughtBlock();
        block.UserPinnedOpen = true;
        block.UserPinnedOpen = false;
        Assert.That(block.ShouldCollapse(), Is.True);
    }
}
