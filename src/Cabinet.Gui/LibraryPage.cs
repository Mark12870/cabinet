using Cabinet.Core;

namespace Cabinet.Gui;

internal sealed class LibraryPage
{
    private readonly Layout layout;
    private readonly IProcessRunner runner;
    private readonly Gtk.Window window;
    private readonly Adw.NavigationView navigation;
    private readonly Action changed;
    private readonly Gtk.Box list = Gtk.Box.New(Gtk.Orientation.Vertical, 12);

    private PluginPage? open;

    public LibraryPage(
        Layout layout,
        IProcessRunner runner,
        Gtk.Window window,
        Adw.NavigationView navigation,
        Action changed)
    {
        this.layout = layout;
        this.runner = runner;
        this.window = window;
        this.navigation = navigation;
        this.changed = changed;

        navigation.OnPopped += (_, _) => open = null;

        var page = Ui.Page();
        page.Append(Ui.Scrolled(list));
        Widget = page;
    }

    public Gtk.Widget Widget { get; }

    public void Refresh()
    {
        Ui.Clear(list);

        var library = new Library(layout, runner);
        var entries = library.Entries();

        if (entries.Count == 0)
        {
            list.Append(Empty());
            Reopen(entries, new Dictionary<string, string?>());
            return;
        }

        var installed = library.Installed();

        Section("Windows plugins", "Each one gets a Wine prefix, bridged into your DAW.",
            entries.Where(entry => entry.Kind == PluginKind.Windows), installed);

        Section("Linux plugins", "VST3, CLAP and LV2, in Cabinet's own directory and linked out.",
            entries.Where(entry => entry.Kind == PluginKind.Native), installed);

        Reopen(entries, installed);
    }

    private void Reopen(
        IReadOnlyList<LibraryEntry> entries, IReadOnlyDictionary<string, string?> installed)
    {
        if (open is null)
        {
            return;
        }

        var still = entries.FirstOrDefault(entry => entry.Id == open.Id);

        if (still is null)
        {
            navigation.Pop();
            return;
        }

        open.Show(still, installed.GetValueOrDefault(still.Id), installed.ContainsKey(still.Id));
    }

    private void Open(LibraryEntry entry, string? prefix, bool installed)
    {
        var page = new PluginPage(
            layout, window, entry, one => Begin(one, prefix), one => ConfirmRemove(one, prefix));
        page.Show(entry, prefix, installed);

        open = page;
        navigation.Push(page.Page);
    }

    private void Section(
        string title,
        string description,
        IEnumerable<LibraryEntry> entries,
        IReadOnlyDictionary<string, string?> installed)
    {
        var found = entries.ToList();

        if (found.Count == 0)
        {
            return;
        }

        var group = Adw.PreferencesGroup.New();
        group.SetTitle(title);
        group.SetDescription(description);

        foreach (var entry in found)
        {
            group.Add(Row(entry, installed));
        }

        list.Append(group);
    }

    private static Adw.StatusPage Empty()
    {
        var empty = Adw.StatusPage.New();
        empty.SetIconName(Icons.Library);
        empty.SetTitle("Nothing in the library");
        empty.SetDescription("This build shipped without a catalogue of plugins.");
        return empty;
    }

    private Adw.ActionRow Row(LibraryEntry entry, IReadOnlyDictionary<string, string?> installed)
    {
        var here = installed.TryGetValue(entry.Id, out var prefix);

        var row = Adw.ActionRow.New();
        row.SetTitle(entry.Name);
        row.SetSubtitle(Subtitle(entry));
        row.AddPrefix(RowIcon(entry, here));

        if (here)
        {
            row.AddSuffix(Badge(prefix));
        }

        var enter = Ui.RowButton(Icons.Forward, $"About {entry.Name}");
        enter.OnClicked += (_, _) => Open(entry, prefix, here);
        row.AddSuffix(enter);
        row.SetActivatableWidget(enter);

        return row;
    }

    private Gtk.Widget RowIcon(LibraryEntry entry, bool installed)
    {
        if (layout.LibraryIcon(entry.Vendor, entry.Id) is { } file)
        {
            var art = Gtk.Image.NewFromFile(file);
            art.SetPixelSize(32);
            return art;
        }

        var icon = Gtk.Image.NewFromIconName(installed ? Icons.Ok : Icons.Prefixes);

        if (installed)
        {
            icon.AddCssClass("success");
        }

        return icon;
    }

    private static Gtk.Label Badge(string? prefix)
    {
        var badge = Gtk.Label.New(prefix is null ? "Installed" : $"Installed in {prefix}");
        badge.AddCssClass("success");
        badge.AddCssClass("caption-heading");
        badge.SetValign(Gtk.Align.Center);
        return badge;
    }

    private static string Subtitle(LibraryEntry entry)
    {
        var parts = new List<string>();

        if (entry.Developer is { } developer)
        {
            parts.Add(developer);
        }

        parts.Add(entry.Category);

        if (entry.Summary.Length > 0)
        {
            parts.Add(entry.Summary);
        }

        if (entry.Source == PluginSource.Byo)
        {
            parts.Add(entry.Account is null
                ? "needs the installer you bought"
                : "needs the file you download from your account");
        }

        return string.Join("  ·  ", parts);
    }

