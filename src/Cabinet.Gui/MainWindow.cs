using Cabinet.Core;

namespace Cabinet.Gui;

internal sealed class MainWindow
{
    private readonly Layout layout;
    private readonly IProcessRunner runner;
    private readonly Adw.ApplicationWindow window;
    private readonly Adw.ViewStack stack = Adw.ViewStack.New();
    private readonly Adw.NavigationView navigation = Adw.NavigationView.New();

    private readonly PrefixesPage prefixes;
    private readonly LibraryPage library;
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

        prefixes = new PrefixesPage(layout, runner, window, navigation, RefreshAll);
        library = new LibraryPage(layout, runner, window, navigation, RefreshAll);
        runners = new RunnersPage(layout, runner, window, RefreshAll);
        doctor = new DoctorPage(layout, runner);
        about = new AboutPage(layout, runner, window);

        stack.AddTitledWithIcon(library.Widget, "library", "Library", Icons.Library);
        stack.AddTitledWithIcon(prefixes.Widget, "prefixes", "Prefixes", Icons.Prefixes);
        stack.AddTitledWithIcon(runners.Widget, "runners", "Runners", Icons.Runners);
        stack.AddTitledWithIcon(doctor.Widget, "doctor", "Doctor", Icons.Doctor);
        stack.AddTitledWithIcon(about.Widget, "about", "About", Icons.About);

        var view = Adw.ToolbarView.New();
        view.AddTopBar(Header());
        view.AddBottomBar(Switcher());
        view.SetContent(stack);

        navigation.Add(Adw.NavigationPage.New(view, "Cabinet"));
        window.SetContent(navigation);

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
        library.Refresh();
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

        new EnrolmentDialog(window, layout, dawId, link).Present();

        RefreshAll();
    }
}
