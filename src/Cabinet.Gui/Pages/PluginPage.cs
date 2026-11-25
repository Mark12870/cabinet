using Cabinet.Core;

namespace Cabinet.Gui;

internal sealed class PluginPage
{
    private readonly Layout layout;
    private readonly Action<LibraryEntry> install;
    private readonly Action<LibraryEntry> remove;
    private readonly Action<LibraryEntry> launch;
    private readonly Action<LibraryEntry> stop;
    private readonly Action<PrefixUpdate> update;
    private readonly Action<PrefixUpdate> restore;
    private readonly Action<string> openPrefix;
    private readonly Func<LibraryEntry, string?> log;
    private readonly Action<LibraryEntry> showPlugins;
    private readonly Gtk.Window window;
    private readonly Gtk.Box body = Ui.Page();

    public PluginPage(
        Layout layout,
        Gtk.Window window,
        LibraryEntry entry,
        Action<LibraryEntry> install,
        Action<LibraryEntry> remove,
        Action<LibraryEntry> launch,
        Action<LibraryEntry> stop,
        Action<PrefixUpdate> update,
        Action<PrefixUpdate> restore,
        Action<string> openPrefix,
        Func<LibraryEntry, string?> log,
        Action<LibraryEntry> showPlugins)
    {
        this.layout = layout;
        this.window = window;
        this.install = install;
        this.remove = remove;
        this.launch = launch;
        this.stop = stop;
        this.update = update;
        this.restore = restore;
        this.openPrefix = openPrefix;
        this.log = log;
        this.showPlugins = showPlugins;
        Id = entry.Id;

        var view = Ui.Bars(Ui.Scrolled(body));

        Page = Adw.NavigationPage.New(view, entry.Name);
    }

    public string Id { get; }

    public Adw.NavigationPage Page { get; }

    public void Show(
        LibraryEntry entry,
        bool installed,
        bool running,
        PrefixUpdate? prefixUpdate = null,
        bool updateBlocked = false)
    {
        Ui.Clear(body);

        body.Append(Heading(entry, installed, running));

        if (installed)
        {
            body.Append(Manage(entry));
        }

        if (layout.LibraryScreenshot(entry.Vendor, entry.Id) is { } screenshot)
        {
            body.Append(Screenshot(screenshot));
        }

        foreach (var paragraph in entry.Description.Count > 0
                     ? entry.Description
                     : (IReadOnlyList<string>)[entry.Summary])
        {
            body.Append(Paragraph(paragraph));
        }

        body.Append(Details(entry));

        if (prefixUpdate is not null)
        {
            body.Append(PrefixSetup(prefixUpdate, updateBlocked));
        }
    }

    private Gtk.Widget PrefixSetup(PrefixUpdate plan, bool blocked)
    {
        var group = Adw.PreferencesGroup.New();
        group.SetTitle("Prefix");

        var where = Adw.ActionRow.New();
        where.SetUseMarkup(false);
        where.SetTitle("Installed in");
        where.SetSubtitle(plan.Prefix);
        var enter = Ui.RowButton(Icons.Forward, $"Open prefix {plan.Prefix}");
        enter.OnClicked += (_, _) => Ui.Guard(() => openPrefix(plan.Prefix));
        where.AddSuffix(enter);
        where.SetActivatableWidget(enter);
        group.Add(where);

        Add(group, "Installed version", plan.Software);

        var config = Adw.ActionRow.New();
        config.SetUseMarkup(false);
        config.SetTitle("Config");
        config.SetSubtitle(plan.ConfigState);

        var state = Ui.Tag(plan.State, plan.Available || plan.Edits.Count > 0 ? "warning" : "success");
        state.SetValign(Gtk.Align.Center);
        state.SetMarginEnd(6);
        config.AddSuffix(state);

        if (plan.Available)
        {
            config.AddSuffix(ConfigButton(
                "Update prefix…", "Apply the reviewed dependencies and prefix settings.", blocked, () => update(plan)));
        }
        else if (plan.Edits.Count > 0)
        {
            config.AddSuffix(ConfigButton(
                "Restore config…", "Put the edited settings back to this config.", blocked, () => restore(plan)));
        }

        group.Add(config);

        Add(group, PrefixUpdate.DiffersFromConfig, plan.Edits.Count > 0 ? string.Join("\n", plan.Edits) : null);
        Add(group, "Shares this config", plan.Members.Count > 1 ? string.Join(", ", plan.Members) : null);
        Add(group, "Changes", plan.Changes.Count > 0 ? string.Join("\n", plan.Changes) : null);
        Add(group, PrefixUpdate.ReplacesYours, plan.Resets.Count > 0 ? string.Join("\n", plan.Resets) : null);
        Add(group, "Other plugins in this prefix", plan.Sharing.Count > 0 ? string.Join(", ", plan.Sharing) : null);
        return group;
    }

