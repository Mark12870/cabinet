using Cabinet.Core;

namespace Cabinet.Gui;

internal sealed class MainWindow
{
    private readonly Adw.Application application;
    private readonly Adw.ApplicationWindow window;
    private readonly Adw.ViewStack stack = Adw.ViewStack.New();
    private readonly Adw.NavigationView navigation = Adw.NavigationView.New();
    private readonly Adw.ToastOverlay toasts = Adw.ToastOverlay.New();

    private readonly PrefixesPage prefixes;
    private readonly LibraryPage library;
    private readonly RunnersPage runners;
    private readonly DoctorPage doctor;
    private readonly AboutPage about;
    private readonly Operation operations;
    private int activeOperations;
    private bool hidden;

    public MainWindow(Adw.Application application, Layout layout, IProcessRunner runner)
    {
        this.application = application;
        window = Adw.ApplicationWindow.New(application);
        window.SetTitle("Cabinet");
        var settings = Gio.Settings.New(Layout.AppId);
        settings.Bind("window-width", window, "default-width", Gio.SettingsBindFlags.Default);
        settings.Bind("window-height", window, "default-height", Gio.SettingsBindFlags.Default);
        window.SetHideOnClose(false);
        window.OnCloseRequest += (_, _) => CloseRequested();
        operations = new Operation(window, Hold, Release);

        prefixes = new PrefixesPage(
            layout, runner, window, navigation, RefreshState, Toast, operations);
        library = new LibraryPage(
            layout,
            runner,
            window,
            navigation,
            RefreshState,
            Toast,
            Report,
            Hold,
            Release,
            prefixes.IsChanging,
            operations);
        runners = new RunnersPage(layout, runner, window, RefreshRunners, operations);
        doctor = new DoctorPage(layout, runner, window, RefreshDoctor, RefreshAll, operations);
        about = new AboutPage(layout, runner, window);

        stack.AddTitledWithIcon(library.Widget, "library", "Library", Icons.Library);
        stack.AddTitledWithIcon(prefixes.Widget, "prefixes", "Prefixes", Icons.Prefixes);
        stack.AddTitledWithIcon(runners.Widget, "runners", "Runners", Icons.Runners);
        stack.AddTitledWithIcon(doctor.Widget, "doctor", "Doctor", Icons.Doctor);
        stack.AddTitledWithIcon(about.Widget, "about", "About", Icons.About);

        var view = Adw.ToolbarView.New();
        view.AddTopBar(Adw.HeaderBar.New());
        view.AddBottomBar(Switcher());
        view.SetContent(stack);

        navigation.Add(Adw.NavigationPage.New(view, "Cabinet"));
        toasts.SetChild(navigation);
        window.SetContent(toasts);

        RefreshAll();
        BridgeInBackground(layout, runner);
    }

    public void Present()
    {
        hidden = false;
        window.Present();
    }

    public void OpenLink(string link)
    {
        Present();
        stack.SetVisibleChildName("library");
        library.OpenLink(link);
    }

    public void ReportFailure(Exception exception)
    {
        Present();
        Ui.Report(window, "Cabinet could not continue", exception.Message);
    }

    private bool CloseRequested()
    {
        if (activeOperations == 0)
        {
            return false;
        }

        hidden = true;
        window.SetVisible(false);
        return true;
    }

    private void Hold()
    {
        activeOperations++;
        application.Hold();
    }

    private void Release()
    {
        activeOperations--;

        try
        {
            if (activeOperations == 0 && hidden)
            {
                window.Close();
            }
        }
        finally
        {
            application.Release();
        }
    }

    private void BridgeInBackground(Layout layout, IProcessRunner runner)
    {
        Hold();
        Task.Run(() =>
        {
            string? failure = null;
            IReadOnlyList<string> unpermitted = [];

            try
            {
                new Prefixes(layout, runner).Bridge();
                unpermitted = new Doctor(layout, runner).DawsMissingPermissions();
            }
            catch (Exception exception)
            {
                failure = exception.Message;
            }

            Ui.OnMainLoop(() =>
            {
                try
                {
                    if (hidden && (failure is not null || unpermitted.Count > 0))
                    {
                        Present();
                    }

                    if (failure is not null)
                    {
                        Report($"Could not bridge plugins: {failure}", () => failure);
                    }

                    foreach (var dawId in unpermitted)
                    {
                        PermissionsToast(layout, dawId);
                    }

                    doctor.Refresh();
                }
                finally
                {
                    Release();
                }
            });
        });
    }

    private void PermissionsToast(Layout layout, string dawId)
    {
        var toast = Adw.Toast.New($"{dawId} needs updated permissions to load Windows plugins");
        toast.SetButtonLabel("Show");
        toast.SetTimeout(0);
        toast.OnButtonClicked += (_, _) => Ui.Guard(() =>
            new EnrolmentDialog(window, layout, dawId).Present());
        toasts.AddToast(toast);
    }

    private void Toast(string message) => toasts.AddToast(Adw.Toast.New(message));

    private void Report(string message, Func<string?>? details)
    {
        var headline = message.Split('\n')[0];
        var toast = Adw.Toast.New(headline);
        toast.SetUseMarkup(false);
        toast.SetTimeout(0);

        if (details is not null)
        {
            toast.SetButtonLabel("Details");
            toast.OnButtonClicked += (_, _) => Ui.Guard(() =>
                Ui.Observe(Task.Run(details), text =>
                    Ui.Log(window, headline, text ?? message)));
        }

        toasts.AddToast(toast);
    }

    private Adw.ViewSwitcherBar Switcher()
    {
        var switcher = Adw.ViewSwitcherBar.New();
        switcher.SetStack(stack);
        switcher.SetReveal(true);
        return switcher;
    }

    private void RefreshAll()
    {
        library.Refresh();
        prefixes.Refresh();
        runners.Refresh();
        doctor.Refresh();
        about.Refresh();
    }

    private void RefreshState()
    {
        library.Refresh();
        prefixes.Refresh();
        runners.Refresh();
        doctor.Refresh();
    }

    private void RefreshRunners()
    {
        prefixes.Refresh();
        runners.Refresh();
        doctor.Refresh();
    }

    private void RefreshDoctor() => doctor.Refresh();
}
