using Cabinet.Core;

namespace Cabinet.Gui;

internal sealed class PrefixPage
{
    private readonly Layout layout;
    private readonly IProcessRunner runner;
    private readonly Gtk.Window window;
    private readonly Action changed;
    private readonly Action<string> toast;
    private readonly Func<bool> tryBeginChange;
    private readonly Action endChange;
    private readonly Action hold;
    private readonly Action release;
    private readonly Gtk.Box body = Gtk.Box.New(Gtk.Orientation.Vertical, 12);

    public PrefixPage(
        Layout layout,
        IProcessRunner runner,
        Gtk.Window window,
        string name,
        Action changed,
        Action<string> toast,
        Func<bool> tryBeginChange,
        Action endChange,
        Action hold,
        Action release)
    {
        this.layout = layout;
        this.runner = runner;
        this.window = window;
        this.changed = changed;
        this.toast = toast;
        this.tryBeginChange = tryBeginChange;
        this.endChange = endChange;
        this.hold = hold;
        this.release = release;
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

    public void Show(Prefix prefix, IReadOnlyList<string> runnerNames, bool busy)
    {
        Ui.Clear(body);
        body.SetSensitive(!busy);

        var settings = Adw.PreferencesGroup.New();
        settings.SetTitle("Settings");
        settings.Add(RunnerRow(prefix, runnerNames));
        settings.Add(SyncRow(prefix));
        settings.Add(DxvkRow(prefix));
        settings.Add(DesktopRow(prefix));

        var actions = Adw.PreferencesGroup.New();
        actions.SetTitle("Prefix");
        actions.Add(Ui.ActionRow("Environment variables", "", Icons.Variables, EditVariables));
        actions.Add(Ui.ActionRow("Winetricks", "", Icons.Configure, OpenWinetricks));
        actions.Add(
            Ui.ActionRow("Wine configuration", "", Icons.Configure, () => Run("winecfg", [])));
        actions.Add(Ui.ActionRow("Windows installer", "", Icons.Install, ChooseInstaller));
        actions.Add(Ui.ActionRow("Run a command", "", Icons.Command, AskForCommand));
        actions.Add(
            Ui.ActionRow("Delete", "", Icons.Delete, ConfirmDelete, destructive: true));

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

    private Adw.ActionRow SyncRow(Prefix prefix)
    {
        var choices = PrefixSettings.SyncModes;
        var spinner = Gtk.Spinner.New();
        var combo = Gtk.DropDown.NewFromStrings([.. choices.Select(Label)]);
        var row = Adw.ActionRow.New();

        row.SetTitle("Sync");
        row.AddSuffix(spinner);
        row.AddSuffix(combo);
        row.SetActivatableWidget(combo);
        combo.SetValign(Gtk.Align.Center);
        combo.SetSelected((uint)choices.ToList().IndexOf(prefix.Sync));
        spinner.SetVisible(false);

        combo.OnNotify += (_, args) =>
        {
            if (args.Pspec.GetName() != "selected")
            {
                return;
            }

            var chosen = choices[(int)combo.GetSelected()];

            if (chosen != prefix.Sync)
            {
                if (!tryBeginChange())
                {
                    combo.SetSelected((uint)choices.ToList().IndexOf(prefix.Sync));
                    return;
                }

                body.SetSensitive(false);
                spinner.SetVisible(true);
                spinner.Start();
                RunSetting(
                    spinner,
                    () => new PrefixSettings(layout).SetSync(Name, chosen));
            }
        };

        return row;
    }

    private Adw.ActionRow DxvkRow(Prefix prefix) =>
        ToggleRow(
            "DXVK",
            prefix.Dxvk is null ? "not installed" : prefix.Dxvk,
            prefix.Dxvk is not null,
            enabled =>
            {
                if (enabled)
                {
                    new Dxvk(layout, runner).Install(Name);
                }
                else
                {
                    new Dxvk(layout, runner).Remove(Name);
                }
            });

    private Adw.ActionRow DesktopRow(Prefix prefix) =>
        ToggleRow(
            "Virtual desktop",
            "Confines this prefix's windows to their own desktop",
            prefix.Desktop,
            enabled =>
            {
                var desktop = new VirtualDesktop(layout, runner);

                if (enabled)
                {
                    desktop.Set(Name, null);
                }
                else
                {
                    desktop.Unset(Name, null);
                }
            });

    private Adw.ActionRow ToggleRow(
        string title,
        string subtitle,
        bool enabled,
        Action<bool> operation)
    {
        var spinner = Gtk.Spinner.New();
        var toggle = Gtk.Switch.New();
        var row = Adw.ActionRow.New();

        spinner.SetVisible(false);
        toggle.SetValign(Gtk.Align.Center);
        row.SetTitle(title);
        row.SetSubtitle(subtitle);
        row.AddSuffix(spinner);
        row.AddSuffix(toggle);
        row.SetActivatableWidget(toggle);
        toggle.SetActive(enabled);

        toggle.OnNotify += (_, args) =>
        {
            if (args.Pspec.GetName() != "active" || toggle.GetActive() == enabled)
            {
                return;
            }

            if (!tryBeginChange())
            {
                return;
            }

            var wanted = toggle.GetActive();
            body.SetSensitive(false);
            spinner.SetVisible(true);
            spinner.Start();
            RunSetting(spinner, () => operation(wanted));
        };

        return row;
    }

    private void RunSetting(Gtk.Spinner spinner, Action operation)
    {
        hold();

        Task.Run(() =>
        {
            try
            {
                operation();
                Ui.OnMainLoop(() => FinishSetting(spinner, null));
            }
            catch (Exception exception)
            {
                Ui.OnMainLoop(() => FinishSetting(spinner, exception));
            }
        });
    }

    private void FinishSetting(Gtk.Spinner spinner, Exception? exception)
    {
        try
        {
            spinner.Stop();
            spinner.SetVisible(false);
            endChange();
            body.SetSensitive(true);
            changed();

            if (exception is not null)
            {
                toast(exception.Message);
            }
        }
        finally
        {
            release();
        }
    }

    private static string Label(SyncMode mode) =>
        mode == SyncMode.System ? "System" : PrefixSettings.Word(mode);

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

    private void EditVariables() =>
        new VariablesDialog(window, layout, Name, changed).Present();

    private void OpenWinetricks() =>
        Operation.Run(
            window,
            $"Configuring {Name} with Winetricks",
            output => Operation.Ensure(
                new Winetricks(layout, runner).Open(Name, output), "winetricks"),
            changed);

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
