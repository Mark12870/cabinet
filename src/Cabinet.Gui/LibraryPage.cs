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
    private readonly Action<string, Func<string?>?> report;
    private readonly Action hold;
    private readonly Action release;
    private readonly Func<string, bool> prefixIsChanging;
    private readonly Operation operations;
    private readonly Gtk.Box list = Gtk.Box.New(Gtk.Orientation.Vertical, 18);
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
    private IReadOnlyList<LibraryEntry> retired = [];
    private IReadOnlyDictionary<string, string?> installed =
        new Dictionary<string, string?>(StringComparer.Ordinal);

    private PluginPage? open;
    private readonly RefreshGeneration generation = new();
    private bool fillingFilters;

    public LibraryPage(
        Layout layout,
        IProcessRunner runner,
        Gtk.Window window,
        Adw.NavigationView navigation,
        Action changed,
        Action<string> toast,
        Action<string, Func<string?>?> report,
        Action hold,
        Action release,
        Func<string, bool> prefixIsChanging,
        Operation operations)
    {
        this.layout = layout;
        this.runner = runner;
        this.window = window;
        this.navigation = navigation;
        this.changed = changed;
        this.toast = toast;
        this.report = report;
        this.hold = hold;
        this.release = release;
        this.prefixIsChanging = prefixIsChanging;
        this.operations = operations;

        navigation.OnPopped += (_, _) => Ui.Guard(() => open = null);

        var page = Ui.Page();
        page.Append(Filters());
        page.Append(Ui.Scrolled(list));
        Widget = page;
    }

    public Gtk.Widget Widget { get; }

    public void Refresh()
    {
        var current = generation.Next();

        Task.Run(() =>
        {
            var library = new Library(layout, runner);
            return new Snapshot(library.Entries(), library.Installed(), library.Retired());
        }).ContinueWith(task => Ui.OnMainLoop(() =>
        {
            if (!generation.IsCurrent(current))
            {
                return;
            }

            if (task.IsFaulted)
            {
                ShowFailure(task.Exception!.InnerException!.Message);
                return;
            }

            entries = task.Result.Entries;
            installed = task.Result.Installed;
            retired = task.Result.Retired;
            fillingFilters = true;

            try
            {
                Fill(categories, "Any category", Library.Categories(entries));
                Fill(developers, "Any developer", Library.Developers(entries));
            }
            finally
            {
                fillingFilters = false;
            }

            Rebuild();
        }));
    }

    private Gtk.Widget Filters()
    {
        search.SetPlaceholderText("Search by name, developer or what it does");
        search.SetHexpand(true);
        search.OnSearchChanged += (_, _) => Ui.Guard(Rebuild);
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
            if (!fillingFilters && args.Pspec.GetName() == "selected")
            {
                Ui.Guard(Rebuild);
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
            Retired(retired);
            Reopen();
            return;
        }

        filters.SetVisible(true);

        var filter = Filter();
        var matching = entries
            .Where(entry => filter.Matches(entry, installed.ContainsKey(entry.Id)))
            .ToList();

        var managers = matching
            .Where(entry => entry.Manager)
            .ToList();

        var pinned = managers.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);

        Section("Managers", managers);

        Section(
            "Windows plugins",
            matching.Where(entry =>
                entry.Kind == PluginKind.Windows && !pinned.Contains(entry.Id)));

        Section(
            "Linux plugins",
            matching.Where(entry =>
                entry.Kind == PluginKind.Native && !pinned.Contains(entry.Id)));

        var gone = retired.Where(entry => filter.Matches(entry, true)).ToList();
        Retired(gone);

        if (matching.Count == 0 && gone.Count == 0)
        {
            list.Append(Nothing());
        }

        Reopen();
    }

    private void ShowFailure(string message)
    {
        Ui.Clear(list);
        var failed = Adw.StatusPage.New();
        failed.SetIconName(Icons.Fail);
        failed.SetTitle("Could not read the library");
        failed.SetDescription(message);
        list.Append(failed);
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
            Begin,
            ConfirmRemove,
            Launch,
            Stop,
            one => LaunchLog(one)());
        page.Show(entry, prefix, here, running.Contains(entry.Id));

        open = page;
        navigation.Push(page.Page);
    }

    private void Section(string title, IEnumerable<LibraryEntry> entries)
    {
        var found = entries.ToList();

        if (found.Count == 0)
        {
            return;
        }

        var group = Adw.PreferencesGroup.New();
        group.SetTitle(title);

        foreach (var entry in found)
        {
            group.Add(Row(entry));
        }

        list.Append(group);
    }

    private void Retired(IReadOnlyList<LibraryEntry> gone)
    {
        if (gone.Count == 0)
        {
            return;
        }

        var group = Adw.PreferencesGroup.New();
        group.SetTitle("No longer in the catalogue");
        group.SetDescription(
            "An earlier Cabinet installed these. They keep working until you remove them.");

        foreach (var entry in gone)
        {
            var row = Adw.ActionRow.New();
            row.SetUseMarkup(false);
            row.SetTitle(entry.Id);
            row.SetSubtitle(installed.GetValueOrDefault(entry.Id) is { } prefix
                ? $"Windows plugin in {prefix}"
                : "Linux plugin");

            var remove = Ui.RowButton(Icons.Delete, $"Remove {entry.Id}", destructive: true);
            remove.OnClicked += (_, _) => Ui.Guard(() => ConfirmRemove(entry));
            row.AddSuffix(remove);
            group.Add(row);
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
        clear.OnClicked += (_, _) => Ui.Guard(Clear);
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
            row.AddSuffix(Control(entry));
        }

        var enter = Ui.RowButton(Icons.Forward, $"About {entry.Name}");
        enter.OnClicked += (_, _) => Ui.Guard(() => Open(entry, prefix, here));
        row.AddSuffix(enter);
        row.SetActivatableWidget(enter);

        return row;
    }

    private Gtk.Button Control(LibraryEntry entry)
    {
        if (running.Contains(entry.Id))
        {
            var halt = Ui.RowButton(Icons.Stop, $"Stop {entry.Name}");
            halt.SetSensitive(!stopping.Contains(entry.Id));
            halt.OnClicked += (_, _) => Ui.Guard(() => Stop(entry));
            return halt;
        }

        var start = Ui.RowButton(Icons.Play, $"Open {entry.Name}");
        start.OnClicked += (_, _) => Ui.Guard(() => Launch(entry));
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
        open.OnClicked += (_, _) => Ui.Guard(() =>
            Ui.Observe(Gtk.UriLauncher.New(account).LaunchAsync(window)));
        row.AddSuffix(open);
        row.SetActivatableWidget(open);

        return row;
    }

    private void AskForPrefix(LibraryEntry entry, string? already)
    {
        var existing = new Prefixes(layout, runner).List().Select(one => one.Name).ToList();
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

        where.OnNotify += (_, args) =>
        {
            Ui.Guard(() =>
            {
                if (args.Pspec.GetName() == "selected")
                {
                    name.SetVisible(already is null && where.GetSelected() == 0);
                    asking?.SetBody(Prospect(entry, Into(), already is not null));
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

                if (Into() is null && prefix.Length == 0)
                {
                    toast("A new prefix needs a name.");
                    return;
                }

                if (Into() is null && existing.Contains(prefix))
                {
                    toast($"A prefix named {prefix} is already there — choose it from the "
                          + "list to install beside what it holds.");
                    return;
                }

                if (Into() is null && !Layout.IsName(prefix))
                {
                    toast($"{prefix} cannot name a prefix: use one word of a path, not "
                          + "starting with a dot.");
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

    private static string Prospect(LibraryEntry entry, string? into, bool again)
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

    private void ConfirmRemove(LibraryEntry entry)
    {
        Removal removal;

        try
        {
            removal = new Library(layout, runner).RemovalOf(entry);
        }
        catch (Exception gone)
        {
            toast(gone.Message);
            changed();
            return;
        }

        if (removal.Kind == RemovalKind.Native)
        {
            ConfirmRemoveNative(removal);
            return;
        }

        var where = removal.Prefix!;

        if (prefixIsChanging(where))
        {
            toast($"{where} is changing; try removing {entry.Name} again when it is done.");
            return;
        }

        if (removal.Kind == RemovalKind.TakesPrefix)
        {
            Ui.Confirm(
                window,
                $"Delete “{where}”?",
                $"{entry.Name}'s own uninstaller leaves everything it downloaded behind, so it "
                + $"is the prefix or nothing: its Wine, its registry and every library "
                + $"{entry.Name} put in it go together."
                + (removal.Sharing.Count > 0
                    ? $" {string.Join(" and ", removal.Sharing)} go with it."
                    : ""),
                "Delete Prefix",
                () => Take(removal),
                Adw.ResponseAppearance.Destructive);
            return;
        }

        if (removal.Kind == RemovalKind.KeepsPrefix)
        {
            Ui.Confirm(
                window,
                $"Remove {entry.Name}?",
                Kept(where, removal.Sharing) + " " + Wizard(entry),
                "Remove",
                () => Uninstall(removal),
                Adw.ResponseAppearance.Destructive);
            return;
        }

        Ui.Choose(
            window,
            $"Remove {entry.Name}?",
            $"It is the only plugin Cabinet installed in “{where}”. Deleting the prefix takes "
            + $"its Wine, its registry and its settings with it. {Wizard(entry)}",
            "Remove Plugin Only",
            () => Uninstall(removal),
            "Delete Prefix",
            () => Take(removal));
    }

    private static string Kept(string where, IReadOnlyList<string> sharing) =>
        $"Prefix “{where}” also holds {string.Join(" and ", sharing)}, so it stays.";

    private static string Wizard(LibraryEntry entry) =>
        $"{entry.Name}'s own uninstaller runs, and may open a window of its own.";

    private void ConfirmRemoveNative(Removal removal) => Ui.Confirm(
        window,
        $"Remove {removal.Entry.Name}?",
        removal.Entry.Data is null
            ? "Its files and the links your DAW scans are deleted. Presets you saved elsewhere "
              + "are left alone."
            : $"Its files and the links your DAW scans are deleted, and so is "
              + $"~/{removal.Entry.Data} — the presets you saved for it go with it.",
        "Remove",
        () => operations.Run(
            $"Removing {removal.Entry.Name}",
            output => new Library(layout, runner).Remove(removal, onOutput: output),
            changed),
        Adw.ResponseAppearance.Destructive);

    public void OpenLink(string link)
    {
        hold();

        Task.Run(() => new Library(layout, runner).ForLink(link)).ContinueWith(found =>
            Ui.OnMainLoop(() =>
            {
                try
                {
                    if (found.Exception?.GetBaseException() is { } exception)
                    {
                        report($"Cabinet could not open the link: {exception.Message}", null);
                    }
                    else
                    {
                        HandLink(found.Result, link);
                    }
                }
                finally
                {
                    release();
                }
            }));
    }

    private void HandLink(LibraryEntry entry, string link)
    {
        if (!running.Contains(entry.Id))
        {
            Launch(entry, library => library.Open(link));
            return;
        }

        toast($"Handing the link to {entry.Name}.");
        hold();

        Task.Run(() => new Library(layout, runner).Open(link)).ContinueWith(handed =>
            Ui.OnMainLoop(() =>
            {
                try
                {
                    if (handed.Exception?.GetBaseException() is { } exception)
                    {
                        report($"{entry.Name} was not handed the link: {Told(exception)}",
                            LaunchLog(entry));
                    }
                }
                finally
                {
                    release();
                }
            }));
    }

    private void Launch(LibraryEntry entry) =>
        Launch(entry, library => library.Launch(entry));

    private void Launch(LibraryEntry entry, Action<Library> start)
    {
        running.Add(entry.Id);
        toast($"Opening {entry.Name}.");
        changed();
        hold();

        Task.Run(() =>
        {
            try
            {
                start(new Library(layout, runner));
            }
            catch (Exception exception)
            {
                var told = Told(exception);
                Ui.OnMainLoop(() =>
                {
                    if (!stopping.Contains(entry.Id))
                    {
                        report($"{entry.Name} did not open: {told}", LaunchLog(entry));
                    }
                });
            }
        }).ContinueWith(_ => Ui.OnMainLoop(() =>
        {
            try
            {
                running.Remove(entry.Id);
                stopping.Remove(entry.Id);
                changed();
            }
            finally
            {
                release();
            }
        }));
    }

    private void Stop(LibraryEntry entry)
    {
        stopping.Add(entry.Id);
        toast($"Stopping {entry.Name}.");
        changed();

        Task.Run(() =>
        {
            try
            {
                var outcome = new Library(layout, runner).Stop(entry);
                Ui.OnMainLoop(() =>
                {
                    if (outcome.Result == StopResult.Closed)
                    {
                        toast(outcome.Told);
                        return;
                    }

                    if (outcome.Result == StopResult.LeftRunning)
                    {
                        stopping.Remove(entry.Id);
                    }

                    report(outcome.Told, LaunchLog(entry));
                });
            }
            catch (Exception exception)
            {
                var told = Told(exception);
                Ui.OnMainLoop(() =>
                {
                    stopping.Remove(entry.Id);
                    report($"{entry.Name} did not stop: {told}", LaunchLog(entry));
                });
            }
        }).ContinueWith(_ => Ui.OnMainLoop(changed));
    }

    private Func<string?> LaunchLog(LibraryEntry entry) =>
        () => new Library(layout, runner).LaunchLog(entry);

    private static string Told(Exception exception) =>
        exception is AggregateException many
            ? string.Join(" ", many.Flatten().InnerExceptions.Select(inner => inner.Message))
            : exception.Message;

    private void Uninstall(Removal removal) =>
        Task.Run(() => new Library(layout, runner).PossibleUninstallers(removal))
            .ContinueWith(found => Ui.OnMainLoop(() =>
            {
                if (found.IsFaulted)
                {
                    toast(found.Exception!.InnerException!.Message);
                }
                else if (found.Result.Count > 1)
                {
                    ChooseUninstaller(removal, found.Result);
                }
                else
                {
                    RemoveWhenReady(removal, takePrefix: false, null);
                }
            }));

    private void ChooseUninstaller(Removal removal, IReadOnlyList<UninstallEntry> possible)
    {
        var which = Adw.ComboRow.New();
        which.SetTitle("Uninstaller");
        which.SetModel(Gtk.StringList.New([.. possible.Select(one => one.Name)]));

        var fields = Adw.PreferencesGroup.New();
        fields.Add(which);

        Ui.Confirm(
            window,
            $"Which is {removal.Entry.Name}'s uninstaller?",
            $"Cabinet did not see which uninstaller {removal.Entry.Name} registered, and more "
            + "than one in its prefix could be it. Only the one you choose runs.",
            "Run",
            () => RemoveWhenReady(
                removal, takePrefix: false, possible[(int)which.GetSelected()]),
            Adw.ResponseAppearance.Destructive,
            fields);
    }

    private void Take(Removal removal) => RemoveWhenReady(removal, takePrefix: true, null);

    private void RemoveWhenReady(Removal removal, bool takePrefix, UninstallEntry? uninstaller)
    {
        var (entry, prefix) = (removal.Entry, removal.Prefix!);

        if (prefixIsChanging(prefix))
        {
            toast($"{prefix} is changing; try removing {entry.Name} again when it is done.");
            return;
        }

        operations.Run(
            takePrefix ? $"Deleting {prefix}" : $"Removing {entry.Name}",
            output => new Library(layout, runner)
                .Remove(removal, takePrefix, uninstaller, output),
            changed);
    }

    private sealed record Snapshot(
        IReadOnlyList<LibraryEntry> Entries,
        IReadOnlyDictionary<string, string?> Installed,
        IReadOnlyList<LibraryEntry> Retired);
}
