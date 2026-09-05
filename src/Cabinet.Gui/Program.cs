using Cabinet.Core;

namespace Cabinet.Gui;

internal static class Program
{
    private static int Main()
    {
        var application = Adw.Application.New(Layout.AppId, Gio.ApplicationFlags.DefaultFlags);
        MainWindow? window = null;

        application.OnActivate += (sender, _) =>
        {
            if (window is null)
            {
                var layout = Layout.FromEnvironment();
                Bootstrap.Ensure(layout);
                window = new MainWindow((Adw.Application)sender, layout, new ProcessRunner());
            }

            window.Present();
        };

        return application.RunWithSynchronizationContext(null);
    }
}
