using Cabinet.Core;

namespace Cabinet.Gui;

internal sealed class RunnersPage
{
    private readonly Layout layout;
    private readonly IProcessRunner runner;
    private readonly Gtk.Window window;
    private readonly Gtk.Box list = Gtk.Box.New(Gtk.Orientation.Vertical, 12);

    public RunnersPage(Layout layout, IProcessRunner runner, Gtk.Window window)
    {
        this.layout = layout;
        this.runner = runner;
        this.window = window;

        var page = Ui.Page();
        page.Append(Ui.Scrolled(list));
        Widget = page;
    }

    public Gtk.Widget Widget { get; }

    public void Refresh()
    {
        Ui.Clear(list);

        var runners = new Runners(layout, runner);
        var group = Adw.PreferencesGroup.New();
        group.SetTitle("Installed");

        var add = Ui.RowButton(Icons.Archive, "Unpack a Wine build you already have");
        add.OnClicked += (_, _) => ChooseArchive();
        group.SetHeaderSuffix(add);

        foreach (var installed in runners.List())
        {
            var row = Adw.ActionRow.New();
            row.SetTitle(installed.Name);
            row.SetSubtitle(Describe(runners, installed));
            row.AddPrefix(Gtk.Image.NewFromIconName(Icons.Runners));

            if (!installed.Bundled)
            {
                var remove = Ui.IconButton(Icons.Delete, "Delete this runner");
                remove.SetValign(Gtk.Align.Center);
                remove.OnClicked += (_, _) => Remove(installed.Name);
                row.AddSuffix(remove);
            }

            group.Add(row);
        }

        list.Append(group);
    }

    private static string Describe(Runners runners, Runner installed)
    {
        var used = runners.InUseBy(installed.Bundled ? Layout.BundledRunner : installed.Name);
        var by = used.Count == 0 ? "unused" : "used by " + string.Join(", ", used);

        return installed.Usable ? $"{by}  ·  {(installed.Multilib ? "32+64" : "64-bit only")}" : "broken";
    }

    public void ShowAvailable()
    {
        var dialog = Adw.Dialog.New();
        dialog.SetTitle("Wine versions");
        dialog.SetContentWidth(560);
        dialog.SetContentHeight(520);

        var body = Ui.Page();
        var group = Adw.PreferencesGroup.New();
        group.SetTitle("Available upstream");
        group.SetDescription("Reading Bottles' component index…");
        body.Append(Ui.Scrolled(group));

        var view = Adw.ToolbarView.New();
        view.AddTopBar(Adw.HeaderBar.New());
        view.SetContent(body);
        dialog.SetChild(view);
        dialog.Present(window);

        Task.Run(() =>
        {
            try
            {
                var families = new RunnerIndex(runner).Available()
                    .GroupBy(release => release.Family);
                Ui.OnMainLoop(() =>
                {
                    group.SetDescription(null);

                    foreach (var family in families)
                    {
                        group.Add(FamilyRow(family, dialog));
                    }
                });
            }
            catch (Exception exception)
            {
                Ui.OnMainLoop(() => group.SetDescription(exception.Message));
            }
        });
    }

    private Adw.ExpanderRow FamilyRow(
        IGrouping<RunnerFamily, RunnerRelease> family, Adw.Dialog dialog)
    {
        var expander = Adw.ExpanderRow.New();
        expander.SetTitle(family.Key.Label);
        expander.SetSubtitle(family.Key.Description);
        expander.AddPrefix(Gtk.Image.NewFromIconName(Icons.Runners));

        foreach (var release in family)
        {
            expander.AddRow(AvailableRow(release, dialog));
        }

        return expander;
    }

    private Adw.ActionRow AvailableRow(RunnerRelease release, Adw.Dialog dialog)
    {
        var row = Adw.ActionRow.New();
        row.SetTitle(release.Version);
        row.SetSubtitle(release.Name);

        var install = Ui.IconButton(Icons.Download, $"Install {release.Name}");
        install.SetValign(Gtk.Align.Center);
        install.OnClicked += (_, _) =>
        {
            dialog.ForceClose();
            Operation.Run(
                window,
                $"Installing {release.Name}",
                output => new Runners(layout, runner).Install(release, output),
                Refresh);
        };

        row.AddSuffix(install);
        return row;
    }

    private void ChooseArchive() =>
        Ui.ChooseFile(window, "Choose a Wine archive", path =>
            Operation.Run(
                window,
                $"Unpacking {Path.GetFileName(path)}",
                output => new Runners(layout, runner).Add(path, onOutput: output),
                Refresh));

    private void Remove(string name) =>
        Operation.Run(
            window,
            $"Deleting {name}",
            _ => new Runners(layout, runner).Remove(name),
            Refresh);
}
