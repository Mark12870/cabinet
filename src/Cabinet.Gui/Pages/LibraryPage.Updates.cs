using Cabinet.Core;

namespace Cabinet.Gui;

internal sealed partial class LibraryPage
{
    private bool UpdateBlocked(PrefixUpdate? update) => update is not null
        && (prefixIsChanging(update.Prefix)
            || installed.Any(pair => pair.Value == update.Prefix && Running(pair.Key)));

    private void ConfirmUpdate(PrefixUpdate update)
    {
        if (UpdateBlocked(update))
        {
            toast($"{update.Prefix} is in use; close its apps and wait for ongoing operations to finish.");
            return;
        }

        Ui.Confirm(
            window,
            $"Update prefix {update.Prefix}?",
            update.Description,
            "Update prefix",
            () => Update(update));
    }

    public void UpdateAll() =>
        Task.Run(() => new Library(layout, runner).PendingPrefixUpdates()).ContinueWith(found =>
            Ui.OnMainLoop(() =>
            {
                if (found.IsFaulted)
                {
                    toast(found.Exception!.InnerException!.Message);
                    return;
                }

                if (found.Result.Count == 0)
                {
                    toast("Every prefix setup is up to date.");
                    return;
                }

                ConfirmUpdateAll(found.Result);
            }));

    private void ConfirmUpdateAll(IReadOnlyList<PrefixUpdate> pending)
    {
        if (pending.FirstOrDefault(UpdateBlocked) is { } busy)
        {
            toast($"{busy.Prefix} is in use; close its apps and wait for ongoing operations to finish.");
            return;
        }

        Ui.Confirm(
            window,
            $"Update {pending.Count} prefix(es)?",
            string.Join("\n\n", pending.Select(update => $"{update.Prefix}: {update.Description}")),
            "Update all",
            () => operations.Run(
                "Updating prefixes",
                (output, progress) =>
                {
                    var library = new Library(layout, runner);
                    foreach (var update in pending)
                    {
                        library.UpdatePrefix(update, output, progress);
                    }
                },
                changed));
    }

    private void Update(PrefixUpdate update)
    {
        if (UpdateBlocked(update))
        {
            toast($"{update.Prefix} is in use; try updating it again when it is free.");
            return;
        }

        operations.Run(
            $"Updating prefix {update.Prefix}",
            (output, progress) => new Library(layout, runner).UpdatePrefix(update, output, progress),
            changed);
    }
}
