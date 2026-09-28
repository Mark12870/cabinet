using Cabinet.Core;

namespace Cabinet.Gui;

internal sealed class RunnersPage
{
    private readonly Layout layout;
    private readonly IProcessRunner runner;
    private readonly Gtk.Window window;
    private readonly Action changed;
    private readonly Operation operations;
    private readonly Gtk.Box list = Gtk.Box.New(Gtk.Orientation.Vertical, 12);
    private readonly RefreshGeneration generation = new();

    public RunnersPage(
        Layout layout,
        IProcessRunner runner,
        Gtk.Window window,
        Action changed,
        Operation operations)
    {
        this.layout = layout;
        this.runner = runner;
        this.window = window;
        this.changed = changed;
        this.operations = operations;

        var page = Ui.Page();
        page.Append(Ui.Scrolled(list));
        Widget = page;
    }

    public Gtk.Widget Widget { get; }

    public void Refresh()
    {
        var current = generation.Next();
        Task.Run(() =>
        {
            var runners = new Runners(layout, runner);
            return runners.List()
                .Select(installed => new DescribedRunner(
                    installed, Describe(runners, installed)))
                .ToList();
        }).ContinueWith(task => Ui.OnMainLoop(() =>
        {
            if (!generation.IsCurrent(current))
            {
                return;
            }

            if (task.IsFaulted)
            {
                ShowFailure(task.Exception!.InnerException!.Message);
                return;
            }

            Show(task.Result, current);
        }));
    }

    private void Show(IReadOnlyList<DescribedRunner> installedRunners, int current)
    {
        Ui.Clear(list);
        var group = Adw.PreferencesGroup.New();
        group.SetTitle("Installed");

        group.SetHeaderSuffix(Actions());

        var described = new List<(Runner Runner, Adw.ActionRow Row, string Subtitle)>();

        foreach (var describedRunner in installedRunners)
        {
            var installed = describedRunner.Runner;
            var subtitle = describedRunner.Subtitle;
            var row = Adw.ActionRow.New();
            row.SetTitle(installed.Name);
            row.SetSubtitle(subtitle);
            row.AddPrefix(Gtk.Image.NewFromIconName(Icons.Runners));

            if (!installed.Bundled)
            {
                var remove = Ui.IconButton(Icons.Delete, "Delete this runner");
                remove.SetValign(Gtk.Align.Center);
                remove.OnClicked += (_, _) => Ui.Guard(() => Remove(installed.Name));
                row.AddSuffix(remove);
            }

            group.Add(row);
            described.Add((installed, row, subtitle));
        }

        list.Append(group);
        ShowVersions(described, current);
    }

    private void ShowFailure(string message)
    {
        Ui.Clear(list);
        var failed = Adw.StatusPage.New();
        failed.SetIconName(Icons.Fail);
        failed.SetTitle("Could not read Wine runners");
        failed.SetDescription(message);
        list.Append(failed);
    }

    private Gtk.Box Actions()
    {
        var add = Ui.RowButton(Icons.Archive, "Unpack a Wine build you already have");
        add.OnClicked += (_, _) => Ui.Guard(ChooseArchive);

        var available = Ui.RowButton(Icons.Download, "Wine versions");
        available.OnClicked += (_, _) => Ui.Guard(ShowAvailable);

        var box = Gtk.Box.New(Gtk.Orientation.Horizontal, 6);
        box.Append(available);
        box.Append(add);
        return box;
    }

    private void ShowVersions(
        IReadOnlyList<(Runner Runner, Adw.ActionRow Row, string Subtitle)> described,
        int current) =>
        Task.Run(() =>
        {
            try
            {
                var runners = new Runners(layout, runner);

                foreach (var (installed, row, subtitle) in described
                             .Where(shown => shown.Runner.Usable))
                {
                    var version = runners.Version(installed);
                    Ui.OnMainLoop(() =>
                    {
                        if (generation.IsCurrent(current))
                        {
                            row.SetSubtitle($"{subtitle}  ·  {version}");
                        }
                    });
                }
            }
            catch (Exception exception)
            {
                Ui.OnMainLoop(() =>
                {
                    if (generation.IsCurrent(current))
                    {
                        Ui.Report(window, "Could not read Wine versions", exception.Message);
                    }
                });
            }
        });

