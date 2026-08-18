namespace Cabinet.Gui;

internal sealed class Operation
{
    private readonly Adw.Dialog dialog = Adw.Dialog.New();
    private readonly Gtk.Label status = Gtk.Label.New(null);
    private readonly Gtk.TextView log = Gtk.TextView.New();
    private readonly Gtk.Button close = Gtk.Button.NewWithLabel("Close");

    private Operation(string title)
    {
        dialog.SetTitle(title);
        dialog.SetContentWidth(640);
        dialog.SetContentHeight(420);
        dialog.SetCanClose(false);

        status.SetXalign(0);
        status.SetWrap(true);
        status.AddCssClass("heading");

        log.SetMonospace(true);
        log.SetEditable(false);
        log.AddCssClass("card");

        close.SetSensitive(false);
        close.AddCssClass("suggested-action");
        close.SetHalign(Gtk.Align.End);
        close.OnClicked += (_, _) => dialog.ForceClose();

        var body = Ui.Page();
        body.Append(status);
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
        Action? onFinished = null)
    {
        var operation = new Operation(title);
        operation.status.SetText(title);
        operation.dialog.Present(parent);

        Task.Run(() =>
        {
            try
            {
                work(operation.Write);
                operation.Finish(null);
            }
            catch (Exception exception)
            {
                operation.Finish(exception.Message);
            }
        }).ContinueWith(_ => Ui.OnMainLoop(() => onFinished?.Invoke()));
    }

    private void Write(string line) => Ui.OnMainLoop(() =>
    {
        var buffer = log.GetBuffer();
        buffer.GetEndIter(out var end);
        buffer.Insert(end, line + "\n", -1);
    });

    private void Finish(string? error) => Ui.OnMainLoop(() =>
    {
        status.SetText(error is null ? "Done." : error);
        status.AddCssClass(error is null ? "success" : "error");
        close.SetSensitive(true);
        dialog.SetCanClose(true);
    });
}