    private static Gtk.Button ConfigButton(string label, string tooltip, bool blocked, Action clicked)
    {
        var button = Gtk.Button.NewWithLabel(label);
        button.SetValign(Gtk.Align.Center);
        button.SetSensitive(!blocked);
        button.SetTooltipText(blocked
            ? "Close apps using this prefix and wait for ongoing operations to finish."
            : tooltip);
        button.OnClicked += (_, _) => Ui.Guard(clicked);
        return button;
    }

    private Gtk.Widget Heading(LibraryEntry entry, bool installed, bool running)
    {
        var row = Gtk.Box.New(Gtk.Orientation.Horizontal, 18);
        row.Append(Icon(entry));

        var titles = Gtk.Box.New(Gtk.Orientation.Vertical, 4);
        titles.SetValign(Gtk.Align.Center);

        var name = Gtk.Label.New(entry.Name);
        name.AddCssClass("title-1");
        name.SetXalign(0);
        titles.Append(name);

        var under = Gtk.Label.New(Under(entry));
        under.AddCssClass("dim-label");
        under.SetXalign(0);
        under.SetWrap(true);
        titles.Append(under);

        titles.SetHexpand(true);
        row.Append(titles);
        row.Append(Primary(entry, installed, running));
        return row;
    }

    private Gtk.Widget Icon(LibraryEntry entry)
    {
        var file = layout.LibraryLogo(entry.Vendor);

        if (file is null)
        {
            var fallback = Gtk.Image.NewFromIconName(Icons.Vst);
            fallback.SetPixelSize(64);
            fallback.SetValign(Gtk.Align.Center);
            return fallback;
        }

        var picture = Gtk.Picture.NewForFilename(file);
        picture.SetSizeRequest(96, 96);
        picture.SetValign(Gtk.Align.Center);
        picture.SetContentFit(Gtk.ContentFit.Contain);
        return picture;
    }

    private static Gtk.Widget Screenshot(string file)
    {
        var picture = Gtk.Picture.NewForFilename(file);
        picture.SetContentFit(Gtk.ContentFit.Contain);
        picture.SetCanShrink(true);
        picture.SetSizeRequest(-1, 300);
        return picture;
    }

    private static Gtk.Widget Paragraph(string text)
    {
        var label = Gtk.Label.New(text);
        label.SetXalign(0);
        label.SetWrap(true);
        label.SetWrapMode(Pango.WrapMode.WordChar);
        label.SetSelectable(true);
        label.SetCanFocus(false);
        return label;
    }

    private Gtk.Widget Details(LibraryEntry entry)
    {
        var group = Adw.PreferencesGroup.New();
        group.SetTitle("Details");

        Add(group, "Developer", entry.Developer);
        Add(group, "Version", entry.Version);
        Add(group, "Category", entry.Category);
        Add(group, "Licence", entry.Licence);
        Add(group, "Licensing", entry.Licensing);
        Add(group, "Account", entry.Account);
        Add(group, "Formats", entry.Formats.Count > 0 ? string.Join(", ", entry.Formats) : null);
        Add(group, "Runs", entry.Kind == PluginKind.Native ? "Natively on Linux" : Bridged(entry));
        Add(group, "Presets", entry.Data is { } data ? "~/" + data : null);

        if (entry.Homepage is { } homepage)
        {
            var row = Adw.ActionRow.New();
            row.SetTitle("Website");
            row.SetSubtitle(new Uri(homepage).Host);

            var visit = Ui.RowButton(Icons.Link, $"{entry.Name} on the web");
            visit.OnClicked += (_, _) =>
                Ui.Guard(() => Ui.Observe(Gtk.UriLauncher.New(homepage).LaunchAsync(window)));
            row.AddSuffix(visit);
            row.SetActivatableWidget(visit);

            group.Add(row);
        }

        return group;
    }

