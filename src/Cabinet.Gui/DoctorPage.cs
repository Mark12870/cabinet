using Cabinet.Core;

namespace Cabinet.Gui;

internal sealed class DoctorPage
{
    private readonly Layout layout;
    private readonly IProcessRunner runner;
    private readonly Gtk.Window window;
    private readonly Action changed;
    private readonly Gtk.Box list = Gtk.Box.New(Gtk.Orientation.Vertical, 12);

    public DoctorPage(Layout layout, IProcessRunner runner, Gtk.Window window, Action changed)
    {
        this.layout = layout;
        this.runner = runner;
        this.window = window;
        this.changed = changed;

        var page = Ui.Page();
        page.Append(Ui.Scrolled(list));
        Widget = page;
    }

    public Gtk.Widget Widget { get; }

    public void Refresh()
    {
        Ui.Clear(list);

        var group = Adw.PreferencesGroup.New();
        group.SetTitle("Checks");
        list.Append(group);
        list.Append(Repair());

        Task.Run(() =>
        {
            try
            {
                var checks = new Doctor(layout, runner).Run();
                Ui.OnMainLoop(() =>
                {
                    foreach (var check in checks)
                    {
                        group.Add(Row(check));
                    }
                });
            }
            catch (Exception exception)
            {
                Ui.OnMainLoop(() => group.SetDescription(exception.Message));
            }
        });
    }

    private Adw.PreferencesGroup Repair()
    {
        var group = Adw.PreferencesGroup.New();
        group.SetTitle("Repair");

        group.Add(Ui.ActionRow(
            "Enrol a DAW",
            "Link a DAW to Cabinet's yabridge and print the override it needs",
            Icons.Enrol,
            AskForDaw));

        group.Add(Ui.ActionRow(
            "Bridge what is installed",
            "Register every prefix's plugins with yabridgectl again",
            Icons.Sync,
            Sync));

        group.Add(Ui.ActionRow(
            "Look at everything again",
            "Read every page afresh, for what changed outside Cabinet",
            Icons.Refresh,
            () => Ui.OnMainLoop(changed)));

        return group;
    }

    private void Sync() =>
        Operation.Run(
            window,
            "Bridging plugins",
            output => new Yabridgectl(layout, runner)
                .Bridge(new Prefixes(layout, runner).List(), output),
            changed);

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

        changed();
    }

    private static Adw.ActionRow Row(Check check)
    {
        var row = Adw.ActionRow.New();
        row.SetUseMarkup(false);
        row.SetTitle(check.Name);
        row.SetSubtitle(check.Detail);

        var icon = Gtk.Image.NewFromIconName(IconFor(check.Status));
        icon.AddCssClass(CssFor(check.Status));
        row.AddPrefix(icon);

        return row;
    }

    private static string IconFor(Status status) => status switch
    {
        Status.Ok => Icons.Ok,
        Status.Warn => Icons.Warn,
        _ => Icons.Fail,
    };

    private static string CssFor(Status status) => status switch
    {
        Status.Ok => "success",
        Status.Warn => "warning",
        _ => "error",
    };
}
