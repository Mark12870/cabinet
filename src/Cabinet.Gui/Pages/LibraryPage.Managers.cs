using Cabinet.Core;

namespace Cabinet.Gui;

internal sealed partial class LibraryPage
{
    public void OpenLink(string link)
    {
        hold();

        Task.Run(() => new Library(layout, runner).ForLink(link)).ContinueWith(found =>
            Ui.OnMainLoop(() =>
            {
                try
                {
                    if (found.Exception?.GetBaseException() is { } exception)
                    {
                        report($"Cabinet could not open the link: {exception.Message}", null);
                    }
                    else
                    {
                        HandLink(found.Result, link);
                    }
                }
                finally
                {
                    release();
                }
            }));
    }

    private void HandLink(LibraryEntry entry, string link)
    {
        if (!Running(entry.Id))
        {
            Launch(entry, (library, downloads) => library.Open(link, onDownload: downloads));
            return;
        }

        toast($"Handing the link to {entry.Name}.");
        hold();

        Task.Run(() => new Library(layout, runner).Open(link)).ContinueWith(handed =>
            Ui.OnMainLoop(() =>
            {
                try
                {
                    if (handed.Exception?.GetBaseException() is { } exception)
                    {
                        report($"{entry.Name} was not handed the link: {Told(exception)}",
                            LaunchLog(entry));
                    }
                }
                finally
                {
                    release();
                }
            }));
    }

    private void Launch(LibraryEntry entry) =>
        Launch(entry, (library, downloads) => library.Launch(entry, onDownload: downloads));

    private void Launch(LibraryEntry entry, Action<Library, Action<DownloadStep>> start)
    {
        if (Running(entry.Id))
        {
            Task.Run(() => new Library(layout, runner).Show(entry)).ContinueWith(shown =>
                Ui.OnMainLoop(() =>
                {
                    if (shown.Exception?.GetBaseException() is { } exception)
                    {
                        toast($"{entry.Name} could not be shown: {exception.Message}");
                    }
                }));
            return;
        }

        if (entry.Finish is null)
        {
            OpenApp(entry, start);
            return;
        }

        operations.RunThen(
            $"Preparing {entry.Name}",
            output => new Library(layout, runner).Prepare(entry, output),
            () => OpenApp(entry, start));
    }

    private void OpenApp(LibraryEntry entry, Action<Library, Action<DownloadStep>> start)
    {
        running.Add(entry.Id);
        toast($"Opening {entry.Name}.");
        changed();
        hold();

        Task.Run(() =>
        {
            try
            {
                start(new Library(layout, runner), DownloadWindow.Follow(window, entry.Name));
            }
            catch (Exception exception)
            {
                var told = Told(exception);
                Ui.OnMainLoop(() =>
                {
                    if (!stopping.Contains(entry.Id))
                    {
                        report($"{entry.Name} did not open: {told}", LaunchLog(entry));
                    }
                });
            }
        }).ContinueWith(_ => Ui.OnMainLoop(() =>
        {
            try
            {
                running.Remove(entry.Id);
                stopping.Remove(entry.Id);
                changed();
            }
            finally
            {
                release();
            }
        }));
    }

    private void Stop(LibraryEntry entry)
    {
        stopping.Add(entry.Id);
        toast($"Stopping {entry.Name}.");
        changed();

        Task.Run(() =>
        {
            try
            {
                var outcome = new Library(layout, runner).Stop(entry);
                Ui.OnMainLoop(() =>
                {
                    if (outcome.Result == StopResult.Closed)
                    {
                        toast(outcome.Told);
                        return;
                    }

                    if (outcome.Result == StopResult.LeftRunning)
                    {
                        stopping.Remove(entry.Id);
                    }

                    report(outcome.Told, LaunchLog(entry));
                });
            }
            catch (Exception exception)
            {
                var told = Told(exception);
                Ui.OnMainLoop(() =>
                {
                    stopping.Remove(entry.Id);
                    report($"{entry.Name} did not stop: {told}", LaunchLog(entry));
                });
            }
        }).ContinueWith(_ => Ui.OnMainLoop(changed));
    }

    private Func<string?> LaunchLog(LibraryEntry entry) =>
        () => new Library(layout, runner).LaunchLog(entry);

    private static string Told(Exception exception) =>
        exception is AggregateException many
            ? string.Join(" ", many.Flatten().InnerExceptions.Select(inner => inner.Message))
            : exception.Message;
}
