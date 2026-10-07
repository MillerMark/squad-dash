using System.Collections.Specialized;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;

namespace SquadDash.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
internal sealed class PanelMenuItemSorterTests
{
    [Test]
    public void Sort_AlphabetizesPanelsAndPreservesInstancesStateHandlersAndTrailingItems()
    {
        var menu = new MenuItem();
        var panels = new[] { "Approvals", "Code Health", "Inbox", "Loop", "Notes", "Buttons", "Plans", "Tasks" }
            .Select(Panel).ToArray();
        foreach (var panel in panels)
            menu.Items.Add(panel);
        var separator = new Separator();
        var documentation = Panel("Repo Documentation");
        var history = Panel("Commit History");
        menu.Items.Add(separator);
        menu.Items.Add(documentation);
        menu.Items.Add(history);
        var buttons = panels[5];
        buttons.IsChecked = true;
        var clicked = false;
        buttons.Click += (_, _) => clicked = true;

        new PanelMenuItemSorter().Sort(menu.Items);
        buttons.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        Assert.Multiple(() =>
        {
            Assert.That(menu.Items.Cast<object>().Take(8),
                Is.EqualTo(new[] { panels[0], buttons, panels[1], panels[2], panels[3], panels[4], panels[6], panels[7] }));
            Assert.That(menu.Items[8], Is.SameAs(separator));
            Assert.That(menu.Items[9], Is.SameAs(documentation));
            Assert.That(menu.Items[10], Is.SameAs(history));
            Assert.That(menu.Items.Count, Is.EqualTo(11));
            Assert.That(buttons.IsChecked, Is.True);
            Assert.That(clicked, Is.True);
            Assert.That(buttons.Parent, Is.SameAs(menu));
        });
    }

    [Test]
    public void Sort_IsStableCaseInsensitiveAndNullSafe()
    {
        var menu = new MenuItem();
        var lower = Panel("buttons");
        var upper = Panel("BUTTONS");
        var approvals = Panel("approvals");
        var missingHeader = Panel(null);
        foreach (var panel in new[] { lower, upper, approvals, missingHeader })
            menu.Items.Add(panel);

        new PanelMenuItemSorter().Sort(menu.Items);

        Assert.That(menu.Items.Cast<object>(), Is.EqualTo(new[] { missingHeader, approvals, lower, upper }));
    }

    [Test]
    public void Sort_LeavesNonCheckableEntriesInTheirOriginalSlots()
    {
        var menu = new MenuItem();
        var notes = Panel("Notes");
        var command = new MenuItem { Header = "Command" };
        var buttons = Panel("Buttons");
        menu.Items.Add(notes);
        menu.Items.Add(command);
        menu.Items.Add(buttons);

        new PanelMenuItemSorter().Sort(menu.Items);

        Assert.That(menu.Items.Cast<object>(), Is.EqualTo(new[] { buttons, command, notes }));
    }

    [Test]
    public void Sort_AlreadySortedDoesNotChangeCollectionAndPicksUpFuturePanels()
    {
        var menu = new MenuItem();
        var approvals = Panel("Approvals");
        var notes = Panel("Notes");
        var separator = new Separator();
        menu.Items.Add(approvals);
        menu.Items.Add(notes);
        menu.Items.Add(separator);
        var sorter = new PanelMenuItemSorter();
        var changes = 0;
        ((INotifyCollectionChanged)menu.Items).CollectionChanged += (_, _) => changes++;

        sorter.Sort(menu.Items);
        Assert.That(changes, Is.Zero);

        var futurePanel = Panel("Calendar");
        menu.Items.Insert(2, futurePanel);
        sorter.Sort(menu.Items);
        Assert.That(menu.Items.Cast<object>(), Is.EqualTo(new object[] { approvals, futurePanel, notes, separator }));

        changes = 0;
        sorter.Sort(menu.Items);
        Assert.That(changes, Is.Zero);
    }

    [Test]
    public void Sort_ReentrantCollectionCallbacksDoNotInterruptSorting()
    {
        var menu = new MenuItem();
        var tasks = Panel("Tasks");
        var notes = Panel("Notes");
        var buttons = Panel("Buttons");
        menu.Items.Add(tasks);
        menu.Items.Add(notes);
        menu.Items.Add(buttons);
        var sorter = new PanelMenuItemSorter();
        ((INotifyCollectionChanged)menu.Items).CollectionChanged += (_, _) => sorter.Sort(menu.Items);

        sorter.Sort(menu.Items);

        Assert.That(menu.Items.Cast<object>(), Is.EqualTo(new[] { buttons, notes, tasks }));
    }

    [Test]
    public void Sort_NullEmptySinglePanelAndLeadingSeparatorAreNoOps()
    {
        var sorter = new PanelMenuItemSorter();
        Assert.DoesNotThrow(() => sorter.Sort(null));
        var menu = new MenuItem();
        sorter.Sort(menu.Items);
        Assert.That(menu.Items, Is.Empty);
        var notes = Panel("Notes");
        menu.Items.Add(notes);
        sorter.Sort(menu.Items);
        Assert.That(menu.Items[0], Is.SameAs(notes));
        var separator = new Separator();
        var buttons = Panel("Buttons");
        menu.Items.Insert(0, separator);
        menu.Items.Add(buttons);

        sorter.Sort(menu.Items);

        Assert.That(menu.Items.Cast<object>(), Is.EqualTo(new object[] { separator, notes, buttons }));
    }

    private static MenuItem Panel(string? header) => new() { Header = header, IsCheckable = true };
}
