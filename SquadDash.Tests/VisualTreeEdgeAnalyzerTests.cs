using System.Windows;

namespace SquadDash.Tests;

/// <summary>
/// Unit tests for <see cref="VisualTreeEdgeAnalyzer.FindAnchorFromCandidates"/>.
///
/// The real <see cref="VisualTreeEdgeAnalyzer.Analyze"/> entry-point walks a live
/// WPF visual tree, which requires an STA thread and a real window.  These tests
/// operate at the pure-geometry layer instead: candidates are plain (Rect, name?)
/// tuples that bypass the WPF dependency entirely.
///
/// Standard capture region used across most tests:
///   Left=100, Top=100, Right=300, Bottom=300  (Width=200, Height=200)
/// </summary>
[TestFixture]
internal sealed class VisualTreeEdgeAnalyzerTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    /// Capture region shared by most tests.
    private static readonly Rect _capture = new(100, 100, 200, 200);

    /// Shorthand for building a candidate tuple.
    private static (Rect Bounds, string? Name) El(
        double x, double y, double w, double h, string? name = null)
        => (new Rect(x, y, w, h), name);

    /// Invoke the testable method under test.
    private static (IReadOnlyList<string> Names, Rect Bounds, double Distance, bool NeedsName, bool Found)
        Analyze(string edge, IEnumerable<(Rect Bounds, string? Name)> candidates)
        => VisualTreeEdgeAnalyzer.FindAnchorFromCandidates(candidates, _capture, edge);

    // ── Top edge ─────────────────────────────────────────────────────────────

    [Test]
    public void Top_SingleNamedElementWithTopInsideRegion_ReturnsCorrectNameAndDistance()
    {
        // Element at (150,120,50,50): Top=120, distance from region.Top(100) = 20.
        var result = Analyze("Top", [El(150, 120, 50, 50, "btnOK")]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Found,     Is.True);
            Assert.That(result.Names,     Contains.Item("btnOK"));
            Assert.That(result.Distance,  Is.EqualTo(20.0).Within(1e-9));
            Assert.That(result.NeedsName, Is.False);
        });
    }

    [Test]
    public void Top_NamedAndAnonymousBothQualify_NamedWinsEvenIfAnonymousIsCloser()
    {
        // Anonymous (closer) : Top=105, dist=5
        // Named "btnOK"      : Top=120, dist=20
        var result = Analyze("Top",
        [
            El(150, 105, 50, 50),           // anonymous — dist 5, closer
            El(150, 120, 50, 50, "btnOK"),  // named     — dist 20, farther
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Found,     Is.True);
            Assert.That(result.Names,     Contains.Item("btnOK"));
            Assert.That(result.NeedsName, Is.False);
        });
    }

    [Test]
    public void Top_OnlyAnonymousElementQualifies_NeedsNameIsTrue()
    {
        // No name supplied → NeedsName should be flagged.
        var result = Analyze("Top", [El(150, 120, 50, 50)]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Found,     Is.True);
            Assert.That(result.Names,     Is.Empty);
            Assert.That(result.NeedsName, Is.True);
        });
    }

    [Test]
    public void Top_NoElementWithTopInsideRegion_ReturnsNotFound()
    {
        // Element extends into the region vertically (Bottom > region.Top) but its
        // Top edge (50) is above region.Top (100) — does not qualify.
        var result = Analyze("Top", [El(150, 50, 50, 500)]);

        Assert.That(result.Found, Is.False);
    }

    [Test]
    public void Top_ElementTopExactlyOnRegionTopBoundary_Qualifies()
    {
        // Top == region.Top → inclusive boundary; distance = 0.
        var result = Analyze("Top", [El(150, 100, 50, 50, "el")]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Found,    Is.True);
            Assert.That(result.Distance, Is.EqualTo(0.0).Within(1e-9));
        });
    }

    [Test]
    public void Top_ElementTopExactlyOnRegionBottomBoundary_Qualifies()
    {
        // Top == region.Bottom (300) → inclusive boundary; distance = |300-100| = 200.
        var result = Analyze("Top", [El(150, 300, 50, 50, "el")]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Found,    Is.True);
            Assert.That(result.Distance, Is.EqualTo(200.0).Within(1e-9));
        });
    }

    [Test]
    public void Top_ElementTopJustOutsideRegion_DoesNotQualify()
    {
        // Top=99 is one pixel above region.Top(100);
        // Top=301 is one pixel below region.Bottom(300).
        var result = Analyze("Top",
        [
            El(150,  99, 50, 50, "above"),
            El(150, 301, 50, 50, "below"),
        ]);

        Assert.That(result.Found, Is.False);
    }

    // ── Right edge ───────────────────────────────────────────────────────────

    [Test]
    public void Right_SingleNamedElementWithRightInsideRegion_ReturnsFound()
    {
        // Element at (230,150,50,50): Right=280, dist=|280-300|=20.
        var result = Analyze("Right", [El(230, 150, 50, 50, "inputBox")]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Found,    Is.True);
            Assert.That(result.Names,    Contains.Item("inputBox"));
            Assert.That(result.Distance, Is.EqualTo(20.0).Within(1e-9));
        });
    }

    [Test]
    public void Right_NamedAndAnonymousBothQualify_NamedWinsEvenIfAnonymousIsCloser()
    {
        // Anonymous: Right=290, dist=10 (closer)
        // Named "inputBox": Right=280, dist=20
        var result = Analyze("Right",
        [
            El(240, 150, 50, 50),              // anonymous — Right=290, dist=10
            El(230, 150, 50, 50, "inputBox"),  // named     — Right=280, dist=20
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Found,     Is.True);
            Assert.That(result.Names,     Contains.Item("inputBox"));
            Assert.That(result.NeedsName, Is.False);
        });
    }

    [Test]
    public void Right_ElementRightExactlyOnRegionBoundaries_Qualifies()
    {
        // Right == region.Left (100): qualifies; dist = |100-300| = 200.
        // Right == region.Right (300): qualifies; dist = 0.
        var leftBound  = Analyze("Right", [El( 50, 150, 50, 50, "left")]);  // Right=100
        var rightBound = Analyze("Right", [El(250, 150, 50, 50, "right")]); // Right=300

        Assert.Multiple(() =>
        {
            Assert.That(leftBound.Found,    Is.True,  "Right==region.Left should qualify");
            Assert.That(leftBound.Distance, Is.EqualTo(200.0).Within(1e-9));
            Assert.That(rightBound.Found,   Is.True,  "Right==region.Right should qualify");
            Assert.That(rightBound.Distance, Is.EqualTo(0.0).Within(1e-9));
        });
    }

    // ── Bottom edge ──────────────────────────────────────────────────────────

    [Test]
    public void Bottom_SingleNamedElementWithBottomInsideRegion_ReturnsFound()
    {
        // Element at (150,200,50,50): Bottom=250, dist=|250-300|=50.
        var result = Analyze("Bottom", [El(150, 200, 50, 50, "panel")]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Found,    Is.True);
            Assert.That(result.Names,    Contains.Item("panel"));
            Assert.That(result.Distance, Is.EqualTo(50.0).Within(1e-9));
        });
    }

    [Test]
    public void Bottom_NamedAndAnonymousBothQualify_NamedWinsEvenIfAnonymousIsCloser()
    {
        // Anonymous: Bottom=280, dist=20 (closer)
        // Named "panel": Bottom=250, dist=50
        var result = Analyze("Bottom",
        [
            El(150, 230, 50, 50),           // anonymous — Bottom=280, dist=20
            El(150, 200, 50, 50, "panel"),  // named     — Bottom=250, dist=50
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Found,     Is.True);
            Assert.That(result.Names,     Contains.Item("panel"));
            Assert.That(result.NeedsName, Is.False);
        });
    }

    // ── Left edge ────────────────────────────────────────────────────────────

    [Test]
    public void Left_SingleNamedElementWithLeftInsideRegion_ReturnsFound()
    {
        // Element at (120,150,50,50): Left=120, dist=|120-100|=20.
        var result = Analyze("Left", [El(120, 150, 50, 50, "sidebar")]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Found,    Is.True);
            Assert.That(result.Names,    Contains.Item("sidebar"));
            Assert.That(result.Distance, Is.EqualTo(20.0).Within(1e-9));
        });
    }

    [Test]
    public void Left_NamedAndAnonymousBothQualify_NamedWinsEvenIfAnonymousIsCloser()
    {
        // Anonymous: Left=105, dist=5 (closer)
        // Named "sidebar": Left=120, dist=20
        var result = Analyze("Left",
        [
            El(105, 150, 50, 50),             // anonymous — Left=105, dist=5
            El(120, 150, 50, 50, "sidebar"),  // named     — Left=120, dist=20
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Found,     Is.True);
            Assert.That(result.Names,     Contains.Item("sidebar"));
            Assert.That(result.NeedsName, Is.False);
        });
    }

    // ── General ──────────────────────────────────────────────────────────────

    [Test]
    public void General_EmptyCandidateList_ReturnsNotFound()
    {
        var result = Analyze("Top", []);

        Assert.Multiple(() =>
        {
            Assert.That(result.Found,     Is.False);
            Assert.That(result.NeedsName, Is.False);
            Assert.That(result.Distance,  Is.EqualTo(0.0).Within(1e-9));
        });
    }

    [Test]
    public void General_MultipleNamedElementsQualify_ClosestOneWins()
    {
        // "far"  : Top=180, dist=80
        // "close": Top=110, dist=10  ← should win
        var result = Analyze("Top",
        [
            El(150, 180, 50, 50, "far"),
            El(150, 110, 50, 50, "close"),
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Found,    Is.True);
            Assert.That(result.Names,    Contains.Item("close"));
            Assert.That(result.Distance, Is.EqualTo(10.0).Within(1e-9));
        });
    }

    [Test]
    public void General_MultipleNamedElementsAtExactlyTheSameDistance_AllNamesInResult()
    {
        // "btn1" and "btn2" both have Top=110 — distance = |110−100| = 10, an exact tie.
        // Both names must appear in the result.
        var result = Analyze("Top",
        [
            El(130, 110, 50, 50, "btn1"),  // Top=110, dist=10
            El(180, 110, 50, 50, "btn2"),  // Top=110, dist=10 — exact tie
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Found,     Is.True);
            Assert.That(result.Names,     Contains.Item("btn1"));
            Assert.That(result.Names,     Contains.Item("btn2"));
            Assert.That(result.Distance,  Is.EqualTo(10.0).Within(1e-9));
            Assert.That(result.NeedsName, Is.False);
        });
    }

    [Test]
    public void General_ZeroSizeElementPassedAsCandidate_StillQualifiesGeometrically()
    {
        // Zero-size elements are filtered by TryAddCandidate in the live WPF path
        // (RenderSize.Width <= 0 || RenderSize.Height <= 0).  This test documents
        // that FindAnchorFromCandidates does NOT enforce a size restriction — a
        // zero-size Rect whose edge satisfies the qualification conditions will match.
        //
        // Rect(150, 120, 0, 0): Top=120 ∈ [100,300] ✓
        //                       Right=150 > 100 ✓  Left=150 < 300 ✓
        //                       → qualifies; dist = |120-100| = 20
        var result = Analyze("Top", [El(150, 120, 0, 0, "zeroSize")]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Found,    Is.True,
                "Zero-size rect at a qualifying position passes the geometry check");
            Assert.That(result.Distance, Is.EqualTo(20.0).Within(1e-9));
        });
    }
}
