using Cabinet.Core;

namespace Cabinet.Gui;

internal sealed class AboutPage
{
    private readonly Layout layout;
    private readonly IProcessRunner runner;
    private readonly Gtk.Window window;
    private readonly Gtk.Box list = Gtk.Box.New(Gtk.Orientation.Vertical, 12);

    public AboutPage(Layout layout, IProcessRunner runner, Gtk.Window window)
    {
        this.layout = layout;
        this.runner = runner;
        this.window = window;

        var page = Ui.Page();
        page.Append(Ui.Scrolled(list));
        Widget = page;
    }

    public Gtk.Widget Widget { get; }

    public void Refresh()
    {
        Ui.Clear(list);

        var cabinet = Group("Cabinet");
        var bundled = Group("Bundled");

        list.Append(Paths());

        Task.Run(() =>
        {
            try
            {
                var build = new About(layout, runner).Read();
                Ui.OnMainLoop(() => Fill(cabinet, bundled, build));
            }
            catch (Exception exception)
            {
                Ui.OnMainLoop(() => cabinet.SetDescription(exception.Message));
            }
        });
    }

    private void Fill(Adw.PreferencesGroup cabinet, Adw.PreferencesGroup bundled, Build build)
    {
        cabinet.Add(Row("Version", build.Version));
        cabinet.Add(InstalledFrom(build));
        cabinet.Add(Row("Commit", Short(build.Commit)));

        bundled.Add(Row("yabridge", build.Yabridge));
        bundled.Add(Row("Wine", build.Wine));

        if (Project(build) is { } project)
        {
            list.Append(project);
        }
    }

    private Adw.PreferencesGroup Group(string title)
    {
        var group = Adw.PreferencesGroup.New();
        group.SetTitle(title);
        list.Append(group);
        return group;
    }

    private static Adw.ActionRow InstalledFrom(Build build)
    {
        var row = Row(
            "Installed from",
            build.Origin == Origin.Unknown ? build.Remote : $"{build.Remote}  ·  {build.Url}");

        var status = Gtk.Label.New(build.Origin switch
        {
            Origin.Published => "published build",
            Origin.Local => "local build",
            _ => "origin unknown",
        });

        status.AddCssClass(build.Origin switch
        {
            Origin.Published => "success",
            Origin.Local => "warning",
            _ => "dim-label",
        });

        status.SetValign(Gtk.Align.Center);
        row.AddSuffix(status);
        return row;
    }

    private Adw.PreferencesGroup Paths()
    {
        var group = Adw.PreferencesGroup.New();
        group.SetTitle("Paths");
        group.Add(Row("Prefixes", layout.PrefixesDir));
        group.Add(Row("Runners", layout.RunnersDir));
        group.Add(Row("Sockets", layout.SocketDir));
        group.Add(Row("yabridge", layout.HostYabridgeDir));
        return group;
    }

    private Adw.PreferencesGroup? Project(Build build)
    {
        if (build.Homepage is null && build.BugTracker is null)
        {
            return null;
        }

        var group = Adw.PreferencesGroup.New();
        group.SetTitle("Project");

        if (build.Homepage is { } homepage)
        {
            group.Add(LinkRow("Homepage", homepage));
        }

        if (build.BugTracker is { } tracker)
        {
            group.Add(LinkRow("Report an issue", tracker));
        }

        return group;
    }

    private Adw.ActionRow LinkRow(string title, string uri)
    {
        var row = Row(title, uri);
        row.AddSuffix(Gtk.Image.NewFromIconName(Icons.Link));
        row.SetActivatable(true);
        row.OnActivated += (_, _) => Gtk.UriLauncher.New(uri).LaunchAsync(window);
        return row;
    }

    private static Adw.ActionRow Row(string title, string subtitle)
    {
        var row = Adw.ActionRow.New();
        row.SetTitle(title);
        row.SetSubtitle(subtitle);
        row.SetSubtitleSelectable(true);
        return row;
    }

    private static string Short(string commit) =>
        commit.Length > 12 ? commit[..12] : commit;
}
