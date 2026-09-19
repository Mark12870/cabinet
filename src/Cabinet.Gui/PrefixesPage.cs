using Cabinet.Core;

namespace Cabinet.Gui;

internal sealed class PrefixesPage
{
    private readonly Layout layout;
    private readonly IProcessRunner runner;
    private readonly Gtk.Window window;
    private readonly Adw.NavigationView navigation;
    private readonly Action changed;
    private readonly Action<string> toast;
    private readonly Operation operations;
    private readonly Gtk.Box list = Gtk.Box.New(Gtk.Orientation.Vertical, 12);
    private readonly HashSet<string> changing = new(StringComparer.Ordinal);

    private PrefixPage? open;
    private readonly RefreshGeneration generation = new();

    public PrefixesPage(
        Layout layout,
        IProcessRunner runner,
        Gtk.Window window,
        Adw.NavigationView navigation,
        Action changed,
        Action<string> toast,
        Operation operations)
    {
        this.layout = layout;
        this.runner = runner;
        this.window = window;
        this.navigation = navigation;
        this.changed = changed;
        this.toast = toast;
        this.operations = operations;

        navigation.OnPopped += (_, _) => Ui.Guard(() => open = null);

        var page = Ui.Page();
        page.Append(Ui.Scrolled(list));
        Widget = page;
    }

    public Gtk.Widget Widget { get; }

    public bool IsChanging(string name) => changing.Contains(name);

    public void Refresh()
    {
        var current = generation.Next();
        Task.Run(() => new Snapshot(
            new Prefixes(layout, runner).List(),
            new Runners(layout, runner).List().Select(found => found.Name).ToList()))
            .ContinueWith(task => Ui.OnMainLoop(() =>
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

                Show(task.Result);
            }));
    }

    private void Show(Snapshot snapshot)
    {
        Ui.Clear(list);
        var prefixes = snapshot.Prefixes;

        if (prefixes.Count == 0)
        {
            list.Append(Empty());
            Reopen(prefixes, snapshot.RunnerNames);
            return;
        }

        var group = Adw.PreferencesGroup.New();
        group.SetTitle("Prefixes");

        var create = Ui.RowButton(Icons.New, "New prefix");
        create.OnClicked += (_, _) => Ui.Guard(NewPrefix);
        group.SetHeaderSuffix(create);

        foreach (var prefix in prefixes)
        {
            group.Add(Row(prefix, snapshot.RunnerNames));
        }

        list.Append(group);
        Reopen(prefixes, snapshot.RunnerNames);
    }

    private void ShowFailure(string message)
    {
        Ui.Clear(list);
        var failed = Adw.StatusPage.New();
        failed.SetIconName(Icons.Fail);
        failed.SetTitle("Could not read prefixes");
        failed.SetDescription(message);
        list.Append(failed);
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
            open.Show(still, runnerNames, changing.Contains(still.Name));
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
        create.OnClicked += (_, _) => Ui.Guard(NewPrefix);
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
        enter.OnClicked += (_, _) => Ui.Guard(() => Open(prefix, runnerNames));
        row.AddSuffix(enter);
        row.SetActivatableWidget(enter);

        return row;
    }

    private void Open(Prefix prefix, IReadOnlyList<string> runnerNames)
    {
        var page = new PrefixPage(
            layout,
            runner,
            window,
            prefix.Name,
            changed,
            toast,
            () => BeginChange(prefix.Name),
            () => EndChange(prefix.Name),
            operations);
        page.Show(prefix, runnerNames, changing.Contains(prefix.Name));

        open = page;
        navigation.Push(page.Page);
    }

    private bool BeginChange(string name) => changing.Add(name);

    private void EndChange(string name) => changing.Remove(name);

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
        var name = Adw.EntryRow.New();
        name.SetTitle("Name");

        var choices = RunnerNames();
        var wine = Adw.ComboRow.New();
        wine.SetTitle("Wine");
        wine.SetSubtitle("The runner it will keep");
        wine.SetModel(Gtk.StringList.New([.. choices]));

        var fields = Adw.PreferencesGroup.New();
        fields.Add(name);
        fields.Add(wine);

        Ui.Confirm(
            window,
            "New prefix",
            "A name for the prefix, such as the plugin it will hold.",
            "Create",
            () =>
            {
                if (name.GetText().Trim() is { Length: > 0 } entered)
                {
                    CreatePrefix(entered, choices[(int)wine.GetSelected()]);
                }
            },
            extra: fields);
    }

    private void CreatePrefix(string name, string? runnerName) =>
        operations.Run(
            $"Creating {name}",
            output => new Prefixes(layout, runner).Create(name, runnerName, output),
            changed);

    private sealed record Snapshot(
        IReadOnlyList<Prefix> Prefixes,
        IReadOnlyList<string> RunnerNames);
}