    private void Begin(LibraryEntry entry, string? already)
    {
        if (entry.Kind == PluginKind.Native)
        {
            ConfirmInstall(entry);
            return;
        }

        AskForPrefix(entry, already);
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
                + Presets(entry),
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
            + Presets(entry),
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
        open.OnClicked += (_, _) => Gtk.UriLauncher.New(account).LaunchAsync(window);
        row.AddSuffix(open);
        row.SetActivatableWidget(open);

        return row;
    }

    private void AskForPrefix(LibraryEntry entry, string? already)
    {
        var existing = new Prefixes(layout, runner).List().Select(one => one.Name).ToList();
        List<string> choices = ["New prefix", .. existing];

        var chosen = choices.IndexOf(already ?? entry.Prefix);

        var where = Adw.ComboRow.New();
        where.SetTitle("Prefix");
        where.SetModel(Gtk.StringList.New([.. choices]));
        where.SetSelected((uint)Math.Max(chosen, 0));

        var name = Adw.EntryRow.New();
        name.SetTitle("Name");
        name.SetText(entry.Prefix);
        name.SetVisible(where.GetSelected() == 0);

        where.OnNotify += (_, args) =>
        {
            if (args.Pspec.GetName() == "selected")
            {
                name.SetVisible(where.GetSelected() == 0);
            }
        };

        var fields = Adw.PreferencesGroup.New();

        if (AccountRow(entry) is { } account)
        {
            fields.Add(account);
        }

        fields.Add(where);
        fields.Add(name);

        Ui.Confirm(
            window,
            $"Install {entry.Name}?",
            Prospect(entry, chosen > 0 ? choices[chosen] : null),
            "Install",
            () =>
            {
                var selected = (int)where.GetSelected();
                var prefix = selected == 0 ? name.GetText().Trim() : choices[selected];

                if (prefix.Length == 0)
                {
                    return;
                }

                if (entry.Source == PluginSource.Byo)
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
    }

    private static string Prospect(LibraryEntry entry, string? into)
    {
        if (entry.Source == PluginSource.Byo)
        {
            return entry.Account is null
                ? $"{entry.Name} cannot be downloaded, so you will be asked for the installer "
                  + "you already have."
                : $"{entry.Name} cannot be downloaded. Log in, download it, and you will be "
                  + "asked for the file.";
        }

        var prefix = into is null
            ? "A prefix of its own keeps this plugin's dependencies away from every other."
            : $"It goes into the {into} prefix you already have, beside whatever is in it.";

        return entry.Source == PluginSource.Rolling
            ? "Cabinet downloads it and runs its installer under Wine. "
              + $"{Library.Unverifiable(entry.Url!)}\n\n{prefix}"
            : $"Cabinet downloads it from {new Uri(entry.Url!).Host} and runs its installer "
              + $"under Wine. {prefix}";
    }

    private void Start(LibraryEntry entry, string? prefix, string? installer) =>
        Operation.Run(
            window,
            $"Installing {entry.Name}",
            (output, progress) =>
                new Library(layout, runner).Install(entry, prefix, installer, output, progress),
            changed);

    private void ConfirmRemove(LibraryEntry entry, string? prefix)
    {
        if (entry.Kind == PluginKind.Native)
        {
            ConfirmRemoveNative(entry);
            return;
        }

        var library = new Library(layout, runner);
        var where = prefix ?? entry.Prefix;
        var sharing = library.Sharing(where, entry.Id);

        if (sharing.Count > 0)
        {
            Ui.Confirm(
                window,
                $"Remove {entry.Name}?",
                Kept(where, sharing) + " " + Wizard(entry),
                "Remove",
                () => Uninstall(entry, where),
                Adw.ResponseAppearance.Destructive);
            return;
        }

        Ui.Choose(
            window,
            $"Remove {entry.Name}?",
            $"It is the only plugin Cabinet installed in “{where}”. Deleting the prefix takes "
            + $"its Wine, its registry and its settings with it. {Wizard(entry)}",
            "Remove Plugin Only",
            () => Uninstall(entry, where),
            "Delete Prefix",
            () => Take(entry, where));
    }

    private static string Kept(string where, IReadOnlyList<string> sharing) =>
        $"Prefix “{where}” also holds {string.Join(" and ", sharing)}, so it stays.";

    private static string Wizard(LibraryEntry entry) =>
        $"{entry.Name}'s own uninstaller runs, and may open a window of its own.";

    private void ConfirmRemoveNative(LibraryEntry entry) => Ui.Confirm(
        window,
        $"Remove {entry.Name}?",
        entry.Data is null
            ? "Its files and the links your DAW scans are deleted. Presets you saved elsewhere "
              + "are left alone."
            : $"Its files and the links your DAW scans are deleted, and so is ~/{entry.Data} — "
              + "the presets you saved for it go with it.",
        "Remove",
        () => Operation.Run(
            window,
            $"Removing {entry.Name}",
            output => new Library(layout, runner).Remove(entry, onOutput: output),
            changed),
        Adw.ResponseAppearance.Destructive);

    private void Uninstall(LibraryEntry entry, string prefix) =>
        Operation.Run(
            window,
            $"Removing {entry.Name}",
            output => new Library(layout, runner).Remove(entry, prefix, onOutput: output),
            changed);

    private void Take(LibraryEntry entry, string prefix) =>
        Operation.Run(
            window,
            $"Deleting {prefix}",
            output => new Library(layout, runner)
                .Remove(entry, prefix, takePrefix: true, onOutput: output),
            changed);
}
