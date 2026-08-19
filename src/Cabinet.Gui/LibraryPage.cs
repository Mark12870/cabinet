using Cabinet.Core;

namespace Cabinet.Gui;

internal sealed class LibraryPage
{
    private readonly Layout layout;
    private readonly IProcessRunner runner;
    private readonly Gtk.Window window;
    private readonly Action changed;
    private readonly Gtk.Box list = Gtk.Box.New(Gtk.Orientation.Vertical, 12);

    public LibraryPage(Layout layout, IProcessRunner runner, Gtk.Window window, Action changed)
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

        var library = new Library(layout, runner);
        var entries = library.Entries();

        if (entries.Count == 0)
        {
            list.Append(Empty());
            return;
        }

        var installed = library.InstalledNative().ToHashSet(StringComparer.Ordinal);

        Section("Windows plugins", "Each one gets a Wine prefix, bridged into your DAW.",
            entries.Where(entry => entry.Kind == PluginKind.Windows), installed);

        Section("Linux plugins", "VST3, CLAP and LV2, in Cabinet's own directory and linked out.",
            entries.Where(entry => entry.Kind == PluginKind.Native), installed);
    }

    private void Section(
        string title,
        string description,
        IEnumerable<LibraryEntry> entries,
        IReadOnlySet<string> installed)
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
            group.Add(Row(entry, installed.Contains(entry.Id)));
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

    private Adw.ActionRow Row(LibraryEntry entry, bool installed)
    {
        var row = Adw.ActionRow.New();
        row.SetTitle(entry.Name);
        row.SetSubtitle(Subtitle(entry, installed));
        row.AddPrefix(Gtk.Image.NewFromIconName(Icons.Prefixes));

        if (entry.Homepage is { } homepage)
        {
            var visit = Ui.RowButton(Icons.Link, $"{entry.Name} on the web");
            visit.OnClicked += (_, _) => Gtk.UriLauncher.New(homepage).LaunchAsync(window);
            row.AddSuffix(visit);
        }

        var act = installed
            ? Ui.RowButton(Icons.Delete, $"Remove {entry.Name}", destructive: true)
            : Ui.RowButton(Icons.Download, $"Install {entry.Name}");

        act.OnClicked += (_, _) =>
        {
            if (installed)
            {
                ConfirmRemove(entry);
            }
            else
            {
                Begin(entry);
            }
        };

        row.AddSuffix(act);
        row.SetActivatableWidget(act);

        return row;
    }

    private static string Subtitle(LibraryEntry entry, bool installed)
    {
        var state = entry.Summary.Length > 0 ? $"{entry.Category}  ·  {entry.Summary}"
            : entry.Category;

        if (entry.Source == PluginSource.Byo)
        {
            state += "  ·  needs the installer you bought";
        }

        return installed ? $"{state}  ·  installed" : state;
    }

    private void Begin(LibraryEntry entry)
    {
        if (entry.Kind == PluginKind.Native)
        {
            Start(entry, null, null);
            return;
        }

        AskForPrefix(entry);
    }

    private void AskForPrefix(LibraryEntry entry)
    {
        var dialog = Adw.AlertDialog.New(
            $"Install {entry.Name}",
            entry.Source == PluginSource.Byo
                ? $"{entry.Name} cannot be downloaded, so you will be asked for the installer "
                  + "you already have."
                : "A prefix of its own keeps this plugin's dependencies away from every other.");

        var existing = new Prefixes(layout, runner).List().Select(one => one.Name).ToList();
        List<string> choices = ["New prefix", .. existing];

        var where = Adw.ComboRow.New();
        where.SetTitle("Prefix");
        where.SetModel(Gtk.StringList.New([.. choices]));

        var name = Adw.EntryRow.New();
        name.SetTitle("Name");
        name.SetText(entry.Prefix);

        where.OnNotify += (_, args) =>
        {
            if (args.Pspec.GetName() == "selected")
            {
                name.SetVisible(where.GetSelected() == 0);
            }
        };

        var fields = Adw.PreferencesGroup.New();
        fields.SetMarginTop(12);
        fields.Add(where);
        fields.Add(name);
        dialog.SetExtraChild(fields);

        dialog.AddResponse("cancel", "Cancel");
        dialog.AddResponse("ok", "Install");
        dialog.SetResponseAppearance("ok", Adw.ResponseAppearance.Suggested);
        dialog.SetDefaultResponse("ok");
        dialog.SetCloseResponse("cancel");

        dialog.OnResponse += (_, args) =>
        {
            if (args.Response != "ok")
            {
                return;
            }

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
        };

        dialog.Present(window);
    }

    private void Start(LibraryEntry entry, string? prefix, string? installer) =>
        Operation.Run(
            window,
            $"Installing {entry.Name}",
            output => new Library(layout, runner).Install(entry, prefix, installer, output),
            changed);

    private void ConfirmRemove(LibraryEntry entry)
    {
        var dialog = Adw.AlertDialog.New(
            $"Remove {entry.Name}?",
            "Its files and the links your DAW scans are deleted. Presets you saved elsewhere "
            + "are left alone.");

        dialog.AddResponse("cancel", "Cancel");
        dialog.AddResponse("delete", "Remove");
        dialog.SetResponseAppearance("delete", Adw.ResponseAppearance.Destructive);
        dialog.SetDefaultResponse("cancel");
        dialog.SetCloseResponse("cancel");

        dialog.OnResponse += (_, args) =>
        {
            if (args.Response == "delete")
            {
                Operation.Run(
                    window,
                    $"Removing {entry.Name}",
                    output => new Library(layout, runner).RemoveNative(entry.Id, output),
                    changed);
            }
        };

        dialog.Present(window);
    }
}
