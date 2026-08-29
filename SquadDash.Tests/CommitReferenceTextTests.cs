using System.Threading;
using System.Windows;
using System.Windows.Documents;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class CommitReferenceTextTests
{
    private const string Head = "e996a9105c6e5b6c8a56530a6376c434a2f0e215";
    private const string Baseline = "cc5fb70b68c45813d2ab9e846d24f72330f2a1d8";

    [Test]
    public void Parse_FullHashes_PreservesTargetsAndUsesSevenCharacterText()
    {
        var segments = CommitReferenceText.Parse($"HEAD {Head} differs from baseline {Baseline}.");
        var commits = segments.Where(segment => segment.FullSha is not null).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(commits.Select(segment => segment.Text), Is.EqualTo(new[] { "e996a91", "cc5fb70" }));
            Assert.That(commits.Select(segment => segment.FullSha), Is.EqualTo(new[] { Head, Baseline }));
        });
    }

    [Test]
    public void Shorten_LeavesNonCommitHexAndOrdinaryTextAlone()
    {
        Assert.That(
            CommitReferenceText.Shorten($"attempt 021dfd668cca43a6bdf28b89342c8248 at {Head}"),
            Is.EqualTo("attempt 021dfd668cca43a6bdf28b89342c8248 at e996a91"));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void InboxCommitLinks_EmbeddedHashesBecomeShortClickableLinks()
    {
        _ = Application.Current ?? new Application();
        var opened = new List<string>();
        var document = MarkdownFlowDocumentBuilder.BuildInbox(
            $"Why it stopped: **HEAD {Head} differs from baseline {Baseline}.**");

        InboxCommitLinkPresenter.Attach(document, opened.Add);

        var links = EnumerateInlines(document).OfType<Hyperlink>().ToArray();
        Assert.That(
            links.Select(link => new TextRange(link.ContentStart, link.ContentEnd).Text),
            Is.EqualTo(new[] { "e996a91", "cc5fb70" }));

        links[0].RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent, links[0]));
        Assert.That(opened, Does.Contain(Head));
    }

    private static IEnumerable<Inline> EnumerateInlines(FlowDocument document)
    {
        foreach (var paragraph in document.Blocks.OfType<Paragraph>())
            foreach (var inline in Enumerate(paragraph.Inlines))
                yield return inline;
    }

    private static IEnumerable<Inline> Enumerate(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            yield return inline;
            if (inline is Span span)
                foreach (var child in Enumerate(span.Inlines))
                    yield return child;
        }
    }
}
