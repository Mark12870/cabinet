using Cabinet.Core;

namespace Cabinet.Gui;

internal sealed partial class LibraryPage
{
    private void ConfirmRemove(LibraryEntry entry)
    {
        Removal removal;

        try
        {
            removal = new Library(layout, runner).RemovalOf(entry);
        }
        catch (Exception gone)
        {
            toast(gone.Message);
            changed();
            return;
        }

        if (removal.Kind == RemovalKind.Native)
        {
            ConfirmRemoveNative(removal);
            return;
        }

        var where = removal.Prefix!;

        if (prefixIsChanging(where))
        {
            toast($"{where} is changing; try removing {entry.Name} again when it is done.");
            return;
        }

        if (removal.Kind == RemovalKind.TakesPrefix)
        {
            Ui.Confirm(
                window,
                $"Delete “{where}”?",
                $"{entry.Name}'s own uninstaller leaves everything it downloaded behind, so it "
                + $"is the prefix or nothing: its Wine, its registry and every library "
                + $"{entry.Name} put in it go together."
                + (removal.Sharing.Count > 0
                    ? $" {string.Join(" and ", removal.Sharing)} go with it."
                    : ""),
                "Delete Prefix",
                () => Take(removal),
                Adw.ResponseAppearance.Destructive);
            return;
        }

        if (removal.Kind == RemovalKind.KeepsPrefix)
        {
            Ui.Confirm(
                window,
                $"Remove {entry.Name}?",
                Kept(where, removal.Sharing) + " " + Wizard(entry),
                "Remove",
                () => Uninstall(removal),
                Adw.ResponseAppearance.Destructive);
            return;
        }

        Ui.Choose(
            window,
            $"Remove {entry.Name}?",
            $"It is the only plugin Cabinet installed in “{where}”. Deleting the prefix takes "
            + $"its Wine, its registry and its settings with it. {Wizard(entry)}",
            "Remove Plugin Only",
            () => Uninstall(removal),
            "Delete Prefix",
            () => Take(removal));
    }

    private static string Kept(string where, IReadOnlyList<string> sharing) =>
        $"Prefix “{where}” also holds {string.Join(" and ", sharing)}, so it stays.";

    private static string Wizard(LibraryEntry entry) =>
        $"{entry.Name}'s own uninstaller runs, and may open a window of its own.";

    private void ConfirmRemoveNative(Removal removal) => Ui.Confirm(
        window,
        $"Remove {removal.Entry.Name}?",
        removal.Entry.Data is null
            ? "Its files and the links your DAW scans are deleted. Presets you saved elsewhere "
              + "are left alone."
            : $"Its files and the links your DAW scans are deleted, and so is "
              + $"~/{removal.Entry.Data} — the presets you saved for it go with it.",
        "Remove",
        () => operations.Run(
            $"Removing {removal.Entry.Name}",
            output => new Library(layout, runner).Remove(removal, onOutput: output),
            changed),
        Adw.ResponseAppearance.Destructive);

    private void Uninstall(Removal removal) =>
        Task.Run(() => new Library(layout, runner).PossibleUninstallers(removal))
            .ContinueWith(found => Ui.OnMainLoop(() =>
            {
                if (found.IsFaulted)
                {
                    toast(found.Exception!.InnerException!.Message);
                }
                else if (found.Result.Count > 1)
                {
                    ChooseUninstaller(removal, found.Result);
                }
                else
                {
                    RemoveWhenReady(removal, takePrefix: false, null);
                }
            }));

    private void ChooseUninstaller(Removal removal, IReadOnlyList<UninstallEntry> possible)
    {
        var which = Adw.ComboRow.New();
        which.SetTitle("Uninstaller");
        which.SetModel(Gtk.StringList.New([.. possible.Select(one => one.Name)]));

        var fields = Adw.PreferencesGroup.New();
        fields.Add(which);

        Ui.Confirm(
            window,
            $"Which is {removal.Entry.Name}'s uninstaller?",
            $"Cabinet did not see which uninstaller {removal.Entry.Name} registered, and more "
            + "than one in its prefix could be it. Only the one you choose runs.",
            "Run",
            () => RemoveWhenReady(
                removal, takePrefix: false, possible[(int)which.GetSelected()]),
            Adw.ResponseAppearance.Destructive,
            fields);
    }

    private void Take(Removal removal) => RemoveWhenReady(removal, takePrefix: true, null);

    private void RemoveWhenReady(Removal removal, bool takePrefix, UninstallEntry? uninstaller)
    {
        var (entry, prefix) = (removal.Entry, removal.Prefix!);

        if (prefixIsChanging(prefix))
        {
            toast($"{prefix} is changing; try removing {entry.Name} again when it is done.");
            return;
        }

        operations.Run(
            takePrefix ? $"Deleting {prefix}" : $"Removing {entry.Name}",
            output => new Library(layout, runner)
                .Remove(removal, takePrefix, uninstaller, output),
            changed);
    }
}
