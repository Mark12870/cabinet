using Cabinet.Core;

namespace Cabinet.Gui;

internal sealed class AboutPage
{
    private readonly Layout layout;
    private readonly IProcessRunner runner;
    private readonly Gtk.Window window;
    private readonly Gtk.Box list = Gtk.Box.New(Gtk.Orientation.Vertical, 12);
    private readonly RefreshGeneration generation = new();

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
        var current = generation.Next();
        Ui.Clear(list);

        var cabinet = Group("Cabinet");
        var bundled = Group("Bundled");

        list.Append(Paths());

        Task.Run(() =>
        {
            try
            {
                var build = new About(layout, runner).Read();
                Ui.OnMainLoop(() =>
                {
                    if (generation.IsCurrent(current))
                    {
                        Fill(cabinet, bundled, build);
                    }
                });
            }
            catch (Exception exception)
            {
                Ui.OnMainLoop(() =>
                {
                    if (generation.IsCurrent(current))
                    {
                        cabinet.SetDescription(exception.Message);
                    }
                });
            }
        });
    }

    private void Fill(Adw.PreferencesGroup cabinet, Adw.PreferencesGroup bundled, Build build)
    {
        cabinet.Add(VersionRow(build));
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

    private Adw.ActionRow VersionRow(Build build)
    {
        var row = Row("Version", build.Version);
        var changelog = Gtk.Button.NewWithLabel("Changelog");
        changelog.SetValign(Gtk.Align.Center);
        changelog.OnClicked += (_, _) => Ui.Guard(ShowChangelog);
        row.AddSuffix(changelog);
        return row;
    }

    private void ShowChangelog()
    {
        var releases = Gtk.Box.New(Gtk.Orientation.Vertical, 24);

        foreach (var release in About.Changelog())
        {
            var group = Adw.PreferencesGroup.New();
            group.SetTitle(release.Version);
            group.SetDescription(release.Date);
            group.Add(Line(release.Headline));

            foreach (var change in release.Changes)
            {
                group.Add(Line($"• {change}"));
            }

            releases.Append(group);
        }

        var body = Ui.Page();
        body.Append(Ui.Scrolled(releases));

        var bars = Adw.ToolbarView.New();
        bars.AddTopBar(Adw.HeaderBar.New());
        bars.SetContent(body);

        var dialog = Adw.Dialog.New();
        dialog.SetTitle("Changelog");
        dialog.SetContentWidth(640);
        dialog.SetContentHeight(560);
        dialog.SetChild(bars);
        dialog.Present(window);
    }

    private static Adw.ActionRow Line(string text)
    {
        var row = Adw.ActionRow.New();
        row.SetUseMarkup(false);
        row.SetTitle(text);
        return row;
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
        row.OnActivated += (_, _) =>
            Ui.Guard(() => Ui.Observe(Gtk.UriLauncher.New(uri).LaunchAsync(window)));
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
