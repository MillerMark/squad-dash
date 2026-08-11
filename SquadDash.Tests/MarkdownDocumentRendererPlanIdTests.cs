using SquadDash;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class MarkdownDocumentRendererPlanIdTests {

    // --- Positive: real plan ID patterns ---

    [TestCase("MODELPROF-20260810",       0, "MODELPROF-20260810")]
    [TestCase("ROUTEPROBE-20260728",      0, "ROUTEPROBE-20260728")]
    [TestCase("HANDOFFPROBE-20260804",    0, "HANDOFFPROBE-20260804")]
    [TestCase("GODCLASS-20260725",        0, "GODCLASS-20260725")]
    [TestCase("ROUTEPROBE-20260728-001",  0, "ROUTEPROBE-20260728-001")]
    [TestCase("MODELPROF-20260810-999",   0, "MODELPROF-20260810-999")]
    public void TryReadPlanId_ReturnsTrue_ForValidPlanIds(string text, int startIndex, string expected) {
        var result = MarkdownDocumentRenderer.TryReadPlanId(text, startIndex, out var nextIndex, out var planId);
        Assert.Multiple(() => {
            Assert.That(result, Is.True);
            Assert.That(planId, Is.EqualTo(expected));
            Assert.That(nextIndex, Is.EqualTo(expected.Length + startIndex));
        });
    }

    [Test]
    public void TryReadPlanId_ReturnsTrue_WhenEmbeddedInProseSentence() {
        const string text = "Plan MODELPROF-20260810 stopped before the current task was accepted";
        var result = MarkdownDocumentRenderer.TryReadPlanId(text, 5, out var nextIndex, out var planId);
        Assert.Multiple(() => {
            Assert.That(result, Is.True);
            Assert.That(planId, Is.EqualTo("MODELPROF-20260810"));
            Assert.That(nextIndex, Is.EqualTo(23));
        });
    }

    [TestCase("(MODELPROF-20260810)", 1)]
    [TestCase("[MODELPROF-20260810]", 1)]
    [TestCase("MODELPROF-20260810.", 0)]
    [TestCase("MODELPROF-20260810,", 0)]
    [TestCase("MODELPROF-20260810!", 0)]
    [TestCase("MODELPROF-20260810?", 0)]
    public void TryReadPlanId_ReturnsTrue_WithProseDelimiters(string text, int startIndex) {
        var result = MarkdownDocumentRenderer.TryReadPlanId(text, startIndex, out _, out var planId);
        Assert.Multiple(() => {
            Assert.That(result, Is.True);
            Assert.That(planId, Is.EqualTo("MODELPROF-20260810"));
        });
    }

    [Test]
    public void TryReadPlanId_ReturnsTrue_AtStartOfString() {
        const string text = "GODCLASS-20260725";
        var result = MarkdownDocumentRenderer.TryReadPlanId(text, 0, out var nextIndex, out var planId);
        Assert.Multiple(() => {
            Assert.That(result, Is.True);
            Assert.That(planId, Is.EqualTo("GODCLASS-20260725"));
            Assert.That(nextIndex, Is.EqualTo(text.Length));
        });
    }

    [Test]
    public void TryReadPlanId_ReturnsTrue_WithMultiSegmentPrefix() {
        // Multi-word uppercase prefix: e.g. HAND-OFF-20260804 is not valid (lowercase in prefix)
        // but HANDOFFPROBE-20260804 should be fine — test a two-part uppercase prefix
        const string text = "PLAN-20260810";
        var result = MarkdownDocumentRenderer.TryReadPlanId(text, 0, out _, out var planId);
        Assert.Multiple(() => {
            Assert.That(result, Is.True);
            Assert.That(planId, Is.EqualTo("PLAN-20260810"));
        });
    }

    // --- Negative: plan ID must not match these ---

    [Test]
    public void TryReadPlanId_ReturnsFalse_ForLowercasePlanId() {
        const string text = "modelprof-20260810";
        var anyMatch = false;
        for (var i = 0; i < text.Length; i++) {
            if (MarkdownDocumentRenderer.TryReadPlanId(text, i, out _, out _))
                anyMatch = true;
        }
        Assert.That(anyMatch, Is.False);
    }

    [Test]
    public void TryReadPlanId_ReturnsFalse_ForMixedCasePlanId() {
        const string text = "ModelProf-20260810";
        var anyMatch = false;
        for (var i = 0; i < text.Length; i++) {
            if (MarkdownDocumentRenderer.TryReadPlanId(text, i, out _, out _))
                anyMatch = true;
        }
        Assert.That(anyMatch, Is.False);
    }

    [Test]
    public void TryReadPlanId_ReturnsFalse_WhenDateIsSevenDigits() {
        const string text = "MODELPROF-2026081";  // 7 digits, not 8
        var anyMatch = false;
        for (var i = 0; i < text.Length; i++) {
            if (MarkdownDocumentRenderer.TryReadPlanId(text, i, out _, out _))
                anyMatch = true;
        }
        Assert.That(anyMatch, Is.False);
    }

    [Test]
    public void TryReadPlanId_ReturnsFalse_WhenNoHyphenBeforeDate() {
        const string text = "MODELPROF20260810";
        var anyMatch = false;
        for (var i = 0; i < text.Length; i++) {
            if (MarkdownDocumentRenderer.TryReadPlanId(text, i, out _, out _))
                anyMatch = true;
        }
        Assert.That(anyMatch, Is.False);
    }

    [Test]
    public void TryReadPlanId_ReturnsFalse_WhenPrecededByUppercaseLetter() {
        // "XMODELPROF-20260810" — 'X' directly precedes, no boundary
        const string text = "XMODELPROF-20260810";
        var result = MarkdownDocumentRenderer.TryReadPlanId(text, 1, out _, out _);
        Assert.That(result, Is.False);
    }

    [Test]
    public void TryReadPlanId_ReturnsFalse_WhenFollowedByLetter() {
        // "MODELPROF-20260810X" — 'X' directly follows, no boundary
        const string text = "MODELPROF-20260810X";
        var result = MarkdownDocumentRenderer.TryReadPlanId(text, 0, out _, out _);
        Assert.That(result, Is.False);
    }

    [Test]
    public void TryReadPlanId_ReturnsFalse_ForDateWithLetters() {
        const string text = "MODELPROF-2026X810";
        var anyMatch = false;
        for (var i = 0; i < text.Length; i++) {
            if (MarkdownDocumentRenderer.TryReadPlanId(text, i, out _, out _))
                anyMatch = true;
        }
        Assert.That(anyMatch, Is.False);
    }

    [Test]
    public void TryReadPlanId_ReturnsFalse_ForPlainEnglishWord() {
        const string text = "succeeded";
        var anyMatch = false;
        for (var i = 0; i < text.Length; i++) {
            if (MarkdownDocumentRenderer.TryReadPlanId(text, i, out _, out _))
                anyMatch = true;
        }
        Assert.That(anyMatch, Is.False);
    }

    [Test]
    public void TryReadPlanId_ReturnsFalse_ForAllCapsWordWithNoDate() {
        const string text = "IMPORTANT";
        var anyMatch = false;
        for (var i = 0; i < text.Length; i++) {
            if (MarkdownDocumentRenderer.TryReadPlanId(text, i, out _, out _))
                anyMatch = true;
        }
        Assert.That(anyMatch, Is.False);
    }
}
