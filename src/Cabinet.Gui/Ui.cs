namespace Cabinet.Gui;

internal static class Ui
{
    public static void OnMainLoop(Action action) =>
        GLib.Functions.IdleAdd(0, () =>
        {
            action();
            return false;
        });

    public static void Clear(Gtk.Box box)
    {
        while (box.GetFirstChild() is { } child)
        {
            box.Remove(child);
        }
    }

    public static Gtk.Box Page()
    {
        var box = Gtk.Box.New(Gtk.Orientation.Vertical, 18);
        box.SetMarginTop(24);
        box.SetMarginBottom(24);
        box.SetMarginStart(24);
        box.SetMarginEnd(24);
        return box;
    }

    public static Gtk.ScrolledWindow Scrolled(Gtk.Widget child)
    {
        var scrolled = Gtk.ScrolledWindow.New();
        scrolled.SetVexpand(true);
        scrolled.SetChild(child);
        return scrolled;
    }

    public static Gtk.Button IconButton(string iconName, string tooltip)
    {
        var button = Gtk.Button.NewFromIconName(iconName);
        button.SetTooltipText(tooltip);
        button.AddCssClass("flat");
        return button;
    }
}
