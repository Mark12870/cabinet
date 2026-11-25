using Cabinet.Core;

namespace Cabinet.Gui;

internal sealed class DownloadWindow
{
    private const uint PulseMilliseconds = 100;

    private readonly Adw.Window window = Adw.Window.New();
    private readonly Gtk.Label status = Gtk.Label.New(null);
    private readonly Gtk.ProgressBar bar = Gtk.ProgressBar.New();
    private readonly Gtk.TextView log = Ui.LogView();
    private readonly Gtk.Button close = Gtk.Button.NewWithLabel("Close");
    private bool finished;
    private bool closed;

    private DownloadWindow(Gtk.Application? application, string app)
    {
        window.SetApplication(application);
        window.SetTitle($"Installing from {app}");
        window.SetDefaultSize(560, 380);
        window.OnCloseRequest += (_, _) =>
        {
            closed = true;
            return false;
        };

        status.SetXalign(0);
        status.SetWrap(true);
        status.AddCssClass("heading");

        close.AddCssClass("suggested-action");
        close.SetHalign(Gtk.Align.End);
        close.OnClicked += (_, _) => Ui.Guard(window.Close);

        var body = Ui.Page();
        body.Append(status);
        body.Append(bar);
        body.Append(Ui.Scrolled(log));
        body.Append(close);
        window.SetContent(Ui.Bars(body));

        GLib.Functions.TimeoutAdd(0, PulseMilliseconds, () =>
        {
            if (!finished && !closed)
            {
                bar.Pulse();
            }

            return !finished && !closed;
        });
    }

    public static Action<DownloadStep> Follow(Gtk.Window parent, string app)
    {
        DownloadWindow? showing = null;

        return step => Ui.OnMainLoop(() =>
        {
            if (showing is null)
            {
                showing = new DownloadWindow(parent.GetApplication(), app);
                showing.window.Present();
            }

            if (!showing.closed)
            {
                showing.Take(step);
            }

            if (step is DownloadInstalled)
            {
                showing = null;
            }
        });
    }

    private void Take(DownloadStep step)
    {
        switch (step)
        {
            case DownloadReceiving receiving:
                status.SetText($"Downloading… {receiving.Bytes / 1_000_000.0:0.0} MB received");
                break;
            case DownloadInstalling installing:
                status.SetText("Installing…");
                log.GetBuffer().GetEndIter(out var end);
                log.GetBuffer().Insert(end, installing.Line + "\n", -1);
                break;
            case DownloadInstalled installed:
                finished = true;
                bar.SetVisible(false);
                status.SetText(installed.Failure ?? (installed.Ready.Count > 0
                    ? $"Ready in your DAW: {string.Join(", ", installed.Ready)}"
                    : "Installed. Nothing new appeared for your DAW."));
                status.AddCssClass(installed.Failure is null ? "success" : "error");
                break;
        }
    }
}
