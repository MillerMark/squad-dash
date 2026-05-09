namespace SquadDash.Tests;

[TestFixture]
internal sealed class TasksPanelFilterTests {

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (IReadOnlyList<string>? owners, string text) Parse(
        string filter,
        IReadOnlyList<SquadTeamMember>? roster = null)
        => TasksPanelFilter.Parse(filter, roster);

    private static SquadTeamMember Member(string name, string? folderPath = null)
        => new(name, "dev", "active", null, null, folderPath, false, "AccentBlue");

    private static readonly string Sentinel = TasksPanelFilter.UserOwnedSentinel;

    // ── Empty / no-op inputs ─────────────────────────────────────────────────

    [Test]
    public void EmptyFilter_ReturnsNullOwners_AndEmptyText() {
        var (owners, text) = Parse("");

        Assert.Multiple(() => {
            Assert.That(owners, Is.Null);
            Assert.That(text,   Is.Empty);
        });
    }

    [Test]
    public void BareAt_ReturnsNullOwners_AndEmptyText() {
        var (owners, text) = Parse("@");

        Assert.Multiple(() => {
            Assert.That(owners, Is.Null);
            Assert.That(text,   Is.Empty);
        });
    }

    // ── Plain text filter (no @) ──────────────────────────────────────────────

    [Test]
    public void PlainText_ReturnsNullOwners_AndOriginalText() {
        var (owners, text) = Parse("crash fix");

        Assert.Multiple(() => {
            Assert.That(owners, Is.Null);
            Assert.That(text,   Is.EqualTo("crash fix"));
        });
    }

    // ── @me ───────────────────────────────────────────────────────────────────

    [Test]
    public void AtMe_ExactMatch_ReturnsSentinel_AndEmptyText() {
        var (owners, text) = Parse("@me");

        Assert.Multiple(() => {
            Assert.That(owners, Is.EqualTo(new[] { Sentinel }));
            Assert.That(text,   Is.Empty);
        });
    }

    [Test]
    public void AtMe_CaseInsensitive_ReturnsSentinel() {
        var (owners, _) = Parse("@ME");

        Assert.That(owners, Is.EqualTo(new[] { Sentinel }));
    }

    [Test]
    public void AtMe_WithTrailingText_ReturnsSentinel_AndText() {
        var (owners, text) = Parse("@me fix the crash");

        Assert.Multiple(() => {
            Assert.That(owners, Is.EqualTo(new[] { Sentinel }));
            Assert.That(text,   Is.EqualTo("fix the crash"));
        });
    }

    // ── @m prefix (partial "@me") ─────────────────────────────────────────────

    [Test]
    public void AtMePrefix_ReturnsAtLeastSentinel_WhenNoRoster() {
        var (owners, text) = Parse("@m");

        Assert.Multiple(() => {
            Assert.That(owners, Does.Contain(Sentinel));
            Assert.That(text,   Is.Empty);
        });
    }

    // ── Exact handle match ────────────────────────────────────────────────────

    [Test]
    public void ExactHandleMatch_ViaFolderPath_ReturnsMemberName() {
        var roster = new[] { Member("Vesper Knox", @".squad\agents\vesper-knox") };
        var (owners, text) = Parse("@vesper-knox", roster);

        Assert.Multiple(() => {
            Assert.That(owners, Is.EqualTo(new[] { "Vesper Knox" }));
            Assert.That(text,   Is.Empty);
        });
    }

    [Test]
    public void ExactHandleMatch_ViaNameDerived_ReturnsMemberName() {
        var roster = new[] { Member("Vesper Knox") };
        var (owners, text) = Parse("@vesper-knox", roster);

        Assert.Multiple(() => {
            Assert.That(owners, Is.EqualTo(new[] { "Vesper Knox" }));
            Assert.That(text,   Is.Empty);
        });
    }

    [Test]
    public void ExactHandleMatch_IsCaseInsensitive() {
        var roster = new[] { Member("Vesper Knox") };
        var (owners, _) = Parse("@VESPER-KNOX", roster);

        Assert.That(owners, Is.EqualTo(new[] { "Vesper Knox" }));
    }

    [Test]
    public void ExactHandleMatch_WithTrailingText_PreservesText() {
        var roster = new[] { Member("Vesper Knox") };
        var (owners, text) = Parse("@vesper-knox audit coverage", roster);

        Assert.Multiple(() => {
            Assert.That(owners, Is.EqualTo(new[] { "Vesper Knox" }));
            Assert.That(text,   Is.EqualTo("audit coverage"));
        });
    }

    // ── Prefix match ─────────────────────────────────────────────────────────

    [Test]
    public void PrefixMatch_ReturnsAllMatchingMembers() {
        var roster = new[] {
            Member("Vesper Knox"),
            Member("Victor Hall"),
            Member("Lyra Morn"),
        };
        var (owners, _) = Parse("@v", roster);

        Assert.That(owners, Is.EquivalentTo(new[] { "Vesper Knox", "Victor Hall" }));
    }

    [Test]
    public void PrefixMatch_AlsoIncludesSentinel_WhenPrefixStartsWithM() {
        var roster = new[] { Member("Maria Chen") };
        var (owners, _) = Parse("@m", roster);

        Assert.That(owners, Does.Contain(Sentinel));
        Assert.That(owners, Does.Contain("Maria Chen"));
    }

    // ── Unresolved handle ─────────────────────────────────────────────────────

    [Test]
    public void UnresolvedHandle_NullRoster_TreatsWholeFilterAsText() {
        var (owners, text) = Parse("@nobody");

        Assert.Multiple(() => {
            Assert.That(owners, Is.Null);
            Assert.That(text,   Is.EqualTo("@nobody"));
        });
    }

    [Test]
    public void UnresolvedHandle_WithRoster_NoMatch_TreatsWholeFilterAsText() {
        var roster = new[] { Member("Vesper Knox") };
        var (owners, text) = Parse("@zara", roster);

        Assert.Multiple(() => {
            Assert.That(owners, Is.Null);
            Assert.That(text,   Is.EqualTo("@zara"));
        });
    }

    // ── Multiple spaces in trailing text ─────────────────────────────────────

    [Test]
    public void TrailingText_ExtraSpaces_AreCollapsed() {
        var (_, text) = Parse("@me  fix   crash");

        Assert.That(text, Is.EqualTo("fix crash"));
    }

    // ── FolderPath handle derivation ──────────────────────────────────────────

    [Test]
    public void FolderPath_UsesLastSegment_AsHandle() {
        var roster = new[] { Member("Orion Vale", @"C:\agents\orion-vale") };
        var (owners, _) = Parse("@orion-vale", roster);

        Assert.That(owners, Is.EqualTo(new[] { "Orion Vale" }));
    }

    [Test]
    public void NoFolderPath_DrivesHandleFromName_LowercaseHyphenated() {
        var roster = new[] { Member("Arjun Sen") };
        var (owners, _) = Parse("@arjun-sen", roster);

        Assert.That(owners, Is.EqualTo(new[] { "Arjun Sen" }));
    }
}
