using Cabinet.Core;

namespace Cabinet.Gui;

internal sealed class LibraryPage
{
    private readonly Layout layout;
    private readonly IProcessRunner runner;
    private readonly Gtk.Window window;
    private readonly Adw.NavigationView navigation;
    private readonly Action changed;
    private readonly Action<string> toast;
    private readonly Gtk.Box list = Gtk.Box.New(Gtk.Orientation.Vertical, 12);
    private readonly Gtk.Box filters = Gtk.Box.New(Gtk.Orientation.Vertical, 12);
    private readonly Gtk.SearchEntry search = Gtk.SearchEntry.New();
    private readonly Gtk.DropDown categories = Gtk.DropDown.NewFromStrings(["Any category"]);
    private readonly Gtk.DropDown developers = Gtk.DropDown.NewFromStrings(["Any developer"]);
    private readonly Gtk.DropDown kinds =
        Gtk.DropDown.NewFromStrings(["Any kind", "Windows", "Linux"]);

    private readonly Gtk.DropDown states =
        Gtk.DropDown.NewFromStrings(["Any state", "Installed", "Not installed"]);

    private readonly HashSet<string> running = new(StringComparer.Ordinal);
    private readonly HashSet<string> stopping = new(StringComparer.Ordinal);

    private IReadOnlyList<LibraryEntry> entries = [];
    private IReadOnlyDictionary<string, string?> installed =
        new Dictionary<string, string?>(StringComparer.Ordinal);

    private PluginPage? open;

    public LibraryPage(
        Layout layout,
        IProcessRunner runner,
        Gtk.Window window,
        Adw.NavigationView navigation,
        Action changed,
        Action<string> toast)
    {
        this.layout = layout;
        this.runner = runner;
        this.window = window;
        this.navigation = navigation;
        this.changed = changed;
        this.toast = toast;

        navigation.OnPopped += (_, _) => open = null;

        var page = Ui.Page();
        page.Append(Filters());
        page.Append(Ui.Scrolled(list));
        Widget = page;
    }

    public Gtk.Widget Widget { get; }

    public void Refresh()
    {
        var library = new Library(layout, runner);
        entries = library.Entries();
        installed = entries.Count == 0
            ? new Dictionary<string, string?>(StringComparer.Ordinal)
            : library.Installed();

        Fill(categories, "Any category", Library.Categories(entries));
        Fill(developers, "Any developer", Library.Developers(entries));

        Rebuild();
    }

    private Gtk.Widget Filters()
    {
        search.SetPlaceholderText("Search by name, developer or what it does");
        search.SetHexpand(true);
        search.OnSearchChanged += (_, _) => Rebuild();
        filters.Append(search);

        var row = Gtk.Box.New(Gtk.Orientation.Horizontal, 12);
        row.Append(Narrowing(categories, "Category"));
        row.Append(Narrowing(developers, "Developer"));
        row.Append(Narrowing(kinds, "Kind"));
        row.Append(Narrowing(states, "Installed"));

        filters.Append(row);
        return filters;
    }

    private Gtk.DropDown Narrowing(Gtk.DropDown drop, string what)
    {
        drop.SetTooltipText(what);
        drop.SetHexpand(true);
        drop.OnNotify += (_, args) =>
        {
            if (args.Pspec.GetName() == "selected")
            {
                Rebuild();
            }
        };

        return drop;
    }

    private void Clear()
    {
        search.SetText("");
        categories.SetSelected(0);
        developers.SetSelected(0);
        kinds.SetSelected(0);
        states.SetSelected(0);
        Rebuild();
    }

    private static void Fill(Gtk.DropDown drop, string any, IReadOnlyList<string> values)
    {
        var chosen = Narrowed(drop);
        string[] options = [any, .. values];
        drop.SetModel(Gtk.StringList.New(options));
        drop.SetSelected((uint)Math.Max(Array.IndexOf(options, chosen ?? any), 0));
    }

    private static string? Narrowed(Gtk.DropDown drop) =>
        drop.GetSelected() == 0
            ? null
            : (drop.GetModel() as Gtk.StringList)?.GetString(drop.GetSelected());

    private LibraryFilter Filter() => new(
        search.GetText(),
        Narrowed(categories),
        Narrowed(developers),
        kinds.GetSelected() switch
        {
            1 => PluginKind.Windows,
            2 => PluginKind.Native,
            _ => null,
        },
        states.GetSelected() switch
        {
            1 => true,
            2 => false,
            _ => null,
        });

    private void Rebuild()
    {
        Ui.Clear(list);

        if (entries.Count == 0)
        {
            filters.SetVisible(false);
            list.Append(Empty());
            Reopen();
            return;
        }

        filters.SetVisible(true);

        var filter = Filter();
        var matching = entries
            .Where(entry => filter.Matches(entry, installed.ContainsKey(entry.Id)))
            .ToList();

        var managers = matching
            .Where(entry => entry.Manager && installed.ContainsKey(entry.Id))
            .ToList();

        var pinned = managers.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);

        Section(
            "Managers",
            "The applications that download and update plugins of their own.",
            managers);

        Section(
            "Windows plugins",
            "Each one gets a Wine prefix, bridged into your DAW.",
            matching.Where(entry =>
                entry.Kind == PluginKind.Windows && !pinned.Contains(entry.Id)));

