using Cabinet.Core;

namespace Cabinet.Gui;

internal sealed class EnrolmentDialog(Gtk.Window window, Layout layout, string dawId)
{
    private readonly Adw.Dialog dialog = Adw.Dialog.New();

    public void Present()
    {
        dialog.SetTitle($"Enrol {dawId}");
        dialog.SetContentWidth(680);
        dialog.SetContentHeight(600);

        var steps = Gtk.Box.New(Gtk.Orientation.Vertical, 24);
        steps.Append(Step(
            "Grant the permissions",
            "Cabinet does not run this for you, because the decision is yours. "
            + Enrolment.TrustBoundary(dawId),
            Enrolment.OverrideCommand(dawId, layout)));
        steps.Append(Step(
            "Then check the shim loads",
            $"{dawId} may ship a runtime older than the one the shim was built against.",
            Enrolment.SelfTestCommand(dawId, layout)));

        var body = Ui.Page();
        body.Append(Ui.Scrolled(steps));

        var view = Adw.ToolbarView.New();
        view.AddTopBar(Adw.HeaderBar.New());
        view.SetContent(body);
        dialog.SetChild(view);
        dialog.Present(window);
    }

    private Gtk.Box Step(string title, string why, string command)
    {
        var heading = Gtk.Label.New(title);
        heading.SetXalign(0);
        heading.AddCssClass("heading");

        var reason = Gtk.Label.New(why);
        reason.SetXalign(0);
        reason.SetWrap(true);
        reason.AddCssClass("dim-label");

        var copy = Gtk.Button.NewWithLabel("Copy");
        copy.SetHalign(Gtk.Align.End);
        copy.AddCssClass("suggested-action");
        copy.OnClicked += (_, _) => Ui.Guard(() =>
        {
            window.GetClipboard().SetText(command);
            copy.SetLabel("Copied");
        });

        var step = Gtk.Box.New(Gtk.Orientation.Vertical, 12);
        step.Append(heading);
        step.Append(reason);
        step.Append(Shown(command));
        step.Append(copy);
        return step;
    }

    private static Gtk.Box Shown(string command)
    {
        var text = Gtk.Label.New(command);
        text.SetSelectable(true);
        text.SetWrap(true);
        text.SetWrapMode(Pango.WrapMode.WordChar);
        text.SetXalign(0);
        text.AddCssClass("monospace");
        text.SetMarginTop(12);
        text.SetMarginBottom(12);
        text.SetMarginStart(12);
        text.SetMarginEnd(12);

        var card = Gtk.Box.New(Gtk.Orientation.Vertical, 0);
        card.AddCssClass("card");
        card.Append(text);
        return card;
    }
}
