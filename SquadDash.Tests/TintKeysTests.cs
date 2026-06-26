namespace SquadDash.Tests;

[TestFixture]
internal sealed class TintKeysTests {

    [Test]
    public void TintKeys_ContainsExpectedSurfaceKeys() {
        Assert.Multiple(() => {
            Assert.That(TintKeys.All, Does.Contain("AppSurface"));
            Assert.That(TintKeys.All, Does.Contain("TranscriptSurface"));
            Assert.That(TintKeys.All, Does.Contain("InputSurface"));
            Assert.That(TintKeys.All, Does.Contain("PanelBorder"));
            Assert.That(TintKeys.All, Does.Contain("BodyText"));
            Assert.That(TintKeys.All, Does.Contain("ScrollBarThumbBrush"));
        });
    }

    [Test]
    public void TintKeys_ExcludesSemanticStatusColors() {
        Assert.Multiple(() => {
            Assert.That(TintKeys.All, Does.Not.Contain("PriorityHigh"),
                "PriorityHigh is amber = mid-priority status; shifting it would collide with PriorityMid");
            Assert.That(TintKeys.All, Does.Not.Contain("ScreenshotAnchorUnnamed"),
                "ScreenshotAnchorUnnamed is amber = semantic annotation color");
        });
    }

    [Test]
    public void TintKeys_ExcludesSearchHighlightKeys() {
        Assert.Multiple(() => {
            Assert.That(TintKeys.All, Does.Not.Contain("SearchHighlight"));
            Assert.That(TintKeys.All, Does.Not.Contain("SearchHighlightCurrent"));
            Assert.That(TintKeys.All, Does.Not.Contain("SearchHighlightText"));
        });
    }

    [Test]
    public void TintKeys_CountMatchesKnownTotal() {
        // 30 surfaces + 2 chrome + 19 borders + 20 text + 4 scrollbar + 2 editor = 77
        // A lower count would indicate a duplicate in the source literal (silently dropped by HashSet)
        // or an accidental deletion.
        Assert.That(TintKeys.All.Count, Is.EqualTo(77));
    }

    [Test]
    public void TintKeys_ActiveAccentContainsExpectedKeys() {
        Assert.Multiple(() => {
            Assert.That(TintKeys.ActiveAccent, Does.Contain("QueueTabActiveBorder"));
            Assert.That(TintKeys.ActiveAccent, Does.Contain("ActivePanelSurface"));
            Assert.That(TintKeys.ActiveAccent, Does.Contain("ActivePanelBorder"));
            Assert.That(TintKeys.ActiveAccent, Does.Contain("ActivePanelTitle"));
            Assert.That(TintKeys.ActiveAccent, Does.Contain("ActivePanelSubtitle"));
        });
    }

    [Test]
    public void TintKeys_ActiveAccentDoesNotOverlapAll() {
        // ActiveAccent keys are rotated separately; they must not also be in All
        // or they would receive a double rotation.
        foreach (var key in TintKeys.ActiveAccent)
            Assert.That(TintKeys.All, Does.Not.Contain(key),
                $"'{key}' appears in both TintKeys.All and TintKeys.ActiveAccent — it would be rotated twice.");
    }
}
