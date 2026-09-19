using Cabinet.Core;

namespace Cabinet.Gui;

internal static class Program
{
    private static int Main(string[] args)
    {
        var application = Adw.Application.New(Layout.AppId, Gio.ApplicationFlags.HandlesOpen);
        MainWindow? window = null;

        MainWindow Window(Adw.Application app)
        {
            Ui.SetErrorHandler(exception =>
            {
                if (window is null)
                {
                    ShowFailure(app, exception);
                }
                else
                {
                    window.ReportFailure(exception);
                }
            });

            if (window is null)
            {
                var layout = Layout.FromEnvironment();
                Bootstrap.Ensure(layout);
                window = new MainWindow(app, layout, new ProcessRunner());
            }

            return window;
        }

        application.OnActivate += (sender, _) =>
            Ui.Guard(() => Window((Adw.Application)sender).Present());

        application.OnOpen += (sender, signal) =>
            Ui.Guard(() =>
            {
                var opened = Window((Adw.Application)sender);

                foreach (var file in signal.Files)
                {
                    opened.OpenLink(file.GetUri());
                }
            });

        return application.RunWithSynchronizationContext(["cabinet-gui", .. args]);
    }

    private static void ShowFailure(Adw.Application application, Exception exception)
    {
        var window = Adw.ApplicationWindow.New(application);
        window.SetTitle("Cabinet");
        window.SetDefaultSize(520, 240);
        window.Present();
        Ui.Report(window, "Cabinet could not continue", exception.Message);
    }
}
