using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
public class ButtonReorderGestureTests
{
    private static readonly Guid A = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid B = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid C = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid X = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Test]
    public void Move_StartsDragOnlyWhenHoldElapsedAndDeltaIsStrictlyOverThreshold()
    {
        var gesture = CreateGesture(dragged: B, thresholdX: 4, thresholdY: 4);

        var beforeHold = gesture.Move(TimeSpan.FromMilliseconds(349), x: 20, y: 10, insidePanel: true, captureValid: true);
        var atThreshold = gesture.Move(TimeSpan.FromMilliseconds(350), x: 14, y: 10, insidePanel: true, captureValid: true);
        var overThreshold = gesture.Move(TimeSpan.FromMilliseconds(350), x: 15, y: 10, insidePanel: true, captureValid: true);

        Assert.Multiple(() =>
        {
            Assert.That(beforeHold, Is.False);
            Assert.That(atThreshold, Is.False);
            Assert.That(overThreshold, Is.True);
            Assert.That(gesture.Phase, Is.EqualTo(ButtonReorderPhase.Dragging));
            Assert.That(gesture.SuppressExecution, Is.True);
        });
    }

    [TestCase(0, 10, 10)]
    [TestCase(349, 20, 20)]
    [TestCase(1000, 10, 10)]
    public void Release_WhileStillPressed_IsClickWithoutSuppression(int elapsed, double x, double y)
    {
        var gesture = CreateGesture(dragged: B);
        Assert.That(gesture.Move(TimeSpan.FromMilliseconds(elapsed), x, y, true, true), Is.False);

        var release = gesture.Release(20, 20, new ButtonReorderBounds(0, 0, 200, 120),
            new ButtonReorderBounds(10, 10, 100, 40), captureValid: true);

        Assert.Multiple(() =>
        {
            Assert.That(release, Is.EqualTo(ButtonReorderRelease.Click));
            Assert.That(gesture.Phase, Is.EqualTo(ButtonReorderPhase.ClickReleased));
            Assert.That(gesture.SuppressExecution, Is.False);
        });
    }

    [Test]
    public void DragThirdToFirst_CommitProducesExpectedVisibleAndFullOrder()
    {
        var gesture = CreateGesture(dragged: C);
        gesture.Move(TimeSpan.FromMilliseconds(350), x: 20, y: 30, insidePanel: true, captureValid: true);
        gesture.PreviewMove(targetIndex: 0);

        var release = gesture.Release(20, 20, new ButtonReorderBounds(0, 0, 300, 200),
            new ButtonReorderBounds(10, 10, 120, 60), captureValid: true);

        Assert.Multiple(() =>
        {
            Assert.That(release, Is.EqualTo(ButtonReorderRelease.Commit));
            Assert.That(gesture.Phase, Is.EqualTo(ButtonReorderPhase.Committed));
            Assert.That(gesture.SuppressExecution, Is.True);
            Assert.That(gesture.PreviewVisibleOrder, Is.EqualTo(new[] { C, A, B }));
            Assert.That(gesture.PreviewFullOrder, Is.EqualTo(new[] { C, A, B }));
        });
    }

    [Test]
    public void Move_CaptureLoss_CancelsAndSuppressesExecution()
    {
        var gesture = CreateGesture(dragged: B);

        var dragging = gesture.Move(TimeSpan.FromMilliseconds(350), x: 30, y: 30, insidePanel: true, captureValid: true);
        var afterLoss = gesture.Move(TimeSpan.FromMilliseconds(360), x: 32, y: 32, insidePanel: true, captureValid: false);

        Assert.Multiple(() =>
        {
            Assert.That(dragging, Is.True);
            Assert.That(afterLoss, Is.False);
            Assert.That(gesture.Phase, Is.EqualTo(ButtonReorderPhase.Cancelled));
            Assert.That(gesture.SuppressExecution, Is.True);
        });
    }

    [Test]
    public void OnceCancelledByLeavingPanel_ReEntryDoesNotResumeDragging()
    {
        var gesture = CreateGesture(dragged: B);
        gesture.Move(TimeSpan.FromMilliseconds(350), x: 30, y: 30, insidePanel: true, captureValid: true);
        gesture.PreviewMove(0);

        var cancelMove = gesture.Move(TimeSpan.FromMilliseconds(360), x: 500, y: 500, insidePanel: false, captureValid: true);
        var reenterMove = gesture.Move(TimeSpan.FromMilliseconds(370), x: 30, y: 30, insidePanel: true, captureValid: true);

        Assert.Multiple(() =>
        {
            Assert.That(cancelMove, Is.False);
            Assert.That(reenterMove, Is.False);
            Assert.That(gesture.Phase, Is.EqualTo(ButtonReorderPhase.Cancelled));
            Assert.That(gesture.PreviewFullOrder, Is.EqualTo(new[] { A, B, C }));
            Assert.That(gesture.SuppressExecution, Is.True);
            Assert.That(gesture.Release(20, 20, new(0, 0, 200, 200), new(10, 10, 100, 50), true),
                Is.EqualTo(ButtonReorderRelease.Cancel));
        });
    }

    [TestCase(180, 180, true, TestName = "ReleaseInBlankPanelSpace_RestoresOriginalOrder")]
    [TestCase(250, 20, true, TestName = "ReleaseOutsidePanel_RestoresOriginalOrder")]
    [TestCase(20, 20, false, TestName = "ReleaseWithoutCapture_RestoresOriginalOrder")]
    public void DragThirdToFirst_InvalidRelease_CancelsAndSuppressesExecution(double x, double y, bool captured)
    {
        var gesture = CreateGesture(dragged: C);
        gesture.Move(TimeSpan.FromMilliseconds(350), 20, 30, true, true);
        gesture.PreviewMove(0);
        Assert.That(gesture.PreviewFullOrder, Is.EqualTo(new[] { C, A, B }));

        var outcome = gesture.Release(x, y, new(0, 0, 200, 200), new(10, 10, 100, 50), captured);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(ButtonReorderRelease.Cancel));
            Assert.That(gesture.Phase, Is.EqualTo(ButtonReorderPhase.Cancelled));
            Assert.That(gesture.PreviewFullOrder, Is.EqualTo(new[] { A, B, C }));
            Assert.That(gesture.SuppressExecution, Is.True);
        });
    }

    [Test]
    public void Cancel_RestoresPreviewAndIgnoresFurtherPreviewMoves()
    {
        var gesture = CreateGesture(dragged: C);
        gesture.Move(TimeSpan.FromMilliseconds(350), 20, 30, true, true);
        gesture.PreviewMove(0);
        gesture.Cancel();
        gesture.PreviewMove(1);
        Assert.Multiple(() =>
        {
            Assert.That(gesture.PreviewFullOrder, Is.EqualTo(new[] { A, B, C }));
            Assert.That(gesture.Phase, Is.EqualTo(ButtonReorderPhase.Cancelled));
            Assert.That(gesture.SuppressExecution, Is.True);
        });
    }

    [Test]
    public void MapVisibleOrder_ReplacesOnlyVisibleSlots()
    {
        var mapped = ButtonReorderGesture.MapVisibleOrder(
            persistedOrder: new[] { A, X, B, C },
            originalVisibleOrder: new[] { A, B, C },
            previewVisibleOrder: new[] { C, A, B });

        Assert.That(mapped, Is.EqualTo(new[] { C, X, A, B }));
    }

    [Test]
    public void Constructor_CopiesInputOrders()
    {
        var persisted = new List<Guid> { A, B, C };
        var visible = new List<Guid> { A, B, C };
        var gesture = new ButtonReorderGesture(persisted, visible, B, 10, 10, 1, 1);

        persisted.Reverse();
        visible[0] = C;

        Assert.Multiple(() =>
        {
            Assert.That(gesture.PersistedOrder, Is.EqualTo(new[] { A, B, C }));
            Assert.That(gesture.OriginalVisibleOrder, Is.EqualTo(new[] { A, B, C }));
            Assert.That(gesture.PreviewVisibleOrder, Is.EqualTo(new[] { A, B, C }));
        });
    }

    [Test]
    public void PreviewMove_KeepsDraggedIdStableAcrossReorders()
    {
        var gesture = CreateGesture(dragged: B);
        gesture.Move(TimeSpan.FromMilliseconds(350), x: 30, y: 30, insidePanel: true, captureValid: true);

        gesture.PreviewMove(targetIndex: 2);
        var afterFirst = gesture.PreviewVisibleOrder.ToArray();
        gesture.PreviewMove(targetIndex: 0);
        var afterSecond = gesture.PreviewVisibleOrder.ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(afterFirst, Is.EqualTo(new[] { A, C, B }));
            Assert.That(afterSecond, Is.EqualTo(new[] { B, A, C }));
            Assert.That(afterSecond.Count(id => id == B), Is.EqualTo(1));
        });
    }

    [TestCaseSource(nameof(InvalidMapVisibleOrderCases))]
    public void MapVisibleOrder_InvalidPermutations_Throw(
        IReadOnlyList<Guid> persisted,
        IReadOnlyList<Guid> originalVisible,
        IReadOnlyList<Guid> previewVisible)
    {
        Assert.That(() => ButtonReorderGesture.MapVisibleOrder(persisted, originalVisible, previewVisible),
            Throws.TypeOf<ArgumentException>());
    }

    private static IEnumerable<TestCaseData> InvalidMapVisibleOrderCases()
    {
        yield return new TestCaseData(
            new[] { A, X, B, C },
            new[] { B, A, C },
            new[] { C, A, B }).SetName("OriginalVisibleMustMatchPersistedVisibleSlots");

        yield return new TestCaseData(
            new[] { A, X, B, C },
            new[] { A, B, C },
            new[] { C, A, A }).SetName("PreviewVisibleMustBePermutation");

        yield return new TestCaseData(
            new[] { A, X, B, B },
            new[] { A, B, B },
            new[] { B, A, B }).SetName("PersistedOrderMustNotContainDuplicates");
    }

    private static ButtonReorderGesture CreateGesture(Guid dragged, double thresholdX = 2, double thresholdY = 2)
    {
        return new ButtonReorderGesture(
            persistedOrder: new[] { A, B, C },
            visibleOrder: new[] { A, B, C },
            draggedId: dragged,
            startX: 10,
            startY: 10,
            thresholdX: thresholdX,
            thresholdY: thresholdY);
    }
}
