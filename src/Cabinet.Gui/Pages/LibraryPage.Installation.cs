using Cabinet.Core;

namespace Cabinet.Gui;

internal sealed partial class LibraryPage
{
    private void Begin(LibraryEntry entry)
    {
        if (entry.Kind == PluginKind.Native)
        {
            ConfirmInstall(entry);
            return;
        }

        AskForPrefix(entry, new Library(layout, runner).Installed().GetValueOrDefault(entry.Id));
    }

    private void ConfirmInstall(LibraryEntry entry)
    {
        if (entry.Source == PluginSource.Byo)
        {
            Ui.Confirm(
                window,
                $"Install {entry.Name}?",
                $"Cabinet cannot download {entry.Name}. Log in, download it, then choose the "
                + "file — Cabinet keeps it in its own directory and links it into ~/.vst3, "
                + "~/.clap, ~/.lv2 and ~/.vst. Rescan in your DAW afterwards."
                + Presets(entry)
                + $"\n\n{entry.Consent}",
                "Choose File…",
                () => Ui.ChooseFile(
                    window,
                    $"Choose the {entry.Name} download",
                    file => Start(entry, null, file)),
                extra: AccountGroup(entry));
            return;
        }

        Ui.Confirm(
            window,
            $"Install {entry.Name}?",
            (entry.Source == PluginSource.Rolling
                ? "Cabinet downloads it, keeps it in its own directory and links it into "
                  + "~/.vst3, ~/.clap, ~/.lv2 and ~/.vst. Rescan in your DAW afterwards."
                  + $"\n\n{Library.Unverifiable(entry.Url!)}"
                : $"Cabinet downloads it from {new Uri(entry.Url!).Host}, keeps it in its own "
                  + "directory and links it into ~/.vst3, ~/.clap, ~/.lv2 and ~/.vst. Rescan in "
                  + "your DAW afterwards.")
            + Presets(entry)
            + $"\n\n{entry.Consent}",
            "Install",
            () => Start(entry, null, null));
    }

    private static string Presets(LibraryEntry entry) => entry.Data is null
        ? ""
        : $"\n\nIts presets and resources go in ~/{entry.Data}, which is where this plugin "
          + "looks for them.";

    private Adw.PreferencesGroup? AccountGroup(LibraryEntry entry)
    {
        if (AccountRow(entry) is not { } row)
        {
            return null;
        }

        var group = Adw.PreferencesGroup.New();
        group.Add(row);
        return group;
    }

    private Adw.ActionRow? AccountRow(LibraryEntry entry)
    {
        if (entry.Account is not { } account)
        {
            return null;
        }

        var host = new Uri(account).Host;
        var row = Adw.ActionRow.New();
        row.SetTitle("Log in and download");
        row.SetSubtitle(host);

        var open = Ui.RowButton(Icons.Link, $"{entry.Name} at {host}");
        open.OnClicked += (_, _) => Ui.Guard(() =>
            Ui.Observe(Gtk.UriLauncher.New(account).LaunchAsync(window)));
        row.AddSuffix(open);
        row.SetActivatableWidget(open);

        return row;
    }

    private void AskForPrefix(LibraryEntry entry, string? already)
    {
        var prefixes = new Prefixes(layout, runner);
        var existing = prefixes.Names();
        List<string> choices = already is null ? ["New prefix", .. existing] : [already];

        var chosen = choices.IndexOf(already ?? entry.Prefix);

        var where = Adw.ComboRow.New();
        where.SetTitle("Prefix");
        where.SetModel(Gtk.StringList.New([.. choices]));
        where.SetSelected((uint)Math.Max(chosen, 0));

        var name = Adw.EntryRow.New();
        name.SetTitle("Name");
        name.SetText(entry.Prefix);
        name.SetVisible(already is null && where.GetSelected() == 0);

        Adw.ComboRow? installer = null;

        if (entry.DemoUrl is not null)
        {
            installer = Adw.ComboRow.New();
            installer.SetTitle("Installer");
            installer.SetModel(Gtk.StringList.New(["Download demo", "Use my installation file"]));
        }

        Adw.AlertDialog? asking = null;

        string? Into() =>
            already ?? (where.GetSelected() == 0 ? null : choices[(int)where.GetSelected()]);

        string? NameProblem() =>
            Into() is null ? prefixes.NewNameProblem(name.GetText().Trim()) : null;

        where.OnNotify += (_, args) =>
        {
            Ui.Guard(() =>
            {
                if (args.Pspec.GetName() == "selected")
                {
                    name.SetVisible(already is null && where.GetSelected() == 0);
                    asking?.SetBody(Prospect(entry, Into(), already is not null));
                    asking?.SetResponseEnabled("ok", NameProblem() is null);
                }
            });
        };

        var fields = Adw.PreferencesGroup.New();

        if (AccountRow(entry) is { } account)
        {
            fields.Add(account);
        }

        if (installer is not null)
        {
            fields.Add(installer);
        }

        fields.Add(where);
        fields.Add(name);

        asking = Ui.Confirm(
            window,
            already is null ? $"Install {entry.Name}?" : $"Reinstall {entry.Name}?",
            Prospect(entry, Into(), already is not null),
            already is null ? "Install" : "Reinstall",
            () =>
            {
                var prefix = Into() ?? name.GetText().Trim();

                if (entry.Source == PluginSource.Byo
                    && (installer is null || installer.GetSelected() == 1))
                {
                    Ui.ChooseFile(
                        window,
                        $"Choose the {entry.Name} installer",
                        installer => Start(entry, prefix, installer));
                    return;
                }

                Start(entry, prefix, null);
            },
            extra: fields);
        Ui.RequireName(asking, name, NameProblem);
    }

    private static string Prospect(LibraryEntry entry, string? into, bool again) =>
        $"{Placement(entry, into, again)}\n\n{entry.Consent}";

    private static string Placement(LibraryEntry entry, string? into, bool again)
    {
        var prefix = again
            ? $"Its installer runs again in {into}, over what it installed there before."
            : into is null
            ? "A prefix of its own keeps this plugin's dependencies away from every other."
            : $"It goes into the {into} prefix you already have, beside whatever is in it.";

        if (entry.DemoUrl is not null)
        {
            return "Download the demo, or choose an installation file you already have. Both "
                   + $"use the same Cabinet prefix and Wine settings.\n\n{prefix}";
        }

        if (entry.Source == PluginSource.Byo)
        {
            return entry.Account is null
                ? $"{entry.Name} cannot be downloaded, so you will be asked for the installer "
                  + "you already have."
                : $"{entry.Name} cannot be downloaded. Log in, download it, and you will be "
                  + "asked for the file.";
        }

        return entry.Source == PluginSource.Rolling
            ? "Cabinet downloads it and runs its installer under Wine. "
              + $"{Library.Unverifiable(entry.Url!)}\n\n{prefix}"
            : $"Cabinet downloads it from {new Uri(entry.Url!).Host} and runs its installer "
              + $"under Wine. {prefix}";
    }

    private void Start(LibraryEntry entry, string? prefix, string? installer)
    {
        if (prefix is not null && prefixIsChanging(prefix))
        {
            toast($"{prefix} is changing; try installing {entry.Name} again when it is done.");
            return;
        }

        operations.Run(
            $"Installing {entry.Name}",
            (output, progress) =>
                new Library(layout, runner).Install(entry, prefix, installer, output, progress),
            changed);
    }
}
