using Cabinet.Core;

namespace Cabinet.Gui;

internal sealed class MainWindow
{
    private readonly Layout layout;
    private readonly IProcessRunner runner;
    private readonly Adw.ApplicationWindow window;
    private readonly Adw.ViewStack stack = Adw.ViewStack.New();

    private readonly PrefixesPage prefixes;
    private readonly RunnersPage runners;
    private readonly DoctorPage doctor;
    private readonly AboutPage about;

    public MainWindow(Adw.Application application, Layout layout, IProcessRunner runner)
    {
        this.layout = layout;
        this.runner = runner;

        window = Adw.ApplicationWindow.New(application);
        window.SetTitle("Cabinet");
        window.SetDefaultSize(920, 640);

        prefixes = new PrefixesPage(layout, runner, window, RefreshAll);
        runners = new RunnersPage(layout, runner, window, RefreshAll);
        doctor = new DoctorPage(layout);
        about = new AboutPage(layout, runner, window);

        stack.AddTitledWithIcon(prefixes.Widget, "prefixes", "Prefixes", Icons.Prefixes);
        stack.AddTitledWithIcon(runners.Widget, "runners", "Runners", Icons.Runners);
        stack.AddTitledWithIcon(doctor.Widget, "doctor", "Doctor", Icons.Doctor);
        stack.AddTitledWithIcon(about.Widget, "about", "About", Icons.About);

        var view = Adw.ToolbarView.New();
        view.AddTopBar(Header());
        view.AddBottomBar(Switcher());
        view.SetContent(stack);
        window.SetContent(view);

        RefreshAll();
    }

    public void Present() => window.Present();

    private Adw.HeaderBar Header()
    {
        var header = Adw.HeaderBar.New();

        var refresh = Ui.IconButton(Icons.Refresh, "Look at everything again");
        refresh.OnClicked += (_, _) => RefreshAll();
        header.PackEnd(refresh);

        var sync = Ui.IconButton(Icons.Sync, "Bridge what is installed");
        sync.OnClicked += (_, _) => Sync();
        header.PackEnd(sync);

        var enrol = Ui.IconButton(Icons.Enrol, "Enrol a DAW");
        enrol.OnClicked += (_, _) => AskForDaw();
        header.PackEnd(enrol);

        return header;
    }

    private Adw.ViewSwitcherBar Switcher()
    {
        var switcher = Adw.ViewSwitcherBar.New();
        switcher.SetStack(stack);
        switcher.SetReveal(true);
        return switcher;
    }

    private void RefreshAll()
    {
        prefixes.Refresh();
        runners.Refresh();
        doctor.Refresh();
        about.Refresh();
    }

    private void Sync() =>
        Operation.Run(
            window,
            "Bridging plugins",
            output =>
            {
                var found = new Prefixes(layout, runner).List();
                var result = new Yabridgectl(layout, runner).SyncPrefixes(found);

                foreach (var line in result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    output(line);
                }

                Operation.Ensure(result, "yabridgectl");
            },
            RefreshAll);

    private void AskForDaw() =>
        Ui.Prompt(
            window,
            "Enrol a DAW",
            "The Flatpak id of the DAW it should bridge plugins into.",
            "fm.reaper.Reaper",
            EnrolDaw);

    private void EnrolDaw(string dawId)
    {
        string link;

        try
        {
            link = Enrolment.Link(dawId, layout);
        }
        catch (Exception exception)
        {
            Ui.Report(window, "Could not enrol", exception.Message);
            return;
        }

        Ui.Report(
            window,
            $"Linked {link}",
            "Now run this yourself:\n\n"
            + Enrolment.OverrideCommand(dawId, layout)
            + "\n\nIt is not applied automatically: --talk-name=org.freedesktop.Flatpak lets "
            + $"{dawId} run commands on the host, which is yours to decide.\n\n"
            + "Then check the shim loads inside that DAW's runtime, which is older than the "
            + "one it was built against on some DAWs:\n\n"
            + Enrolment.SelfTestCommand(dawId, layout));

        RefreshAll();
    }
}
