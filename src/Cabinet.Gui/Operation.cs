using Cabinet.Core;

namespace Cabinet.Gui;

internal sealed class Operation
{
    private const long RedrawMilliseconds = 100;

    private readonly Adw.Dialog dialog = Adw.Dialog.New();
    private readonly Gtk.Label status = Gtk.Label.New(null);
    private readonly Gtk.ProgressBar bar = Gtk.ProgressBar.New();
    private readonly Gtk.TextView log = Gtk.TextView.New();
    private readonly Gtk.Button close = Gtk.Button.NewWithLabel("Close");
    private readonly Lock gate = new();
    private double pending;
    private bool queued;
    private long drawn;

    private Operation(string title)
    {
        dialog.SetTitle(title);
        dialog.SetContentWidth(640);
        dialog.SetContentHeight(420);
        dialog.SetCanClose(false);

        status.SetXalign(0);
        status.SetWrap(true);
        status.AddCssClass("heading");

        bar.SetShowText(true);
        bar.SetVisible(false);

        log.SetMonospace(true);
        log.SetEditable(false);
        log.AddCssClass("card");

        close.SetSensitive(false);
        close.AddCssClass("suggested-action");
        close.SetHalign(Gtk.Align.End);
        close.OnClicked += (_, _) => dialog.ForceClose();

        var body = Ui.Page();
        body.Append(status);
        body.Append(bar);
        body.Append(Ui.Scrolled(log));
        body.Append(close);

        var view = Adw.ToolbarView.New();
        view.AddTopBar(Adw.HeaderBar.New());
        view.SetContent(body);
        dialog.SetChild(view);
    }

    public static void Run(
        Gtk.Widget parent,
        string title,
        Action<Action<string>> work,
        Action? onFinished = null) =>
        Run(parent, title, (output, _) => work(output), onFinished);

    public static void Run(
        Gtk.Widget parent,
        string title,
        Action<Action<string>, Action<double>> work,
        Action? onFinished = null)
    {
        var operation = new Operation(title);
        operation.status.SetText(title);
        operation.dialog.Present(parent);

        Task.Run(() =>
        {
            try
            {
                work(operation.Write, operation.Show);
                operation.Finish(null);
            }
            catch (Exception exception)
            {
                operation.Finish(exception.Message);
            }
        }).ContinueWith(_ => Ui.OnMainLoop(() => onFinished?.Invoke()));
    }

    public static void Ensure(ProcessResult result, string what)
    {
        if (!result.Ok)
        {
            throw new InvalidOperationException($"{what} exited with {result.ExitCode}");
        }
    }

    private void Write(string line) => Ui.OnMainLoop(() =>
    {
        var buffer = log.GetBuffer();
        buffer.GetEndIter(out var end);
        buffer.Insert(end, line + "\n", -1);
    });

    private void Show(double fraction)
    {
        lock (gate)
        {
            pending = fraction;

            if (queued || Environment.TickCount64 - drawn < RedrawMilliseconds)
            {
                return;
            }

            queued = true;
            drawn = Environment.TickCount64;
        }

        Ui.OnMainLoop(() =>
        {
            double drawing;

            lock (gate)
            {
                drawing = pending;
                queued = false;
            }

            bar.SetVisible(true);
            bar.SetFraction(drawing);
            bar.SetText($"{drawing * 100:0}%");
        });
    }

    private void Finish(string? error) => Ui.OnMainLoop(() =>
    {
        bar.SetVisible(false);
        status.SetText(error is null ? "Done." : error);
        status.AddCssClass(error is null ? "success" : "error");
        close.SetSensitive(true);
        dialog.SetCanClose(true);
    });
}
