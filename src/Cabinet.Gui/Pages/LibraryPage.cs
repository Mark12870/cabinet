using Cabinet.Core;

namespace Cabinet.Gui;

internal sealed partial class LibraryPage
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
        Gtk.DropDown.NewFromStrings(["Any state", "Installed", "Not installed", "Prefix updates"]);

    private readonly HashSet<string> running = new(StringComparer.Ordinal);
    private readonly HashSet<string> stopping = new(StringComparer.Ordinal);
    private IReadOnlySet<string> opened = new HashSet<string>(StringComparer.Ordinal);

    private IReadOnlyList<LibraryEntry> entries = [];
    private IReadOnlyList<LibraryEntry> retired = [];
    private IReadOnlyDictionary<string, string?> installed =
        new Dictionary<string, string?>(StringComparer.Ordinal);

    private IReadOnlyDictionary<string, PrefixUpdate> updates =
        new Dictionary<string, PrefixUpdate>(StringComparer.Ordinal);

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
            return new Snapshot(
                library.Entries(), library.Installed(), library.Retired(), library.Opened(),
                library.PrefixUpdates());
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
            opened = task.Result.Opened;
            updates = task.Result.Updates;
            stopping.RemoveWhere(id => !Running(id));
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

    public void RefreshOpened() =>
        Task.Run(() => new Library(layout, runner).Opened()).ContinueWith(found =>
            Ui.OnMainLoop(() =>
            {
                if (found.IsFaulted || found.Result.SetEquals(opened))
                {
                    return;
                }

                opened = found.Result;
                stopping.RemoveWhere(id => !Running(id));
                Rebuild();
            }));

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
        row.Append(Narrowing(states, "State"));

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
            .Where(entry => states.GetSelected() != 3 || updates.ContainsKey(entry.Id))
            .ToList();

        if (states.GetSelected() == 3 && updates.Count > 0)
        {
            list.Append(UpdateAll());
        }

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

        var gone = retired.Where(entry => states.GetSelected() != 3 && filter.Matches(entry, true))
            .ToList();
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
            Running(still.Id),
            updates.GetValueOrDefault(still.Id),
            UpdateBlocked(updates.GetValueOrDefault(still.Id)));
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
            ConfirmUpdate,
            one => LaunchLog(one)());
        page.Show(entry, prefix, here, Running(entry.Id), updates.GetValueOrDefault(entry.Id),
            UpdateBlocked(updates.GetValueOrDefault(entry.Id)));

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
        row.SetSubtitleLines(1);
        row.AddPrefix(RowIcon(entry, here));
        row.AddSuffix(Tags(entry, here, prefix));

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
        if (Running(entry.Id))
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

    private Gtk.Box Tags(LibraryEntry entry, bool here, string? prefix)
    {
        var tags = Gtk.Box.New(Gtk.Orientation.Horizontal, 6);
        tags.SetValign(Gtk.Align.Center);
        tags.Append(Ui.Tag(entry.Category));
        tags.Append(Ui.Tag(entry.Kind == PluginKind.Native ? "Linux" : "Windows"));
        tags.Append(Ui.Tag(entry.Licence == "Commercial" ? "Paid" : "Free"));

        if (Obtained(entry) is { } obtained)
        {
            tags.Append(Ui.Tag(obtained));
        }

        if (updates.ContainsKey(entry.Id))
        {
            tags.Append(Ui.Tag("Update", "warning"));
        }

        if (here)
        {
            tags.Append(Ui.Tag(prefix is null ? "Installed" : $"Installed in {prefix}", "success"));
        }

        return tags;
    }

    private static string? Obtained(LibraryEntry entry) =>
        entry.DemoUrl is not null ? "Demo"
        : entry.Source != PluginSource.Byo ? null
        : entry.Account is null ? "Your installer"
        : "Account download";

    private static string Subtitle(LibraryEntry entry) =>
        string.Join("  ·  ", new[] { entry.Developer, entry.Summary }.Where(part => part is { Length: > 0 }));

    private bool Running(string id) => running.Contains(id) || opened.Contains(id);

    private sealed record Snapshot(
        IReadOnlyList<LibraryEntry> Entries,
        IReadOnlyDictionary<string, string?> Installed,
        IReadOnlyList<LibraryEntry> Retired,
        IReadOnlySet<string> Opened,
        IReadOnlyDictionary<string, PrefixUpdate> Updates);
}
