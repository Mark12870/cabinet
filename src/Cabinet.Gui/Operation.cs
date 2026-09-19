using Cabinet.Core;

namespace Cabinet.Gui;

internal sealed class Operation(
    Gtk.Widget parent,
    Action hold,
    Action release)
{
    public void Run(
        string title,
        Action<Action<string>> work,
        Action? onFinished = null) =>
        Start(title, (output, _, _) => work(output), onFinished, cancellable: false);

    public void Run(
        string title,
        Action<Action<string>, Action<double>> work,
        Action? onFinished = null) =>
        Start(title, (output, progress, _) => work(output, progress), onFinished, cancellable: false);

    public void RunCancellable(
        string title,
        Action<Action<string>, CancellationToken> work,
        Action? onFinished = null) =>
        Start(title, (output, _, token) => work(output, token), onFinished, cancellable: true);

    public void RunCancellable(
        string title,
        Action<Action<string>, Action<double>, CancellationToken> work,
        Action? onFinished = null) =>
        Start(title, work, onFinished, cancellable: true);

    public static void Ensure(ProcessResult result, string what)
    {
        if (!result.Ok)
        {
            throw new InvalidOperationException($"{what} exited with {result.ExitCode}");
        }
    }

    private void Start(
        string title,
        Action<Action<string>, Action<double>, CancellationToken> work,
        Action? onFinished,
        bool cancellable)
    {
        var dialog = new OperationDialog(title, cancellable);
        hold();

        try
        {
            dialog.Present(parent);
        }
        catch
        {
            dialog.Dispose();
            release();
            throw;
        }

        Task.Run(() =>
        {
            try
            {
                work(dialog.Write, dialog.Show, dialog.Token);
                return (string?)null;
            }
            catch (OperationCanceledException) when (dialog.Token.IsCancellationRequested)
            {
                return "Cancelled.";
            }
            catch (Exception exception)
            {
                return exception.Message;
            }
        }).ContinueWith(task => Ui.OnMainLoop(() =>
        {
            try
            {
                dialog.Finish(task.Result);
                onFinished?.Invoke();
            }
            finally
            {
                dialog.Dispose();
                release();
            }
        }));
    }

    private sealed class OperationDialog : IDisposable
    {
        private const long RedrawMilliseconds = 100;

        private readonly Adw.Dialog dialog = Adw.Dialog.New();
        private readonly Gtk.Label status = Gtk.Label.New(null);
        private readonly Gtk.ProgressBar bar = Gtk.ProgressBar.New();
        private readonly Gtk.TextView log = Gtk.TextView.New();
        private readonly Gtk.Button cancel = Gtk.Button.NewWithLabel("Cancel");
        private readonly Gtk.Button close = Gtk.Button.NewWithLabel("Close");
        private readonly CancellationTokenSource cancellation = new();
        private readonly Lock gate = new();
        private double pending;
        private bool queued;
        private long drawn;
        private bool finished;

        public OperationDialog(string title, bool cancellable)
        {
            dialog.SetTitle(title);
            dialog.SetContentWidth(640);
            dialog.SetContentHeight(420);
            dialog.SetCanClose(false);

            status.SetText(cancellable
                ? title
                : $"{title}\nThis step cannot be cancelled safely once started.");
            status.SetXalign(0);
            status.SetWrap(true);
            status.AddCssClass("heading");

            bar.SetShowText(true);
            bar.SetVisible(false);

            log.SetMonospace(true);
            log.SetEditable(false);
            log.AddCssClass("card");

            cancel.SetVisible(cancellable);
            cancel.OnClicked += (_, _) => Ui.Guard(Cancel);

            close.SetSensitive(false);
            close.AddCssClass("suggested-action");
            close.OnClicked += (_, _) => Ui.Guard(dialog.ForceClose);

            var buttons = Gtk.Box.New(Gtk.Orientation.Horizontal, 6);
            buttons.SetHalign(Gtk.Align.End);
            buttons.Append(cancel);
            buttons.Append(close);

            var body = Ui.Page();
            body.Append(status);
            body.Append(bar);
            body.Append(Ui.Scrolled(log));
            body.Append(buttons);

            var view = Adw.ToolbarView.New();
            view.AddTopBar(Adw.HeaderBar.New());
            view.SetContent(body);
            dialog.SetChild(view);
        }

        public CancellationToken Token => cancellation.Token;

        public void Present(Gtk.Widget parent) => dialog.Present(parent);

        public void Dispose() => cancellation.Dispose();

        public void Write(string line) => Ui.OnMainLoop(() =>
        {
            var buffer = log.GetBuffer();
            buffer.GetEndIter(out var end);
            buffer.Insert(end, line + "\n", -1);
        });

        public void Show(double fraction)
        {
            long wait;

            lock (gate)
            {
                pending = fraction;

                if (queued)
                {
                    return;
                }

                queued = true;
                wait = Math.Max(0, RedrawMilliseconds - (Environment.TickCount64 - drawn));
            }

            if (wait == 0)
            {
                Ui.OnMainLoop(Draw);
                return;
            }

            GLib.Functions.TimeoutAdd(0, (uint)wait, () =>
            {
                Ui.Guard(Draw);
                return false;
            });
        }

        private void Draw()
        {
            double drawing;

            lock (gate)
            {
                drawing = pending;
                queued = false;
                drawn = Environment.TickCount64;
            }

            if (finished)
            {
                return;
            }

            bar.SetVisible(true);
            bar.SetFraction(drawing);
            bar.SetText($"{drawing * 100:0}%");
        }

        public void Finish(string? result)
        {
            var cancelled = result == "Cancelled.";
            finished = true;
            bar.SetVisible(false);
            status.SetText(result ?? "Done.");
            status.AddCssClass(result is null ? "success" : cancelled ? "warning" : "error");
            cancel.SetVisible(false);
            close.SetSensitive(true);
            dialog.SetCanClose(true);
        }

        private void Cancel()
        {
            cancel.SetSensitive(false);
            status.SetText("Cancelling…");
            cancellation.Cancel();
        }
    }
}
