using System.Security;
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

        var dialog = Ui.Confirm(
            window,
            $"Update the {update.Prefix} prefix?",
            update.Summary,
            "Update prefix",
            () => Update(update),
            extra: Details([update], headed: false));
        dialog.SetPreferWideLayout(true);
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

        var dialog = Ui.Confirm(
            window,
            pending.Count == 1 ? $"Update the {pending[0].Prefix} prefix?" : $"Update {pending.Count} prefixes?",
            pending.Count == 1 ? pending[0].Summary : "Each prefix gets Cabinet's settings for the software installed in it.",
            pending.Count == 1 ? "Update prefix" : "Update all",
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
                changed),
            extra: Details(pending, headed: pending.Count > 1));
        dialog.SetPreferWideLayout(true);
    }

    private static Gtk.Widget Details(IReadOnlyList<PrefixUpdate> pending, bool headed)
    {
        var details = Gtk.Box.New(Gtk.Orientation.Vertical, 12);

        foreach (var update in pending)
        {
            if (headed)
            {
                var heading = Gtk.Label.New($"The {update.Prefix} prefix");
                heading.AddCssClass("heading");
                heading.SetXalign(0);
                details.Append(heading);

                var summary = Gtk.Label.New(update.Summary);
                summary.SetXalign(0);
                summary.SetWrap(true);
                details.Append(summary);
            }

            Section(details, PrefixUpdate.WillChange, update.Changes, null);
            Section(details, PrefixUpdate.AlreadyInPlace, OneLine(update.Present), null);
            Section(details, PrefixUpdate.NoLongerInSetup, OneLine(update.Dropped), "Stays installed");
            Section(details, PrefixUpdate.KeptAsSet, update.Preserved, null);
        }

        var notes = pending.Select(update => update.NothingToInstall).OfType<string>().Distinct().ToList();
        notes.Add(
            pending.Count == 1
                ? "Close any apps or DAWs using this prefix first."
                : "Close any apps or DAWs using these prefixes first.");
        notes.AddRange(pending
            .Where(update => update.Unrecorded is not null)
            .Select(update => PrefixUpdate.NotUpdatedBefore($"<b>{SecurityElement.Escape(update.Prefix)}</b>")));
        var verbs = pending.SelectMany(update => update.Winetricks).Distinct(StringComparer.Ordinal).ToList();

        if (verbs.Count > 0)
        {
            notes.Add(SecurityElement.Escape(Winetricks.Consent(verbs)));
        }

        var lines = Gtk.Box.New(Gtk.Orientation.Vertical, 6);

        foreach (var text in notes)
        {
            var note = Gtk.Label.New(null);
            note.SetMarkup(text);
            note.AddCssClass("caption");
            note.AddCssClass("dim-label");
            note.SetXalign(0);
            note.SetWrap(true);
            lines.Append(note);
        }

        details.Append(lines);

        var scrolled = Gtk.ScrolledWindow.New();
        scrolled.SetPolicy(Gtk.PolicyType.Never, Gtk.PolicyType.Automatic);
        scrolled.SetPropagateNaturalHeight(true);
        scrolled.SetMaxContentHeight(360);
        scrolled.SetSizeRequest(420, -1);
        scrolled.SetChild(details);
        return scrolled;
    }

    private static IReadOnlyList<string> OneLine(IReadOnlyList<string> items) =>
        items.Count == 0 ? [] : [string.Join(", ", items)];

    private static void Section(Gtk.Box details, string title, IReadOnlyList<string> lines, string? subtitle)
    {
        if (lines.Count == 0)
        {
            return;
        }

        var section = Adw.PreferencesGroup.New();
        section.SetTitle(title);

        foreach (var line in lines)
        {
            section.Add(Line(line, subtitle));
        }

        details.Append(section);
    }

    private static Adw.ActionRow Line(string text, string? subtitle)
    {
        var row = Adw.ActionRow.New();
        row.SetUseMarkup(false);
        row.SetTitle(text.TrimEnd('.'));

        if (subtitle is not null)
        {
            row.SetSubtitle(subtitle);
        }

        return row;
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