        Section(
            "Linux plugins",
            "VST3, CLAP and LV2, in Cabinet's own directory and linked out.",
            matching.Where(entry =>
                entry.Kind == PluginKind.Native && !pinned.Contains(entry.Id)));

        if (matching.Count == 0)
        {
            list.Append(Nothing());
        }

        Reopen();
    }

    private void Reopen()
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

        open.Show(
            still,
            installed.GetValueOrDefault(still.Id),
            installed.ContainsKey(still.Id),
            running.Contains(still.Id));
    }

    private void Open(LibraryEntry entry, string? prefix, bool here)
    {
        var page = new PluginPage(
            layout,
            window,
            entry,
            one => Begin(one, prefix),
            one => ConfirmRemove(one, prefix),
            one => Launch(one, prefix),
            one => Stop(one, prefix),
            one => new Library(layout, runner).LaunchLog(one, prefix));
        page.Show(entry, prefix, here, running.Contains(entry.Id));

        open = page;
        navigation.Push(page.Page);
    }

    private void Section(string title, string description, IEnumerable<LibraryEntry> entries)
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
            group.Add(Row(entry));
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

    private Adw.StatusPage Nothing()
    {
        var empty = Adw.StatusPage.New();
        empty.SetIconName(Icons.Library);
        empty.SetTitle("Nothing matches");
        empty.SetDescription("No plugin in the library answers to that search and those filters.");

        var clear = Gtk.Button.NewWithLabel("Clear filters");
        clear.SetHalign(Gtk.Align.Center);
        clear.AddCssClass("pill");
        clear.OnClicked += (_, _) => Clear();
        empty.SetChild(clear);

        return empty;
    }

    private Adw.ActionRow Row(LibraryEntry entry)
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

        if (entry.Manager && here)
        {
            row.AddSuffix(Control(entry, prefix));
        }

        var enter = Ui.RowButton(Icons.Forward, $"About {entry.Name}");
        enter.OnClicked += (_, _) => Open(entry, prefix, here);
        row.AddSuffix(enter);
        row.SetActivatableWidget(enter);

        return row;
    }

    private Gtk.Button Control(LibraryEntry entry, string? prefix)
    {
        if (running.Contains(entry.Id))
        {
            var halt = Ui.RowButton(Icons.Stop, $"Stop {entry.Name}");
            halt.SetSensitive(!stopping.Contains(entry.Id));
            halt.OnClicked += (_, _) => Stop(entry, prefix);
            return halt;
        }

        var start = Ui.RowButton(Icons.Play, $"Open {entry.Name}");
        start.OnClicked += (_, _) => Launch(entry, prefix);
        return start;
    }

    private Gtk.Widget RowIcon(LibraryEntry entry, bool here)
    {
        if (layout.LibraryIcon(entry.Vendor, entry.Id) is { } file)
        {
            var art = Gtk.Image.NewFromFile(file);
            art.SetPixelSize(32);
            return art;
        }

        var icon = Gtk.Image.NewFromIconName(here ? Icons.Ok : Icons.Prefixes);

        if (here)
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

        if (entry.DemoUrl is not null)
        {
            parts.Add("offers a demo or your own installer");
        }
        else if (entry.Source == PluginSource.Byo)
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

        Adw.ComboRow? installer = null;

        if (entry.DemoUrl is not null)
        {
            installer = Adw.ComboRow.New();
            installer.SetTitle("Installer");
            installer.SetModel(Gtk.StringList.New(["Download demo", "Use my installation file"]));
        }

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

        if (installer is not null)
        {
            fields.Add(installer);
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
    }

    private static string Prospect(LibraryEntry entry, string? into)
    {
        var prefix = into is null
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

        var where = prefix ?? entry.Prefix;

        if (entry.Launch is not null)
        {
            Ui.Confirm(
                window,
                $"Delete “{where}”?",
                $"{entry.Name}'s own uninstaller leaves everything it downloaded behind, so it "
                + $"is the prefix or nothing: its Wine, its registry and every library "
                + $"{entry.Name} put in it go together.",
                "Delete Prefix",
                () => Take(entry, where),
                Adw.ResponseAppearance.Destructive);
            return;
        }

        var library = new Library(layout, runner);
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

    private void Launch(LibraryEntry entry, string? prefix)
    {
        running.Add(entry.Id);
        toast($"Opening {entry.Name}.");
        changed();

        Task.Run(() =>
        {
            try
            {
                new Library(layout, runner).Launch(entry, prefix);
            }
            catch (Exception exception)
            {
                Ui.OnMainLoop(() =>
                {
                    if (!stopping.Contains(entry.Id))
                    {
                        toast(exception.Message);
                    }
                });
            }
        }).ContinueWith(_ => Ui.OnMainLoop(() =>
        {
            running.Remove(entry.Id);
            stopping.Remove(entry.Id);
            changed();
        }));
    }

    private void Stop(LibraryEntry entry, string? prefix)
    {
        stopping.Add(entry.Id);
        toast($"Stopping {entry.Name}.");
        changed();

        Task.Run(() =>
        {
            try
            {
                new Library(layout, runner).Stop(entry, prefix);
            }
            catch (Exception exception)
            {
                Ui.OnMainLoop(() => toast(exception.Message));
            }
        }).ContinueWith(_ => Ui.OnMainLoop(changed));
    }

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
