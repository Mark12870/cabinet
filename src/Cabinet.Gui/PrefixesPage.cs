using Cabinet.Core;

namespace Cabinet.Gui;

internal sealed class PrefixesPage
{
    private readonly Layout layout;
    private readonly IProcessRunner runner;
    private readonly Gtk.Window window;
    private readonly Action changed;
    private readonly Gtk.Box list = Gtk.Box.New(Gtk.Orientation.Vertical, 12);

    public PrefixesPage(Layout layout, IProcessRunner runner, Gtk.Window window, Action changed)
    {
        this.layout = layout;
        this.runner = runner;
        this.window = window;
        this.changed = changed;

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
            list.Append(Empty());
            return;
        }

        var names = RunnerNames();
        var group = Adw.PreferencesGroup.New();
        group.SetTitle("Prefixes");

        var create = Ui.RowButton(Icons.New, "New prefix");
        create.OnClicked += (_, _) => NewPrefix();
        group.SetHeaderSuffix(create);

        foreach (var prefix in prefixes)
        {
            group.Add(Row(prefix, names));
        }

        list.Append(group);
    }

    private Adw.StatusPage Empty()
    {
        var empty = Adw.StatusPage.New();
        empty.SetIconName(Icons.Prefixes);
        empty.SetTitle("No prefixes yet");
        empty.SetDescription("Every plugin gets a Wine prefix of its own.");

        var create = Gtk.Button.NewWithLabel("New prefix");
        create.SetHalign(Gtk.Align.Center);
        create.AddCssClass("suggested-action");
        create.AddCssClass("pill");
        create.OnClicked += (_, _) => NewPrefix();
        empty.SetChild(create);

        return empty;
    }

    private List<string> RunnerNames() =>
        [.. new Runners(layout, runner).List().Select(found => found.Name)];

    private Adw.ExpanderRow Row(Prefix prefix, IReadOnlyList<string> runnerNames)
    {
        var row = Adw.ExpanderRow.New();
        row.SetTitle(prefix.Name);
        row.SetSubtitle(Subtitle(prefix));
        row.AddPrefix(Gtk.Image.NewFromIconName(Icons.Prefixes));

        row.AddRow(RunnerRow(prefix, runnerNames));
        row.AddRow(SyncRow(prefix));
        row.AddRow(DxvkRow(prefix));
        row.AddRow(Action(
            "Environment variables", Icons.Variables, () => EditVariables(prefix.Name)));
        row.AddRow(Action(
            "Windows installer", Icons.Install, () => ChooseInstaller(prefix.Name)));
        row.AddRow(Action(
            "Wine configuration", Icons.Configure, () => Run(prefix.Name, "winecfg", [])));
        row.AddRow(Action("Run a command", Icons.Command, () => AskForCommand(prefix.Name)));
        row.AddRow(Action(
            "Delete", Icons.Delete, () => ConfirmDelete(prefix), destructive: true));

        return row;
    }

    private Adw.ComboRow RunnerRow(Prefix prefix, IReadOnlyList<string> runnerNames)
    {
        List<string> choices = [.. runnerNames];

        if (!choices.Contains(prefix.Runner))
        {
            choices.Add(prefix.Runner);
        }

        var row = Adw.ComboRow.New();
        row.SetTitle("Wine");
        row.SetModel(Gtk.StringList.New([.. choices]));
        row.SetSelected((uint)choices.IndexOf(prefix.Runner));

        row.OnNotify += (_, args) =>
        {
            if (args.Pspec.GetName() != "selected")
            {
                return;
            }

            var chosen = choices[(int)row.GetSelected()];

            if (chosen != prefix.Runner)
            {
                UseRunner(prefix.Name, chosen);
            }
        };

        return row;
    }

    private Adw.ComboRow SyncRow(Prefix prefix)
    {
        var choices = PrefixSettings.SyncModes;

        var row = Adw.ComboRow.New();
        row.SetTitle("Sync");
        row.SetModel(Gtk.StringList.New([.. choices.Select(Label)]));
        row.SetSelected((uint)choices.ToList().IndexOf(prefix.Sync));

        row.OnNotify += (_, args) =>
        {
            if (args.Pspec.GetName() != "selected")
            {
                return;
            }

            var chosen = choices[(int)row.GetSelected()];

            if (chosen != prefix.Sync)
            {
                UseSync(prefix.Name, chosen);
            }
        };

        return row;
    }

    private Adw.SwitchRow DxvkRow(Prefix prefix)
    {
        var installed = prefix.Dxvk is not null;

        var row = Adw.SwitchRow.New();
        row.SetTitle("DXVK");
        row.SetActive(installed);

        row.OnNotify += (_, args) =>
        {
            if (args.Pspec.GetName() != "active" || row.GetActive() == installed)
            {
                return;
            }

            if (row.GetActive())
            {
                InstallDxvk(prefix.Name);
            }
            else
            {
                RemoveDxvk(prefix.Name);
            }
        };

        return row;
    }

    private static string Label(SyncMode mode) =>
        mode == SyncMode.System ? "System" : PrefixSettings.Word(mode);

    private static Adw.ActionRow Action(
        string title, string iconName, Action clicked, bool destructive = false)
    {
        var row = Adw.ActionRow.New();
        row.SetTitle(title);

        var button = Ui.RowButton(iconName, title, destructive);
        button.OnClicked += (_, _) => clicked();
        row.AddSuffix(button);
        row.SetActivatableWidget(button);

        return row;
    }

    private static string Subtitle(Prefix prefix)
    {
        var state = prefix.Initialised ? prefix.Runner : "not initialised";

        if (prefix.Sync != SyncMode.System)
        {
            state += $"  ·  {PrefixSettings.Word(prefix.Sync)}";
        }

        return prefix.Dxvk is null ? state : $"{state}  ·  DXVK {prefix.Dxvk}";
    }

    private void NewPrefix()
    {
        var dialog = Adw.AlertDialog.New(
            "New prefix", "A name for the prefix, such as the plugin it will hold.");

        var name = Adw.EntryRow.New();
        name.SetTitle("Name");

        var choices = RunnerNames();
        var wine = Adw.ComboRow.New();
        wine.SetTitle("Wine");
        wine.SetSubtitle("The runner it will keep");
        wine.SetModel(Gtk.StringList.New([.. choices]));

        var fields = Adw.PreferencesGroup.New();
        fields.SetMarginTop(12);
        fields.Add(name);
        fields.Add(wine);
        dialog.SetExtraChild(fields);

        dialog.AddResponse("cancel", "Cancel");
        dialog.AddResponse("ok", "Create");
        dialog.SetResponseAppearance("ok", Adw.ResponseAppearance.Suggested);
        dialog.SetDefaultResponse("ok");
        dialog.SetCloseResponse("cancel");

        dialog.OnResponse += (_, args) =>
        {
            var entered = name.GetText().Trim();

            if (args.Response == "ok" && entered.Length > 0)
            {
                CreatePrefix(entered, choices[(int)wine.GetSelected()]);
            }
        };

        dialog.Present(window);
    }

    private void CreatePrefix(string name, string? runnerName) =>
        Operation.Run(
            window,
            $"Creating {name}",
            output => new Prefixes(layout, runner).Create(name, runnerName, output),
            changed);

    private void UseRunner(string name, string runnerName) =>
        Operation.Run(
            window,
            $"Moving {name} to {runnerName}",
            output =>
            {
                var prefixes = new Prefixes(layout, runner);
                prefixes.SetRunner(name, runnerName);
                Operation.Ensure(prefixes.Run(name, "wineboot", ["-u"], output), "wineboot");
            },
            changed);

    private void UseSync(string name, SyncMode mode) =>
        Operation.Run(
            window,
            $"Putting {name} on {Label(mode)}",
            _ => new PrefixSettings(layout).SetSync(name, mode),
            changed);

    private void InstallDxvk(string name) =>
        Operation.Run(
            window,
            $"Installing DXVK into {name}",
            output => new Dxvk(layout, runner).Install(name, output),
            changed);

    private void RemoveDxvk(string name) =>
        Operation.Run(
            window,
            $"Taking DXVK out of {name}",
            output => new Dxvk(layout, runner).Remove(name, output),
            changed);

    private void EditVariables(string name) =>
        new VariablesDialog(window, layout, name, changed).Present();

    private void Run(string name, string command, IReadOnlyList<string> arguments) =>
        Operation.Run(
            window,
            $"{command} in {name}",
            output => Operation.Ensure(
                new Prefixes(layout, runner).Run(name, command, arguments, output), command),
            changed);

    private void AskForCommand(string name) =>
        Ui.Prompt(
            window,
            $"Run a command in {name}",
            "It runs against this prefix's own Wine, the way `cabinet run` does.",
            "regedit",
            entered =>
            {
                var parts = entered.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                Run(name, parts[0], parts[1..]);
            });

    private void ChooseInstaller(string name) =>
        Ui.ChooseFile(window, "Choose a Windows installer", path =>
            Operation.Run(
                window,
                $"Installing into {name}",
                output => Operation.Ensure(
                    new Prefixes(layout, runner).Install(name, path, output),
                    Path.GetFileName(path)),
                changed));

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
                    changed);
            }
        };

        dialog.Present(window);
    }
}
