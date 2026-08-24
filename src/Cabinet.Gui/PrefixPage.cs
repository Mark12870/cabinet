using Cabinet.Core;

namespace Cabinet.Gui;

internal sealed class PrefixPage
{
    private readonly Layout layout;
    private readonly IProcessRunner runner;
    private readonly Gtk.Window window;
    private readonly Action changed;
    private readonly Gtk.Box body = Gtk.Box.New(Gtk.Orientation.Vertical, 12);

    public PrefixPage(
        Layout layout, IProcessRunner runner, Gtk.Window window, string name, Action changed)
    {
        this.layout = layout;
        this.runner = runner;
        this.window = window;
        this.changed = changed;
        Name = name;

        var content = Ui.Page();
        content.Append(Ui.Scrolled(body));

        var view = Adw.ToolbarView.New();
        view.AddTopBar(Adw.HeaderBar.New());
        view.SetContent(content);

        Page = Adw.NavigationPage.New(view, name);
    }

    public string Name { get; }

    public Adw.NavigationPage Page { get; }

    public void Show(Prefix prefix, IReadOnlyList<string> runnerNames)
    {
        Ui.Clear(body);

        var settings = Adw.PreferencesGroup.New();
        settings.SetTitle("Settings");
        settings.Add(RunnerRow(prefix, runnerNames));
        settings.Add(SyncRow(prefix));
        settings.Add(DxvkRow(prefix));
        settings.Add(DesktopRow(prefix));

        var actions = Adw.PreferencesGroup.New();
        actions.SetTitle("Prefix");
        actions.Add(Action("Environment variables", Icons.Variables, EditVariables));
        actions.Add(Action("Windows installer", Icons.Install, ChooseInstaller));
        actions.Add(Action("Wine configuration", Icons.Configure, () => Run("winecfg", [])));
        actions.Add(Action("Run a command", Icons.Command, AskForCommand));
        actions.Add(Action("Delete", Icons.Delete, ConfirmDelete, destructive: true));

        body.Append(settings);
        body.Append(actions);
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
                UseRunner(chosen);
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
                UseSync(chosen);
            }
        };

        return row;
    }

    private Adw.SwitchRow DxvkRow(Prefix prefix)
    {
        var installed = prefix.Dxvk is not null;

        var row = Adw.SwitchRow.New();
        row.SetTitle("DXVK");
        row.SetSubtitle(prefix.Dxvk is null ? "not installed" : prefix.Dxvk);
        row.SetActive(installed);

        row.OnNotify += (_, args) =>
        {
            if (args.Pspec.GetName() != "active" || row.GetActive() == installed)
            {
                return;
            }

            if (row.GetActive())
            {
                InstallDxvk();
            }
            else
            {
                RemoveDxvk();
            }
        };

        return row;
    }

    private Adw.ActionRow DesktopRow(Prefix prefix)
    {
        var row = Adw.ActionRow.New();
        row.SetTitle("Virtual desktop");
        row.SetSubtitle(prefix.Desktop ?? "off");

        var button = Ui.RowButton(Icons.Desktop, "Virtual desktop");
        button.OnClicked += (_, _) => AskForDesktop(prefix);
        row.AddSuffix(button);
        row.SetActivatableWidget(button);

        return row;
    }

    private void AskForDesktop(Prefix prefix) =>
        Ui.Prompt(
            window,
            $"A desktop of its own for {Name}",
            "Wine draws every window from this prefix inside one window of that size. "
            + "It keeps clicks landing where you point them in a bridged editor. "
            + "Enter off to stop.",
            prefix.Desktop ?? "1920x1080",
            entered => UseDesktop(entered));

    private void UseDesktop(string entered) =>
        Operation.Run(
            window,
            $"Setting the desktop for {Name}",
            output =>
            {
                var desktop = new VirtualDesktop(layout, runner);

                if (entered.Trim().Equals("off", StringComparison.OrdinalIgnoreCase))
                {
                    desktop.Unset(Name, output);
                }
                else
                {
                    desktop.Set(Name, entered, output);
                }
            },
            changed);

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

    private void UseRunner(string runnerName) =>
        Operation.Run(
            window,
            $"Moving {Name} to {runnerName}",
            output =>
            {
                var prefixes = new Prefixes(layout, runner);
                prefixes.SetRunner(Name, runnerName);
                Operation.Ensure(prefixes.Run(Name, "wineboot", ["-u"], output), "wineboot");
            },
            changed);

    private void UseSync(SyncMode mode) =>
        Operation.Run(
            window,
            $"Putting {Name} on {Label(mode)}",
            _ => new PrefixSettings(layout).SetSync(Name, mode),
            changed);

    private void InstallDxvk() =>
        Operation.Run(
            window,
            $"Installing DXVK into {Name}",
            (output, progress) => new Dxvk(layout, runner).Install(Name, output, progress),
            changed);

    private void RemoveDxvk() =>
        Operation.Run(
            window,
            $"Taking DXVK out of {Name}",
            output => new Dxvk(layout, runner).Remove(Name, output),
            changed);

    private void EditVariables() =>
        new VariablesDialog(window, layout, Name, changed).Present();

    private void Run(string command, IReadOnlyList<string> arguments) =>
        Operation.Run(
            window,
            $"{command} in {Name}",
            output => Operation.Ensure(
                new Prefixes(layout, runner).Run(Name, command, arguments, output), command),
            changed);

    private void AskForCommand() =>
        Ui.Prompt(
            window,
            $"Run a command in {Name}",
            "It runs against this prefix's own Wine, the way `cabinet run` does.",
            "regedit",
            entered =>
            {
                var parts = entered.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                Run(parts[0], parts[1..]);
            });

    private void ChooseInstaller() =>
        Ui.ChooseFile(window, "Choose a Windows installer", path =>
            Operation.Run(
                window,
                $"Installing into {Name}",
                output => Operation.Ensure(
                    new Prefixes(layout, runner).Install(Name, path, output),
                    Path.GetFileName(path)),
                changed));

    private void ConfirmDelete() => Ui.Confirm(
        window,
        $"Delete “{Name}”?",
        "The prefix and every plugin installed in it will be removed.",
        "Delete",
        () => Operation.Run(
            window,
            $"Deleting {Name}",
            output => new Prefixes(layout, runner).Delete(Name, output),
            changed),
        Adw.ResponseAppearance.Destructive);
}
