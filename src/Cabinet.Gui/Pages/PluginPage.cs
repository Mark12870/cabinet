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
    private readonly Func<LibraryEntry, string?> log;
    private readonly Gtk.Window window;
    private readonly Gtk.Box body = Gtk.Box.New(Gtk.Orientation.Vertical, 18);

    public PluginPage(
        Layout layout,
        Gtk.Window window,
        LibraryEntry entry,
        Action<LibraryEntry> install,
        Action<LibraryEntry> remove,
        Action<LibraryEntry> launch,
        Action<LibraryEntry> stop,
        Action<PrefixUpdate> update,
        Func<LibraryEntry, string?> log)
    {
        this.layout = layout;
        this.window = window;
        this.install = install;
        this.remove = remove;
        this.launch = launch;
        this.stop = stop;
        this.update = update;
        this.log = log;
        Id = entry.Id;

        var content = Ui.Page();
        content.Append(Ui.Scrolled(body));

        var view = Adw.ToolbarView.New();
        view.AddTopBar(Adw.HeaderBar.New());
        view.SetContent(content);

        Page = Adw.NavigationPage.New(view, entry.Name);
    }

    public string Id { get; }

    public Adw.NavigationPage Page { get; }

    public void Show(
        LibraryEntry entry,
        string? prefix,
        bool installed,
        bool running,
        PrefixUpdate? prefixUpdate = null,
        bool updateBlocked = false)
    {
        Ui.Clear(body);

        body.Append(Heading(entry));

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

        body.Append(Details(entry, prefix, installed));

        if (prefixUpdate is { Available: true })
        {
            body.Append(PrefixSetup(prefixUpdate, updateBlocked));
        }

        body.Append(Act(entry, installed, running));
    }

    private Gtk.Widget PrefixSetup(PrefixUpdate plan, bool blocked)
    {
        var group = Adw.PreferencesGroup.New();
        group.SetTitle(plan.Title);
        group.SetDescription($"Review the setup of prefix {plan.Prefix} before updating it.");
        Add(group, "Installed version", plan.Software);

        var config = Adw.ActionRow.New();
        config.SetUseMarkup(false);
        config.SetTitle("Config");
        config.SetSubtitle(plan.Config);

        var button = Gtk.Button.NewWithLabel("Update prefix…");
        button.SetValign(Gtk.Align.Center);
        button.AddCssClass("suggested-action");
        button.SetSensitive(!blocked);
        button.SetTooltipText(blocked
            ? "Close apps using this prefix and wait for ongoing operations to finish."
            : "Apply the reviewed dependencies and prefix settings.");
        button.OnClicked += (_, _) => Ui.Guard(() => update(plan));
        config.AddSuffix(button);
        config.SetActivatableWidget(button);
        group.Add(config);

        Add(group, "Applied config", plan.Applied);
        Add(group, "Shares this config", plan.Members.Count > 1 ? string.Join(", ", plan.Members) : null);
        Add(group, "Changes", plan.Changes.Count > 0 ? string.Join("\n", plan.Changes) : null);
        Add(group, "Kept", plan.Preserved.Count > 0 ? string.Join("\n", plan.Preserved) : null);
        Add(group, "Other plugins in this prefix", plan.Sharing.Count > 0 ? string.Join(", ", plan.Sharing) : null);
        return group;
    }

    private Gtk.Widget Heading(LibraryEntry entry)
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

        row.Append(titles);
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

    private Gtk.Widget Details(LibraryEntry entry, string? prefix, bool installed)
    {
        var group = Adw.PreferencesGroup.New();
        group.SetTitle("Details");

        Add(group, "Developer", entry.Developer);
        Add(group, "Version", entry.Version);
        Add(group, "Licence", entry.Licence);
        Add(group, "Licensing", entry.Licensing);
        Add(group, "Formats", entry.Formats.Count > 0 ? string.Join(", ", entry.Formats) : null);
        Add(group, "Runs", entry.Kind == PluginKind.Native ? "Natively on Linux" : Bridged(entry));
        Add(group, "Presets", entry.Data is { } data ? "~/" + data : null);
        Add(group, "Installed", installed ? prefix is null ? "Yes" : $"In prefix {prefix}" : null);

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

    private Gtk.Widget Act(LibraryEntry entry, bool installed, bool running)
    {
        var row = Gtk.Box.New(Gtk.Orientation.Horizontal, 12);
        row.SetHalign(Gtk.Align.Center);

        if (!installed)
        {
            row.Append(Pill($"Install {entry.Name}", "suggested-action", () => install(entry)));
            return row;
        }

        if (entry.Launch is not null)
        {
            var open = Pill(
                running ? "Running" : $"Open {entry.Name}",
                "suggested-action",
                () => launch(entry));

            open.SetSensitive(!running);
            row.Append(open);

            if (running)
            {
                row.Append(Pill(
                    $"Stop {entry.Name}", "destructive-action", () => stop(entry)));
            }
        }

        if (entry.Kind == PluginKind.Windows)
        {
            row.Append(Pill("Reinstall", null, () => install(entry)));
        }

        if (log(entry) is { } written)
        {
            row.Append(Pill(
                "Logs", null, () => Ui.Log(window, "Cabinet, plugin host and yabridge logs", log(entry) ?? written)));
        }

        row.Append(Pill($"Remove {entry.Name}", "destructive-action", () => remove(entry)));

        return row;
    }

    private static Gtk.Button Pill(string label, string? appearance, Action clicked)
    {
        var button = Gtk.Button.NewWithLabel(label);
        button.AddCssClass("pill");
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
