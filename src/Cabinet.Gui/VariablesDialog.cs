using Cabinet.Core;

namespace Cabinet.Gui;

internal sealed class VariablesDialog(
    Gtk.Window window, Layout layout, string prefix, Action changed)
{
    private const string Hint =
        "Set for every Wine this prefix starts, here and in a bridged DAW. "
        + "An empty value unsets it.";

    private readonly Adw.Dialog dialog = Adw.Dialog.New();
    private readonly Gtk.Box list = Gtk.Box.New(Gtk.Orientation.Vertical, 12);
    private readonly PrefixSettings settings = new(layout);

    public void Present()
    {
        dialog.SetTitle($"Environment in {prefix}");
        dialog.SetContentWidth(560);
        dialog.SetContentHeight(460);

        var body = Ui.Page();
        body.Append(Ui.Scrolled(list));

        var view = Adw.ToolbarView.New();
        view.AddTopBar(Adw.HeaderBar.New());
        view.SetContent(body);
        dialog.SetChild(view);

        Fill();
        dialog.Present(window);
    }

    private void Fill()
    {
        Ui.Clear(list);

        var group = Adw.PreferencesGroup.New();
        group.SetTitle("Environment");
        group.SetDescription(Hint);

        var add = Ui.RowButton(Icons.New, "Add a variable");
        add.OnClicked += (_, _) => Ask();
        group.SetHeaderSuffix(add);

        var variables = settings.Variables(prefix);

        foreach (var (key, value) in variables.OrderBy(one => one.Key, StringComparer.Ordinal))
        {
            group.Add(Row(key, value));
        }

        if (variables.Count == 0)
        {
            var empty = Adw.ActionRow.New();
            empty.SetTitle("None yet");
            group.Add(empty);
        }

        list.Append(group);
    }

    private Adw.ActionRow Row(string key, string value)
    {
        var row = Adw.ActionRow.New();
        row.SetTitle(key);
        row.SetSubtitle(value.Length == 0 ? "unset for this prefix" : value);

        var remove = Ui.RowButton(Icons.Delete, $"Remove {key}", destructive: true);
        remove.OnClicked += (_, _) => Apply(key, null);
        row.AddSuffix(remove);

        return row;
    }

    private void Ask()
    {
        var ask = Adw.AlertDialog.New("Add a variable", Hint);

        var key = Adw.EntryRow.New();
        key.SetTitle("Name");

        var value = Adw.EntryRow.New();
        value.SetTitle("Value");

        var fields = Adw.PreferencesGroup.New();
        fields.SetMarginTop(12);
        fields.Add(key);
        fields.Add(value);

        ask.SetExtraChild(fields);
        ask.AddResponse("cancel", "Cancel");
        ask.AddResponse("ok", "Add");
        ask.SetResponseAppearance("ok", Adw.ResponseAppearance.Suggested);
        ask.SetDefaultResponse("ok");
        ask.SetCloseResponse("cancel");

        ask.OnResponse += (_, args) =>
        {
            var entered = key.GetText().Trim();

            if (args.Response == "ok" && entered.Length > 0)
            {
                Apply(entered, value.GetText());
            }
        };

        ask.Present(dialog);
    }

    private void Apply(string key, string? value) =>
        Operation.Run(
            dialog,
            value is null ? $"Removing {key}" : $"Setting {key}",
            _ => settings.SetVariable(prefix, key, value),
            () =>
            {
                Fill();
                changed();
            });
}
