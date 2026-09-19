namespace Cabinet.Gui;

internal static class Ui
{
    private static Action<Exception>? errorHandler;

    public static void SetErrorHandler(Action<Exception> handler) => errorHandler = handler;

    public static void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            Handle(exception);
        }
    }

    public static void OnMainLoop(Action action) =>
        GLib.Functions.IdleAdd(0, () =>
        {
            Guard(action);
            return false;
        });

    public static void Observe(Task task) =>
        task.ContinueWith(completed =>
        {
            if (completed.Exception?.GetBaseException() is { } exception)
            {
                OnMainLoop(() => Handle(exception));
            }
        });

    public static void Observe<T>(Task<T> task, Action<T> completed) =>
        task.ContinueWith(finished =>
        {
            if (finished.Exception?.GetBaseException() is { } exception)
            {
                OnMainLoop(() => Handle(exception));
            }
            else if (finished.IsCompletedSuccessfully)
            {
                OnMainLoop(() => completed(finished.Result));
            }
        });

    private static void Handle(Exception exception)
    {
        if (errorHandler is null)
        {
            Console.Error.WriteLine(exception);
            return;
        }

        try
        {
            errorHandler(exception);
        }
        catch (Exception reporting)
        {
            Console.Error.WriteLine(exception);
            Console.Error.WriteLine(reporting);
        }
    }

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

    public static Adw.ActionRow ActionRow(
        string title,
        string subtitle,
        string iconName,
        Action clicked,
        bool destructive = false)
    {
        var row = Adw.ActionRow.New();
        row.SetTitle(title);

        if (subtitle.Length > 0)
        {
            row.SetSubtitle(subtitle);
        }

        var button = RowButton(iconName, title, destructive);
        button.OnClicked += (_, _) => Guard(clicked);
        row.AddSuffix(button);
        row.SetActivatableWidget(button);

        return row;
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

    public static Adw.AlertDialog Confirm(
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
                Guard(accepted);
            }
        };

        dialog.Present(parent);
        return dialog;
    }

    public static void RequireName(
        Adw.AlertDialog dialog, Adw.EntryRow name, Func<string?> problem)
    {
        void Check()
        {
            var found = problem();
            name.SetTitle(found ?? "Name");
            dialog.SetResponseEnabled("ok", found is null);

            if (found is null)
            {
                name.RemoveCssClass("error");
            }
            else
            {
                name.AddCssClass("error");
            }
        }

        name.OnNotify += (_, args) =>
        {
            if (args.Pspec.GetName() == "text")
            {
                Guard(Check);
            }
        };

        Check();
    }

    public static void Choose(
        Gtk.Widget parent,
        string heading,
        string body,
        string first,
        Action chose,
        string second,
        Action alsoChose)
    {
        var dialog = Adw.AlertDialog.New(heading, body);

        dialog.AddResponse("cancel", "Cancel");
        dialog.AddResponse("first", first);
        dialog.AddResponse("second", second);
        dialog.SetResponseAppearance("first", Adw.ResponseAppearance.Destructive);
        dialog.SetResponseAppearance("second", Adw.ResponseAppearance.Destructive);
        dialog.SetDefaultResponse("cancel");
        dialog.SetCloseResponse("cancel");

        dialog.OnResponse += (_, args) =>
        {
            if (args.Response == "first")
            {
                Guard(chose);
            }
            else if (args.Response == "second")
            {
                Guard(alsoChose);
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

    public static void Log(Gtk.Widget parent, string title, string text)
    {
        var dialog = Adw.Dialog.New();
        dialog.SetTitle(title);
        dialog.SetContentWidth(720);
        dialog.SetContentHeight(480);

        var view = Gtk.TextView.New();
        view.SetMonospace(true);
        view.SetEditable(false);
        view.AddCssClass("card");
        view.GetBuffer().SetText(text, -1);

        var body = Page();
        body.Append(Scrolled(view));

        var bars = Adw.ToolbarView.New();
        bars.AddTopBar(Adw.HeaderBar.New());
        bars.SetContent(body);
        dialog.SetChild(bars);

        dialog.Present(parent);
    }

    public static void ChooseFile(Gtk.Window parent, string title, Action<string> chosen)
    {
        var chooser = Gtk.FileDialog.New();
        chooser.SetTitle(title);

        Observe(chooser.OpenAsync(parent), file =>
        {
            if (file?.GetPath() is not { Length: > 0 } path)
            {
                return;
            }

            chosen(path);
        });
    }
}
