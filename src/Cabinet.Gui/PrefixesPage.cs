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

        var names = new Runners(layout, runner).List().Select(found => found.Name).ToList();
        var group = Adw.PreferencesGroup.New();
        group.SetTitle("Prefixes");

        foreach (var prefix in prefixes)
        {
            group.Add(Row(prefix, names));
        }

        list.Append(group);
    }

    private Adw.ExpanderRow Row(Prefix prefix, IReadOnlyList<string> runnerNames)
    {
        var row = Adw.ExpanderRow.New();
        row.SetTitle(prefix.Name);
        row.SetSubtitle(Subtitle(prefix));
        row.AddPrefix(Gtk.Image.NewFromIconName(Icons.Prefixes));

        row.AddRow(RunnerRow(prefix, runnerNames));
        row.AddRow(Action(
            "Windows installer", "Run an installer inside this prefix",
            Icons.Install, () => ChooseInstaller(prefix.Name)));
        row.AddRow(Action(
            "DXVK", prefix.Dxvk is null ? "JUCE editors need this" : $"installed, {prefix.Dxvk}",
            Icons.Dxvk, () => InstallDxvk(prefix.Name)));
        row.AddRow(Action(
            "Wine configuration", "winecfg", Icons.Configure,
            () => Run(prefix.Name, "winecfg", [])));
        row.AddRow(Action(
            "Run a command", "regedit, or wine reg add …", Icons.Command,
            () => AskForCommand(prefix.Name)));
        row.AddRow(Action(
            "Delete", "The prefix and every plugin in it", Icons.Delete,
            () => ConfirmDelete(prefix), destructive: true));

        return row;
    }

    private Adw.ComboRow RunnerRow(Prefix prefix, IReadOnlyList<string> runnerNames)
    {
        var row = Adw.ComboRow.New();
        row.SetTitle("Wine");
        row.SetSubtitle("The runner this prefix starts on");
        row.SetModel(Gtk.StringList.New([.. runnerNames]));

        var current = runnerNames.ToList().IndexOf(prefix.Runner);

        if (current >= 0)
        {
            row.SetSelected((uint)current);
        }

        row.OnNotify += (_, args) =>
        {
            if (args.Pspec.GetName() != "selected")
            {
                return;
            }

            var chosen = runnerNames[(int)row.GetSelected()];

            if (chosen != prefix.Runner)
            {
                UseRunner(prefix.Name, chosen);
            }
        };

        return row;
    }

    private static Adw.ActionRow Action(
        string title, string subtitle, string iconName, Action clicked,
        bool destructive = false)
    {
        var row = Adw.ActionRow.New();
        row.SetTitle(title);
        row.SetSubtitle(subtitle);

        var button = Ui.RowButton(iconName, title, destructive);
        button.OnClicked += (_, _) => clicked();
        row.AddSuffix(button);
        row.SetActivatableWidget(button);

        return row;
    }

    private static string Subtitle(Prefix prefix)
    {
        var state = prefix.Initialised ? prefix.Runner : "not initialised";
        return prefix.Dxvk is null ? state : $"{state}  ·  DXVK {prefix.Dxvk}";
    }

    public void CreatePrefix(string name, string? runnerName) =>
        Operation.Run(
            window,
            $"Creating {name}",
            output => new Prefixes(layout, runner).Create(name, runnerName, output),
            Refresh);

    private void UseRunner(string name, string runnerName) =>
        Operation.Run(
            window,
            $"Moving {name} to {runnerName}",
            output =>
            {
                var prefixes = new Prefixes(layout, runner);
                prefixes.SetRunner(name, runnerName);
                prefixes.Run(name, "wineboot", ["-u"], output);
            },
            Refresh);

    private void InstallDxvk(string name) =>
        Operation.Run(
            window,
            $"Installing DXVK into {name}",
            _ => new Dxvk(layout, runner).Install(name),
            Refresh);

    private void Run(string name, string command, IReadOnlyList<string> arguments) =>
        Operation.Run(
            window,
            $"{command} in {name}",
            output => new Prefixes(layout, runner).Run(name, command, arguments, output));

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
                output => new Prefixes(layout, runner).Install(name, path, output),
                Refresh));

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