    private Gtk.Widget Primary(LibraryEntry entry, bool installed, bool running)
    {
        var buttons = Gtk.Box.New(Gtk.Orientation.Horizontal, 12);
        buttons.SetValign(Gtk.Align.Center);

        if (!installed)
        {
            buttons.Append(Pill("Install", "suggested-action", () => install(entry)));
            return buttons;
        }

        if (entry.Launch is null)
        {
            return buttons;
        }

        if (running)
        {
            buttons.Append(Pill("Stop", "destructive-action", () => stop(entry)));
        }

        buttons.Append(Pill(running ? "Show" : "Open", "suggested-action", () => launch(entry)));
        return buttons;
    }

    private Gtk.Widget Manage(LibraryEntry entry)
    {
        var manage = new List<Gtk.Widget>();

        if (entry.Launch is not null)
        {
            manage.Add(Plain("Installed Plugins", null, () => showPlugins(entry)));
        }

        if (log(entry) is { } written)
        {
            manage.Add(Plain(
                "Logs", null, () => Ui.Log(window, "Cabinet, plugin host and yabridge logs", log(entry) ?? written)));
        }

        manage.Add(More(entry));

        var row = Row([.. manage]);
        row.SetMarginBottom(12);
        return row;
    }

    private Gtk.MenuButton More(LibraryEntry entry)
    {
        var items = Gtk.Box.New(Gtk.Orientation.Vertical, 0);
        var popover = Gtk.Popover.New();
        popover.AddCssClass("menu");
        popover.SetChild(items);

        if (entry.Kind == PluginKind.Windows)
        {
            items.Append(Item(popover, "Reinstall", null, () => install(entry)));
        }

        items.Append(Item(popover, $"Remove {entry.Name}", "error", () => remove(entry)));

        var more = Gtk.MenuButton.New();
        more.SetIconName(Icons.More);
        more.SetTooltipText("More");
        more.SetPopover(popover);
        return more;
    }

    private static Gtk.Button Item(Gtk.Popover popover, string label, string? appearance, Action clicked)
    {
        var item = Plain(label, appearance, () =>
        {
            popover.Popdown();
            clicked();
        });
        item.AddCssClass("flat");
        ((Gtk.Label)item.GetChild()!).SetXalign(0);
        return item;
    }

    private static Gtk.Box Row(params Gtk.Widget[] buttons)
    {
        var row = Gtk.Box.New(Gtk.Orientation.Horizontal, 12);
        row.SetHalign(Gtk.Align.Start);

        foreach (var button in buttons)
        {
            row.Append(button);
        }

        return row;
    }

    private static Gtk.Button Pill(string label, string? appearance, Action clicked)
    {
        var button = Plain(label, appearance, clicked);
        button.AddCssClass("pill");
        return button;
    }

    private static Gtk.Button Plain(string label, string? appearance, Action clicked)
    {
        var button = Gtk.Button.NewWithLabel(label);
        button.OnClicked += (_, _) => Ui.Guard(clicked);

        if (appearance is not null)
        {
            button.AddCssClass(appearance);
        }

        return button;
    }

    private static void Add(Adw.PreferencesGroup group, string title, string? value)
    {
        if (value is null)
        {
            return;
        }

        var row = Adw.ActionRow.New();
        row.SetUseMarkup(false);
        row.SetTitle(title);
        row.SetSubtitle(value);
        group.Add(row);
    }

    private static string Under(LibraryEntry entry)
    {
        var parts = new List<string>();

        if (entry.Developer is { } developer)
        {
            parts.Add(developer);
        }

        parts.Add(entry.Category);

        if (entry.Version is { } version)
        {
            parts.Add(version);
        }

        return string.Join("  ·  ", parts);
    }

    private static string Bridged(LibraryEntry entry) =>
        string.Join("  ·  ", ["Under Wine, bridged", .. entry.Requirements()]);
}
