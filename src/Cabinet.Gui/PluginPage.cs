using Cabinet.Core;

namespace Cabinet.Gui;

internal sealed class PluginPage
{
    private readonly Layout layout;
    private readonly Action<LibraryEntry> install;
    private readonly Action<LibraryEntry> remove;
    private readonly Gtk.Window window;
    private readonly Gtk.Box body = Gtk.Box.New(Gtk.Orientation.Vertical, 18);

    public PluginPage(
        Layout layout,
        Gtk.Window window,
        LibraryEntry entry,
        Action<LibraryEntry> install,
        Action<LibraryEntry> remove)
    {
        this.layout = layout;
        this.window = window;
        this.install = install;
        this.remove = remove;
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

    public void Show(LibraryEntry entry, string? prefix, bool installed)
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
        body.Append(Act(entry, installed));
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
            visit.OnClicked += (_, _) => Gtk.UriLauncher.New(homepage).LaunchAsync(window);
            row.AddSuffix(visit);
            row.SetActivatableWidget(visit);

            group.Add(row);
        }

        return group;
    }

    private Gtk.Widget Act(LibraryEntry entry, bool installed)
    {
        var removable = installed && entry.Kind == PluginKind.Native;

        var button = Gtk.Button.NewWithLabel(removable
            ? $"Remove {entry.Name}"
            : installed ? "Install again" : $"Install {entry.Name}");

        button.SetHalign(Gtk.Align.Center);
        button.AddCssClass("pill");
        button.AddCssClass(removable ? "destructive-action" : "suggested-action");
        button.OnClicked += (_, _) =>
        {
            if (removable)
            {
                remove(entry);
            }
            else
            {
                install(entry);
            }
        };

        return button;
    }

    private static void Add(Adw.PreferencesGroup group, string title, string? value)
    {
        if (value is null)
        {
            return;
        }

        var row = Adw.ActionRow.New();
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

    private static string Bridged(LibraryEntry entry)
    {
        var costs = new List<string> { "Under Wine, bridged" };

        if (entry.Runner is { } wine)
        {
            costs.Add($"Wine {wine}");
        }

        if (entry.Dxvk)
        {
            costs.Add("DXVK");
        }

        if (entry.Sync != SyncMode.System)
        {
            costs.Add(PrefixSettings.Word(entry.Sync));
        }

        return string.Join("  ·  ", costs);
    }
}
