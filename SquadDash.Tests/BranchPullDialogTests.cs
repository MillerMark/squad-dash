using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SquadDash.Tests;

[TestFixture]
internal sealed class BranchPullDialogTests
{
    [TestCase("Merge (recommended)", true, false)]
    [TestCase("Rebase", true, true)]
    [TestCase("Cancel", false, false)]
    [TestCase("Close", false, false)]
    public void Choice_PreservesExplicitStrategyAndSafeDefaults(string choice, bool accepted, bool useRebase)
    {
        WpfTestContext.Run(() =>
        {
            var owner = new Window { Width = 1, Height = 1, ShowInTaskbar = false };
            owner.Show();
            var dialog = new BranchPullDialog("master", new GitUpstreamCounts(7, 3), owner);
            try
            {
                var content = Descendants(dialog);
                var text = string.Join("\n", content.OfType<TextBlock>().Select(block => block.Text));
                var buttons = content.OfType<Button>().ToArray();
                var merge = buttons.Single(button => ButtonLabel(button) == "Merge (recommended)");
                var rebase = buttons.Single(button => ButtonLabel(button) == "Rebase");
                var cancel = buttons.Single(button => ButtonLabel(button) == "Cancel");

                Assert.Multiple(() =>
                {
                    Assert.That(text, Does.Contain("local 'master' has 3 commit(s)")
                        .And.Contain("remote has 7 commit(s)")
                        .And.Contain("fast-forward isn't possible")
                        .And.Contain("no force-push is needed")
                        .And.Contain("rewrites those local commits"));
                    Assert.That(merge.IsDefault, Is.True);
                    Assert.That(rebase.IsDefault, Is.False);
                    Assert.That(cancel.IsCancel, Is.True);
                });

                var mergeHadFocus = false;
                dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    mergeHadFocus = merge.IsKeyboardFocused;
                    if (choice == "Close")
                        dialog.Close();
                    else
                        buttons.Single(button => ButtonLabel(button) == choice)
                            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }));

                var result = dialog.ShowDialog();
                Assert.Multiple(() =>
                {
                    Assert.That(mergeHadFocus, Is.True, "Merge must receive initial keyboard focus.");
                    Assert.That(result, Is.EqualTo(accepted));
                    Assert.That(dialog.UseRebase, Is.EqualTo(useRebase));
                });
            }
            finally
            {
                dialog.Close();
                owner.Close();
            }
        });
    }

    private static string? ButtonLabel(Button button) =>
        button.Content as string ?? Descendants(button.Content).OfType<TextBlock>().FirstOrDefault()?.Text;

    private static IEnumerable<DependencyObject> Descendants(object root)
    {
        if (root is not DependencyObject dependencyObject) yield break;
        yield return dependencyObject;
        foreach (var child in LogicalTreeHelper.GetChildren(dependencyObject))
        {
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}
