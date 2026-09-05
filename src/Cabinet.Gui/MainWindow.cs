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
    private int activeLaunches;
    private bool hidden;

    public MainWindow(Adw.Application application, Layout layout, IProcessRunner runner)
    {
        this.application = application;
        window = Adw.ApplicationWindow.New(application);
        window.SetTitle("Cabinet");
        window.SetDefaultSize(920, 640);
        window.SetHideOnClose(false);
        window.OnCloseRequest += (_, _) => CloseRequested();

        prefixes = new PrefixesPage(layout, runner, window, navigation, RefreshAll);
        library = new LibraryPage(
            layout, runner, window, navigation, RefreshAll, Toast, Hold, Release);
        runners = new RunnersPage(layout, runner, window, RefreshAll);
        doctor = new DoctorPage(layout, runner, window, RefreshAll);
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
    }

    public void Present()
    {
        hidden = false;
        window.Present();
    }

    private bool CloseRequested()
    {
        if (activeLaunches == 0)
        {
            return false;
        }

        hidden = true;
        window.SetVisible(false);
        return true;
    }

    private void Hold()
    {
        activeLaunches++;
        application.Hold();
    }

    private void Release()
    {
        activeLaunches--;
        application.Release();

        if (activeLaunches == 0 && hidden)
        {
            window.Close();
        }
    }

    private void Toast(string message) => toasts.AddToast(Adw.Toast.New(message));

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
}
