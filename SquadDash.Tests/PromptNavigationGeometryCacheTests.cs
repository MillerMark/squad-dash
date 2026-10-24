using System.Threading;
using System.Windows.Documents;

namespace SquadDash.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
internal sealed class PromptNavigationGeometryCacheTests
{
    [Test]
    public void CachedAnchors_AreReusedAcrossScrollAndViewportHeightChanges()
    {
        var first = Entry();
        var second = Entry();
        var prompts = new[] { first, second };
        var cache = new PromptNavigationGeometryCache();
        var measurements = 0;

        double Measure(Paragraph paragraph)
        {
            measurements++;
            return ReferenceEquals(paragraph, first.Paragraph) ? 100 : 900;
        }

        var initial = cache.GetAbsoluteYs(prompts, layoutWidth: 800, fontSize: 16, Measure);
        var afterScroll = cache.GetAbsoluteYs(prompts, layoutWidth: 800, fontSize: 16, measure: null);

        Assert.Multiple(() =>
        {
            Assert.That(initial, Is.EqualTo(new[] { 100d, 900d }));
            Assert.That(afterScroll, Is.EqualTo(initial));
            Assert.That(measurements, Is.EqualTo(2));
        });
    }

    [Test]
    public void WidthFontOrPromptOrderChange_InvalidatesAnchors()
    {
        var first = Entry();
        var second = Entry();
        var prompts = new[] { first, second };
        var cache = new PromptNavigationGeometryCache();
        var measurements = 0;
        double Measure(Paragraph _) => ++measurements * 10;

        cache.GetAbsoluteYs(prompts, 800, 16, Measure);
        cache.GetAbsoluteYs(prompts, 700, 16, Measure);
        cache.GetAbsoluteYs(prompts, 700, 18, Measure);
        cache.GetAbsoluteYs(new[] { second, first }, 700, 18, Measure);

        Assert.That(measurements, Is.EqualTo(8));
    }

    [Test]
    public void DeferredRefresh_ReturnsUnknownAnchorsWithoutForcingMeasurement()
    {
        var cache = new PromptNavigationGeometryCache();
        var values = cache.GetAbsoluteYs(new[] { Entry() }, 800, 16, measure: null);

        Assert.That(values, Has.Length.EqualTo(1));
        Assert.That(values[0], Is.NaN);
    }

    private static PromptEntry Entry() => new(new Paragraph(), DateTimeOffset.UtcNow);
}
