using Cabinet.Core;

namespace Cabinet.Gui;

internal sealed class PrefixesPage
{
    private readonly Layout layout;
    private readonly IProcessRunner runner;
    private readonly Gtk.Window window;
    private readonly Adw.NavigationView navigation;
    private readonly Action changed;
    private readonly Gtk.Box list = Gtk.Box.New(Gtk.Orientation.Vertical, 12);

    private PrefixPage? open;

    public PrefixesPage(
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

        var prefixes = new Prefixes(layout, runner).List();

        if (prefixes.Count == 0)
        {
            list.Append(Empty());
            Reopen(prefixes, []);
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
        Reopen(prefixes, names);
    }

    private void Reopen(IReadOnlyList<Prefix> prefixes, IReadOnlyList<string> runnerNames)
    {
        if (open is null)
        {
            return;
        }

        var still = prefixes.FirstOrDefault(one => one.Name == open.Name);

        if (still is null)
        {
            navigation.Pop();
        }
        else
        {
            open.Show(still, runnerNames);
        }
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

    private Adw.ActionRow Row(Prefix prefix, IReadOnlyList<string> runnerNames)
    {
        var row = Adw.ActionRow.New();
        row.SetTitle(prefix.Name);
        row.SetSubtitle(Subtitle(prefix));
        row.AddPrefix(Gtk.Image.NewFromIconName(Icons.Prefixes));

        var enter = Ui.RowButton(Icons.Forward, $"Open {prefix.Name}");
        enter.OnClicked += (_, _) => Open(prefix, runnerNames);
        row.AddSuffix(enter);
        row.SetActivatableWidget(enter);

        return row;
    }

    private void Open(Prefix prefix, IReadOnlyList<string> runnerNames)
    {
        var page = new PrefixPage(layout, runner, window, prefix.Name, changed);
        page.Show(prefix, runnerNames);

        open = page;
        navigation.Push(page.Page);
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
}
