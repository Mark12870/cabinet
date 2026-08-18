using Cabinet.Core;

namespace Cabinet.Gui;

internal static class Program
{
    private static int Main()
    {
        var application = Adw.Application.New(Layout.AppId, Gio.ApplicationFlags.DefaultFlags);

        application.OnActivate += (sender, _) =>
        {
            var layout = Layout.FromEnvironment();
            Bootstrap.Ensure(layout);

            new MainWindow((Adw.Application)sender, layout, new ProcessRunner()).Present();
        };

        return application.RunWithSynchronizationContext(null);
    }
}
