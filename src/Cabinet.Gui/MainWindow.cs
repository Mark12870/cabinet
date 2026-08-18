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

    public MainWindow(Adw.Application application, Layout layout, IProcessRunner runner)
    {
        this.layout = layout;
        this.runner = runner;

        window = Adw.ApplicationWindow.New(application);
        window.SetTitle("Cabinet");
        window.SetDefaultSize(920, 640);

        prefixes = new PrefixesPage(layout, runner, window);
        runners = new RunnersPage(layout, runner, window);
        doctor = new DoctorPage(layout);

        stack.AddTitledWithIcon(prefixes.Widget, "prefixes", "Prefixes", Icons.Prefixes);
        stack.AddTitledWithIcon(runners.Widget, "runners", "Runners", Icons.Runners);
        stack.AddTitledWithIcon(doctor.Widget, "doctor", "Doctor", Icons.Doctor);

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

        var create = Ui.IconButton(Icons.New, "New prefix");
        create.OnClicked += (_, _) => AskForName();
        header.PackStart(create);

        var available = Ui.IconButton(Icons.Download, "Wine versions");
        available.OnClicked += (_, _) => runners.ShowAvailable();
        header.PackStart(available);

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

                if (!result.Ok)
                {
                    throw new InvalidOperationException(result.Stderr.Trim());
                }
            },
            RefreshAll);

    private void AskForName()
    {
        Prompt("New prefix", "A name for the prefix, such as the plugin it will hold.", name =>
            prefixes.CreatePrefix(name, null));
    }

    private void AskForDaw()
    {
        Prompt("Enrol a DAW", "The Flatpak id of the DAW, such as fm.reaper.Reaper.", EnrolDaw);
    }

    private void EnrolDaw(string dawId)
    {
        string link;

        try
        {
            link = Enrolment.Link(dawId, layout);
        }
        catch (Exception exception)
        {
            Report("Could not enrol", exception.Message);
            return;
        }

        Report(
            $"Linked {link}",
            "Now run this yourself:\n\n"
            + Enrolment.OverrideCommand(dawId, layout)
            + "\n\nIt is not applied automatically: --talk-name=org.freedesktop.Flatpak lets "
            + $"{dawId} run commands on the host, which is yours to decide.");
    }

    private void Prompt(string heading, string body, Action<string> accepted)
    {
        var dialog = Adw.AlertDialog.New(heading, body);
        var entry = Gtk.Entry.New();
        entry.SetMarginTop(12);
        dialog.SetExtraChild(entry);

        dialog.AddResponse("cancel", "Cancel");
        dialog.AddResponse("ok", "Continue");
        dialog.SetResponseAppearance("ok", Adw.ResponseAppearance.Suggested);
        dialog.SetDefaultResponse("ok");
        dialog.SetCloseResponse("cancel");

        dialog.OnResponse += (_, args) =>
        {
            var text = entry.GetText().Trim();

            if (args.Response == "ok" && text.Length > 0)
            {
                accepted(text);
            }
        };

        dialog.Present(window);
    }

    private void Report(string heading, string body)
    {
        var dialog = Adw.AlertDialog.New(heading, body);
        dialog.AddResponse("ok", "Close");
        dialog.SetDefaultResponse("ok");
        dialog.SetCloseResponse("ok");
        dialog.Present(window);
    }
}
