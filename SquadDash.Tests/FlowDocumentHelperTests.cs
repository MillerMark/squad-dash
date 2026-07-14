using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace SquadDash.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class FlowDocumentHelperTests {
    [Test]
    public void EnsureLayoutIsCurrent_ReflowsDocumentAfterPageWidthChanges() {
        var heading = new Paragraph(new Run("Congratulations!")) {
            FontFamily = new System.Windows.Media.FontFamily("Calibri"),
            FontSize = 30,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0),
        };
        var document = new FlowDocument(heading) {
            PagePadding = new Thickness(0),
            PageWidth = 640,
        };
        var viewer = new FlowDocumentScrollViewer {
            Document = document,
            Width = 640,
            Height = 300,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };

        viewer.Measure(new Size(640, 300));
        viewer.Arrange(new Rect(0, 0, 640, 300));
        viewer.UpdateLayout();
        double singleLineHeight = FlowDocumentHelper.GetLowestBlock(document);

        document.PageWidth = 100;
        FlowDocumentHelper.EnsureLayoutIsCurrent(document);
        double wrappedHeight = FlowDocumentHelper.GetLowestBlock(document);

        Assert.That(wrappedHeight, Is.GreaterThan(singleLineHeight));
    }
}
