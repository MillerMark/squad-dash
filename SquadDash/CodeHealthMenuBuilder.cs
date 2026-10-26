using System;
using System.Linq;
using System.Windows.Controls;

namespace SquadDash;

internal static class CodeHealthMenuBuilder
{
    internal static void Populate(MenuItem menu, CodeHealthMdConfig? config, Action<string> runTask)
    {
        menu.Items.Clear();

        if (config?.Tasks is not { Count: > 0 } tasks)
        {
            menu.Items.Add(new MenuItem
            {
                Header = "No code health tasks configured.",
                IsEnabled = false,
            });
            return;
        }

        foreach (var task in tasks.OrderBy(task => task.Title, StringComparer.OrdinalIgnoreCase))
        {
            var item = new MenuItem
            {
                Header = new TextBlock { Text = task.Title },
            };
            item.Click += (_, _) => runTask(task.Id);
            menu.Items.Add(item);
        }
    }
}
