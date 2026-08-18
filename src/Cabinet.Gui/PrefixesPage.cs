using Cabinet.Core;

namespace Cabinet.Gui;

internal sealed class PrefixesPage
{
    private readonly Layout layout;
    private readonly IProcessRunner runner;
    private readonly Gtk.Window window;
    private readonly Gtk.Box list = Gtk.Box.New(Gtk.Orientation.Vertical, 12);

    public PrefixesPage(Layout layout, IProcessRunner runner, Gtk.Window window)
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

        var prefixes = new Prefixes(layout, runner).List();

        if (prefixes.Count == 0)
        {
            var empty = Adw.StatusPage.New();
            empty.SetIconName(Icons.Prefixes);
            empty.SetTitle("No prefixes yet");
            empty.SetDescription("Every plugin gets a Wine prefix of its own.");
            list.Append(empty);
            return;
        }

        var group = Adw.PreferencesGroup.New();
        group.SetTitle("Prefixes");

        foreach (var prefix in prefixes)
        {
            group.Add(Row(prefix));
        }

        list.Append(group);
    }

    private Adw.ActionRow Row(Prefix prefix)
    {
        var row = Adw.ActionRow.New();
        row.SetTitle(prefix.Name);
        row.SetSubtitle(Subtitle(prefix));

        var icon = Gtk.Image.NewFromIconName(Icons.Prefixes);
        row.AddPrefix(icon);

        var dxvk = Ui.IconButton(Icons.Dxvk, "Install DXVK");
        dxvk.OnClicked += (_, _) => InstallDxvk(prefix.Name);
        dxvk.SetValign(Gtk.Align.Center);
        row.AddSuffix(dxvk);

        var install = Ui.IconButton(Icons.Install, "Run a Windows installer");
        install.OnClicked += (_, _) => ChooseInstaller(prefix.Name);
        install.SetValign(Gtk.Align.Center);
        row.AddSuffix(install);

        var configure = Ui.IconButton(Icons.Configure, "winecfg");
        configure.OnClicked += (_, _) => Run(prefix.Name, "winecfg");
        configure.SetValign(Gtk.Align.Center);
        row.AddSuffix(configure);

        var delete = Ui.IconButton(Icons.Delete, "Delete this prefix");
        delete.OnClicked += (_, _) => ConfirmDelete(prefix);
        delete.SetValign(Gtk.Align.Center);
        row.AddSuffix(delete);

        return row;
    }

    private static string Subtitle(Prefix prefix)
    {
        var state = prefix.Initialised ? prefix.Runner : "not initialised";
        return prefix.Dxvk is null ? state : $"{state}  ·  DXVK {prefix.Dxvk}";
    }

    public void CreatePrefix(string name, string? runnerName)
    {
        Operation.Run(
            window,
            $"Creating {name}",
            output => new Prefixes(layout, runner).Create(name, runnerName, output),
            Refresh);
    }

    private void InstallDxvk(string name) =>
        Operation.Run(
            window,
            $"Installing DXVK into {name}",
            _ => new Dxvk(layout, runner).Install(name),
            Refresh);

    private void Run(string name, string command) =>
        Operation.Run(
            window,
            $"{command} in {name}",
            output => new Prefixes(layout, runner).Run(name, command, [], output));

    private void ChooseInstaller(string name)
    {
        var chooser = Gtk.FileDialog.New();
        chooser.SetTitle("Choose a Windows installer");

        chooser.OpenAsync(window).ContinueWith(task =>
        {
            if (task.IsFaulted || task.Result?.GetPath() is not { Length: > 0 } path)
            {
                return;
            }

            Ui.OnMainLoop(() => Operation.Run(
                window,
                $"Installing into {name}",
                output => new Prefixes(layout, runner).Install(name, path, output),
                Refresh));
        });
    }

    private void ConfirmDelete(Prefix prefix)
    {
        var dialog = Adw.AlertDialog.New(
            $"Delete “{prefix.Name}”?",
            "The prefix and every plugin installed in it will be removed.");

        dialog.AddResponse("cancel", "Cancel");
        dialog.AddResponse("delete", "Delete");
        dialog.SetResponseAppearance("delete", Adw.ResponseAppearance.Destructive);
        dialog.SetDefaultResponse("cancel");
        dialog.SetCloseResponse("cancel");

        dialog.OnResponse += (_, args) =>
        {
            if (args.Response == "delete")
            {
                Operation.Run(
                    window,
                    $"Deleting {prefix.Name}",
                    _ => new Prefixes(layout, runner).Delete(prefix.Name),
                    Refresh);
            }
        };

        dialog.Present(window);
    }
}
