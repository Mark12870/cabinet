using Cabinet.Core;

namespace Cabinet.Gui;

internal sealed class PluginsDialog(
    Gtk.Window window,
    Layout layout,
    IProcessRunner runner,
    LibraryEntry manager,
    Operation operations,
    Action changed)
{
    private readonly Adw.Dialog dialog = Adw.Dialog.New();
    private readonly Gtk.Box list = Gtk.Box.New(Gtk.Orientation.Vertical, 12);

    public void Present()
    {
        dialog.SetTitle($"Plugins installed through {manager.Name}");
        dialog.SetContentWidth(560);
        dialog.SetContentHeight(460);

        var body = Ui.Page();
        body.Append(Ui.Scrolled(list));
        dialog.SetChild(Ui.Bars(body));

        Fill();
        dialog.Present(window);
    }

    private void Fill()
    {
        Ui.Clear(list);

        var group = Adw.PreferencesGroup.New();
        group.SetDescription("Looking…");
        list.Append(group);

        Task.Run(() => new Library(layout, runner).PluginsOf(manager)).ContinueWith(found => Ui.OnMainLoop(() =>
        {
            if (found.IsFaulted)
            {
                group.SetDescription(found.Exception!.GetBaseException().Message);
                return;
            }

            group.SetDescription(found.Result.Count == 0 ? $"{manager.Name} has installed no plugins yet." : null);

            foreach (var plugin in found.Result)
            {
                group.Add(Row(plugin));
            }
        }));
    }

    private Adw.ActionRow Row(ManagerPlugin plugin)
    {
        var row = Adw.ActionRow.New();
        row.SetUseMarkup(false);
        row.SetTitle(plugin.Name);
        row.SetSubtitle(string.Join(", ", plugin.Formats));

        var remove = Ui.RowButton(Icons.Delete, $"Remove {plugin.Name}");
        remove.OnClicked += (_, _) => Ui.Guard(() => ConfirmRemove(plugin));
        row.AddSuffix(remove);

        return row;
    }

    private void ConfirmRemove(ManagerPlugin plugin) =>
        Ui.Confirm(
            window,
            $"Remove {plugin.Name}?",
            $"{plugin.Name} leaves your DAW and its files leave {manager.Name}'s prefix. "
            + $"{manager.Name} can install it again.",
            "Remove",
            () => operations.Run(
                $"Removing {plugin.Name}",
                output => new Library(layout, runner).RemovePlugin(manager, plugin.Name, output),
                () =>
                {
                    Fill();
                    changed();
                }),
            Adw.ResponseAppearance.Destructive);
}
