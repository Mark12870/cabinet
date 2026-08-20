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

    public static Gtk.Button RowButton(string iconName, string tooltip, bool destructive = false)
    {
        var button = IconButton(iconName, tooltip);
        button.SetValign(Gtk.Align.Center);

        if (destructive)
        {
            button.AddCssClass("destructive-action");
            button.RemoveCssClass("flat");
        }

        return button;
    }

    public static void Prompt(
        Gtk.Widget parent,
        string heading,
        string body,
        string placeholder,
        Action<string> accepted)
    {
        var entry = Gtk.Entry.New();
        entry.SetPlaceholderText(placeholder);

        Confirm(parent, heading, body, "Continue", () =>
        {
            if (entry.GetText().Trim() is { Length: > 0 } text)
            {
                accepted(text);
            }
        }, extra: entry);
    }

    public static void Confirm(
        Gtk.Widget parent,
        string heading,
        string body,
        string action,
        Action accepted,
        Adw.ResponseAppearance appearance = Adw.ResponseAppearance.Suggested,
        Gtk.Widget? extra = null)
    {
        var dialog = Adw.AlertDialog.New(heading, body);

        if (extra is not null)
        {
            extra.SetMarginTop(12);
            dialog.SetExtraChild(extra);
        }

        var destructive = appearance == Adw.ResponseAppearance.Destructive;

        dialog.AddResponse("cancel", "Cancel");
        dialog.AddResponse("ok", action);
        dialog.SetResponseAppearance("ok", appearance);
        dialog.SetDefaultResponse(destructive ? "cancel" : "ok");
        dialog.SetCloseResponse("cancel");

        dialog.OnResponse += (_, args) =>
        {
            if (args.Response == "ok")
            {
                accepted();
            }
        };

        dialog.Present(parent);
    }

    public static void Report(Gtk.Widget parent, string heading, string body)
    {
        var dialog = Adw.AlertDialog.New(heading, body);
        dialog.AddResponse("ok", "Close");
        dialog.SetDefaultResponse("ok");
        dialog.SetCloseResponse("ok");
        dialog.Present(parent);
    }

    public static void ChooseFile(Gtk.Window parent, string title, Action<string> chosen)
    {
        var chooser = Gtk.FileDialog.New();
        chooser.SetTitle(title);

        chooser.OpenAsync(parent).ContinueWith(task =>
        {
            if (task.IsFaulted || task.Result?.GetPath() is not { Length: > 0 } path)
            {
                return;
            }

            OnMainLoop(() => chosen(path));
        });
    }
}
