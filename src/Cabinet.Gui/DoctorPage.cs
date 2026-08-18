using Cabinet.Core;

namespace Cabinet.Gui;

internal sealed class DoctorPage
{
    private readonly Layout layout;
    private readonly Gtk.Box list = Gtk.Box.New(Gtk.Orientation.Vertical, 12);

    public DoctorPage(Layout layout)
    {
        this.layout = layout;

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

        Task.Run(() =>
        {
            var checks = new Doctor(layout).Run();
            Ui.OnMainLoop(() =>
            {
                foreach (var check in checks)
                {
                    group.Add(Row(check));
                }
            });
        });
    }

    private static Adw.ActionRow Row(Check check)
    {
        var row = Adw.ActionRow.New();
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
