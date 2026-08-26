using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class WorkspaceUpgradeBindingTests
{
    [Test]
    [Apartment(ApartmentState.STA)]
    public void FilenameRun_ExplicitOneWayCurrentItemBinding_CreatesAtRuntime()
    {
        var template = (DataTemplate)XamlReader.Parse(
            """
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <TextBlock>
                    <Hyperlink>
                        <Run Text="{Binding Path=., Mode=OneWay}" />
                    </Hyperlink>
                </TextBlock>
            </DataTemplate>
            """);
        var presenter = new ContentPresenter
        {
            Content = @".github\agents\squad.agent.md",
            ContentTemplate = template
        };

        Assert.DoesNotThrow(() =>
        {
            presenter.ApplyTemplate();
            presenter.Measure(new Size(800, 600));
        });
    }
}
