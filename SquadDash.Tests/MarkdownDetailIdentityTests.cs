using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class MarkdownDetailIdentityTests {
    [Test]
    public void TaskDescription_KeyMatchesDocumentedUtf8LengthPrefixedFormat() {
        var identity = MarkdownDetailIdentity.ForTaskDescription(@"C:\repo", "plan-1", "task-1");
        Assert.That(identity.GetSectionKey("Why", "Line one\nLine two"), Is.EqualTo(
            "md-detail:v1:1faeefbd37a5002e42fb67a0912d038340c6d8006cf2f4299ebae964d7ea684b:" +
            "1ab8064e73e780516b97f5808273fb73a0334617e2fc338711237c6e1fb22c7d:0"));
    }

    [Test]
    [SetCulture("tr-TR")]
    public void EquivalentWorkspaceKindFieldAndLineEndings_ProduceSameKeyAcrossRebuilds() {
        var first = MarkdownDetailIdentity.Create(@" C:\repo\child\..\ ", " PLAN ", "plan-1", " DESCRIPTION ");
        var second = MarkdownDetailIdentity.Create(@"c:\REPO", "plan", "plan-1", "description");
        Assert.That(first.GetSectionKey("  Why\r\nnow  ", "a\r\nb\rc"),
            Is.EqualTo(second.GetSectionKey("Why\nnow", "a\nb\nc")));
    }

    [TestCase("workspace")]
    [TestCase("kind")]
    [TestCase("entity")]
    [TestCase("entity-case")]
    [TestCase("field")]
    [TestCase("label")]
    [TestCase("body")]
    [TestCase("body-whitespace")]
    [TestCase("label-case")]
    public void IndependentIdentityDimensions_DoNotShareState(string changedDimension) {
        var original = MarkdownDetailIdentity.Create(@"C:\repo", "plan", "plan-1", "description");
        var other = MarkdownDetailIdentity.Create(
            changedDimension == "workspace" ? @"C:\other" : @"C:\repo",
            changedDimension == "kind" ? "inbox-message" : "plan",
            changedDimension switch { "entity" => "plan-2", "entity-case" => "PLAN-1", _ => "plan-1" },
            changedDimension == "field" ? "notes" : "description");
        var label = changedDimension switch { "label" => "Other", "label-case" => "WHY", _ => "Why" };
        var body = changedDimension switch { "body" => "Other body", "body-whitespace" => "Body ", _ => "Body" };
        Assert.That(other.GetSectionKey(label, body), Is.Not.EqualTo(original.GetSectionKey("Why", "Body")));
    }

    [Test]
    public void CommonFactories_IsolateMessagesPlansAndPlanScopedTasks() {
        string Key(MarkdownDetailIdentity identity) => identity.GetSectionKey("Same", "Same body");
        string[] keys = [
            Key(MarkdownDetailIdentity.ForInboxMessage(@"C:\repo", "1")),
            Key(MarkdownDetailIdentity.ForInboxMessage(@"C:\repo", "2")),
            Key(MarkdownDetailIdentity.ForPlanDescription(@"C:\repo", "1")),
            Key(MarkdownDetailIdentity.ForTaskDescription(@"C:\repo", "1", "1")),
            Key(MarkdownDetailIdentity.ForTaskDescription(@"C:\repo", "1", "2")),
            Key(MarkdownDetailIdentity.ForTaskDescription(@"C:\repo", "2", "1"))
        ];
        Assert.That(keys.Distinct().Count(), Is.EqualTo(keys.Length));
        Assert.That(MarkdownDetailIdentity.ForInboxMessage(@"C:\repo", "1").GetSectionKey("Same", "Same body"),
            Is.EqualTo(MarkdownDetailIdentity.Create(@"C:\repo", "inbox-message", "1", "body")
                .GetSectionKey("Same", "Same body")));
    }

    [Test]
    public void LengthPrefixedComponents_DoNotAliasDelimiterOrUnicodeBoundaries() {
        var identity = MarkdownDetailIdentity.ForTaskDescription(@"C:\repo", "a:b", "c");
        var other = MarkdownDetailIdentity.ForTaskDescription(@"C:\repo", "a", "b:c");
        Assert.Multiple(() => {
            Assert.That(identity.GetSectionKey("a:b", "c"), Is.Not.EqualTo(identity.GetSectionKey("a", "b:c")));
            Assert.That(identity.GetSectionKey("a", "b"), Is.Not.EqualTo(other.GetSectionKey("a", "b")));
            Assert.That(identity.GetSectionKey("\u00e9", "x"), Is.Not.EqualTo(identity.GetSectionKey("e\u0301", "x")));
        });
    }

    [Test]
    public void UnrelatedInsertionReorderingAndEdits_LeaveUnchangedSectionsStable() {
        var identity = MarkdownDetailIdentity.ForPlanDescription(@"C:\repo", "plan-1");
        var a = new MarkdownDetailSection("A", "First body");
        var b = new MarkdownDetailSection("B", "Second body");
        var original = identity.GetSectionKeys([a, b]);
        var inserted = identity.GetSectionKeys([new("New", "Unrelated"), b, a]);
        var edited = identity.GetSectionKeys([a with { Body = "Edited" }, b]);
        Assert.Multiple(() => {
            Assert.That(inserted[2], Is.EqualTo(original[0]));
            Assert.That(inserted[1], Is.EqualTo(original[1]));
            Assert.That(edited[0], Is.Not.EqualTo(original[0]));
            Assert.That(edited[1], Is.EqualTo(original[1]));
        });
    }

    [Test]
    public void IdenticalDuplicates_AreDiscriminatedOnlyByTheirOwnOccurrenceOrder() {
        var identity = MarkdownDetailIdentity.ForTaskDescription(@"C:\repo", "plan", "task");
        var a = new MarkdownDetailSection(" A ", "a\r\nb");
        var equivalent = new MarkdownDetailSection("A", "a\nb");
        var original = identity.GetSectionKeys([a, equivalent, a]);
        var inserted = identity.GetSectionKeys([new("Unrelated", "Body"), a, new("A", "Different"), equivalent, a]);
        Assert.Multiple(() => {
            Assert.That(original.Distinct().Count(), Is.EqualTo(3));
            Assert.That(original, Is.EqualTo(new[] { inserted[1], inserted[3], inserted[4] }));
            Assert.That(original[0], Does.EndWith(":0"));
            Assert.That(original[1], Does.EndWith(":1"));
            Assert.That(original[2], Does.EndWith(":2"));
            Assert.That(identity.GetSectionKey("A", "a\nb", 1), Is.EqualTo(original[1]));
        });
    }

    [Test]
    public void InvalidArguments_AreRejectedInsteadOfSharingAnonymousIdentity() {
        Assert.Multiple(() => {
            Assert.That(() => MarkdownDetailIdentity.Create("", "plan", "1", "description"), Throws.ArgumentException);
            Assert.That(() => MarkdownDetailIdentity.Create(@"C:\repo", "", "1", "description"), Throws.ArgumentException);
            Assert.That(() => MarkdownDetailIdentity.Create(@"C:\repo", "plan", "", "description"), Throws.ArgumentException);
            Assert.That(() => MarkdownDetailIdentity.Create(@"C:\repo", "plan", "1", ""), Throws.ArgumentException);
            Assert.That(() => MarkdownDetailIdentity.ForTaskDescription(@"C:\repo", "", "1"), Throws.ArgumentException);
            Assert.That(() => MarkdownDetailIdentity.ForTaskDescription(@"C:\repo", "1", ""), Throws.ArgumentException);
            Assert.That(() => MarkdownDetailIdentity.ForPlanDescription(@"C:\repo", "1").GetSectionKey("A", "B", -1),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        });
    }
}