    private static string Describe(Runners runners, Runner installed)
    {
        var used = runners.InUseBy(installed.Name);
        var by = used.Count == 0 ? "unused" : "used by " + string.Join(", ", used);

        return installed.Usable ? $"{by}  ·  {(installed.Multilib ? "32+64" : "64-bit only")}" : "broken";
    }

    private void ShowAvailable()
    {
        var cancellation = new CancellationTokenSource();
        var open = true;
        var dialog = Adw.Dialog.New();
        dialog.SetTitle("Wine versions");
        dialog.SetContentWidth(560);
        dialog.SetContentHeight(520);

        var body = Ui.Page();
        var group = Adw.PreferencesGroup.New();
        group.SetTitle("Available upstream");
        group.SetDescription("Reading Bottles' component index…");
        body.Append(Ui.Scrolled(group));

        var view = Adw.ToolbarView.New();
        view.AddTopBar(Adw.HeaderBar.New());
        view.SetContent(body);
        dialog.SetChild(view);
        dialog.OnClosed += (_, _) =>
        {
            open = false;
            cancellation.Cancel();
        };
        dialog.Present(window);

        Task.Run(() =>
        {
            try
            {
                var families = new RunnerIndex(runner).Available(cancellation.Token)
                    .GroupBy(release => release.Family);
                Ui.OnMainLoop(() =>
                {
                    if (!open)
                    {
                        return;
                    }

                    group.SetDescription(RunnerIndex.Provenance);

                    foreach (var family in families)
                    {
                        group.Add(FamilyRow(family, dialog));
                    }
                });
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                Ui.OnMainLoop(() =>
                {
                    if (open)
                    {
                        group.SetDescription(exception.Message);
                    }
                });
            }
        });
    }

    private Adw.ExpanderRow FamilyRow(
        IGrouping<RunnerFamily, RunnerRelease> family, Adw.Dialog dialog)
    {
        var expander = Adw.ExpanderRow.New();
        expander.SetTitle(family.Key.Label);
        expander.SetSubtitle(family.Key.Description);
        expander.AddPrefix(Gtk.Image.NewFromIconName(Icons.Runners));

        foreach (var release in family)
        {
            expander.AddRow(AvailableRow(release, dialog));
        }

        return expander;
    }

    private Adw.ActionRow AvailableRow(RunnerRelease release, Adw.Dialog dialog)
    {
        var row = Adw.ActionRow.New();
        row.SetTitle(release.Version);
        row.SetSubtitle(RunnerIndex.IsPinned(release) ? $"{release.Name} · pinned" : release.Name);

        var install = Ui.IconButton(Icons.Download, $"Install {release.Name}");
        install.SetValign(Gtk.Align.Center);
        install.OnClicked += (_, _) => Ui.Guard(() =>
        {
            dialog.ForceClose();
            operations.RunCancellable(
                $"Installing {release.Name}",
                (output, progress, token) =>
                    new Runners(layout, runner).Install(release, output, progress, token),
                changed);
        });

        row.AddSuffix(install);
        return row;
    }

    private void ChooseArchive() =>
        Ui.ChooseFile(window, "Choose a Wine archive", path =>
            operations.RunCancellable(
                $"Unpacking {Path.GetFileName(path)}",
                (output, token) => new Runners(layout, runner).Add(
                    path, onOutput: output, cancellationToken: token),
                changed));

    private void Remove(string name) =>
        Ui.Confirm(
            window,
            $"Delete {name}?",
            "The runner's files are deleted. Getting it back means downloading or unpacking "
            + "it again.",
            "Delete",
            () => operations.Run(
                $"Deleting {name}",
                _ => new Runners(layout, runner).Remove(name),
                changed),
            Adw.ResponseAppearance.Destructive);

    private sealed record DescribedRunner(Runner Runner, string Subtitle);
}
