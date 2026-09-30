using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Cabinet.Core.Tests;

namespace Cabinet.Runtime.Tests.Scenarios;

internal sealed class Manager(
    Process launch, Display display, string shots, Func<Task<ScenarioProcessResult>> stop) : IDisposable
{
    private const double Likeness = 0.05;

    private static readonly TimeSpan Beat = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Draining = TimeSpan.FromSeconds(30);

    private readonly Task<string> said = launch.StandardOutput.ReadToEndAsync();
    private readonly Task<string> complained = launch.StandardError.ReadToEndAsync();
    private int taken;

    public string Window(string title, TimeSpan patience)
    {
        var deadline = DateTime.UtcNow + patience;
        while (DateTime.UtcNow < deadline)
        {
            var found = Xdotool("search", "--onlyvisible", "--name", $"^{Regex.Escape(title)}$").Output.Trim();
            if (found.Length > 0)
            {
                return found.Split('\n')[0];
            }

            Thread.Sleep(Beat);
        }

        var shown = Xdotool("search", "--onlyvisible", "--name", ".+", "getwindowname", "%@").Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        throw new TimeoutException($"no {title} window appeared within {patience}; the display showed [{string.Join(", ", shown)}]");
    }

    public void Click(string window, int x, int y) =>
        Xdotool("mousemove", "--window", window, $"{x}", $"{y}", "click", "1", "mousemove", "restore");

    public void Resize(string window, int width, int height) =>
        Xdotool("windowsize", window, $"{width}", $"{height}");

    public void Type(string window, int x, int y, string text)
    {
        Click(window, x, y);
        Thread.Sleep(Beat);
        Run("xdotool", text, "type", "--file", "-");
    }

    public void Press(string key) => Xdotool("key", key);

    public string Capture(string window, string name, string hidden = "")
    {
        var raw = Path.Combine(shots, $"{++taken:00}-{name}.xwd");
        var png = Path.ChangeExtension(raw, ".png");
        Run("xwd", null, "-id", window, "-silent", "-out", raw);
        Run("magick", null, [raw, .. hidden.Length > 0 ? ["-fill", "black", "-draw", $"rectangle {hidden}"] : Array.Empty<string>(), png]);
        File.Delete(raw);
        return png;
    }

    public bool Shows(string window, string reference, string region)
    {
        var corners = region.Split(' ', ',').Select(int.Parse).ToArray();
        var raw = Path.Combine(Path.GetTempPath(), $"cabinet-{Guid.NewGuid():N}.xwd");
        var crop = Path.ChangeExtension(raw, ".png");
        try
        {
            Run("xwd", null, "-id", window, "-silent", "-out", raw);
            Run("magick", null, raw, "-crop", $"{corners[2] - corners[0]}x{corners[3] - corners[1]}+{corners[0]}+{corners[1]}", "+repage", crop);
            var compared = Run("magick", null, "compare", "-metric", "RMSE", crop, Repo.Path(reference), "null:");
            var distance = Regex.Match(compared.Error, @"\(([0-9.e+-]+)\)");
            return distance.Success && double.Parse(distance.Groups[1].Value, CultureInfo.InvariantCulture) < Likeness;
        }
        finally
        {
            File.Delete(raw);
            File.Delete(crop);
        }
    }

    public void Until(Func<bool> done, string window, string what, TimeSpan patience, string? hidden = "")
    {
        var deadline = DateTime.UtcNow + patience;
        while (!done())
        {
            if (DateTime.UtcNow >= deadline)
            {
                if (hidden is not null)
                {
                    Capture(window, "gave-up", hidden);
                }

                throw new TimeoutException($"{what} did not happen within {patience}");
            }

            Thread.Sleep(Beat);
        }
    }

    public async Task<string> Close()
    {
        var stopped = await stop();
        using var deadline = new CancellationTokenSource(Patience);
        await launch.WaitForExitAsync(deadline.Token);
        await Task.WhenAll(said, complained);
        var log = Keep($"stop:\n{stopped.Said}");
        Assert.True(stopped.ExitCode == 0, log);
        return log;
    }

    public async Task WaitForExit(TimeSpan patience, string logName)
    {
        using var deadline = new CancellationTokenSource(patience);
        try
        {
            await launch.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"the manager did not exit within {patience}");
        }

        await Task.WhenAll(said, complained);
        Keep("exited", logName);
    }

    public void Dispose()
    {
        if (!launch.HasExited)
        {
            launch.Kill(entireProcessTree: true);
            launch.WaitForExit();
            if (Task.WaitAll([said, complained], Draining))
            {
                Keep("killed before it closed");
            }
        }

        launch.Dispose();
    }

    private string Keep(string ending, string name = "launch")
    {
        var log = $"{said.Result}\nstderr:\n{complained.Result}\nexit: {launch.ExitCode}\n{ending}";
        File.WriteAllText(Path.Combine(shots, $"{name}.log"), log);
        return log;
    }

    private ScenarioProcessResult Xdotool(params string[] arguments) => Run("xdotool", null, arguments);

    private ScenarioProcessResult Run(string file, string? input, params string[] arguments)
    {
        var info = new ProcessStartInfo(file)
        {
            RedirectStandardInput = input is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        display.Configure(info);
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException($"could not start {file}");
        if (input is not null)
        {
            process.StandardInput.Write(input);
            process.StandardInput.Close();
        }

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ScenarioProcessResult(process.ExitCode, output, error);
    }
}
