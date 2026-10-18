using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Cabinet.Probes;

internal sealed partial class Desktop(TimeSpan? limit = null, Action<string>? note = null)
{
    public (int Code, string Output) Run(params string[] arguments) => Feed(null, arguments);

    public (int Code, string Output) Feed(string? input, params string[] arguments)
    {
        var info = new ProcessStartInfo(arguments[0])
        {
            RedirectStandardInput = input is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments.Skip(1))
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();

        if (input is not null)
        {
            process.StandardInput.Write(input);
            process.StandardInput.Close();
        }

        if (!process.WaitForExit(limit ?? Timeout.InfiniteTimeSpan))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            note?.Invoke($"timed out: {arguments[0]}");
            return (1, "");
        }

        return (process.ExitCode, output.Result);
    }

    public HashSet<string> Visible() =>
        Run("xdotool", "search", "--onlyvisible", "").Output
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

    public (int X, int Y)? Position(string window) =>
        PositionPattern().Match(Run("xdotool", "getwindowgeometry", window).Output) is { Success: true } at
            ? (Number(at.Groups[1]), Number(at.Groups[2]))
            : null;

    public (int Left, int Top, int Width, int Height)? Geometry(string window)
    {
        var said = Run("xdotool", "getwindowgeometry", window).Output;
        var at = PositionPattern().Match(said);
        var size = SizePattern().Match(said);

        return at.Success && size.Success
            ? (Number(at.Groups[1]), Number(at.Groups[2]), Number(size.Groups[1]), Number(size.Groups[2]))
            : null;
    }

    public int Area(string window) => Geometry(window) is { } found ? found.Width * found.Height : 0;

    public bool Managed(string window) =>
        Run("xprop", "-id", window, "WM_STATE").Output.Contains("window state", StringComparison.OrdinalIgnoreCase);

    public string? Capture(string window, string directory, string name)
    {
        var path = Path.Combine(directory, name + ".png");
        var raw = Path.Combine(directory, name + ".xwd");
        File.Delete(path);
        File.Delete(raw);

        Run(["xwd", "-silent", .. window == "root" ? ["-root"] : new[] { "-id", window }, "-out", raw]);

        if (!File.Exists(raw) || new FileInfo(raw).Length == 0)
        {
            File.Delete(raw);
            return null;
        }

        Run("magick", raw, "png:" + path);
        File.Delete(raw);
        return File.Exists(path) ? path : null;
    }

    public int Colours(string path) =>
        int.TryParse(Run("identify", "-format", "%k", path).Output.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var count)
            ? count
            : 0;

    public double Difference(string before, string after)
    {
        var said = Run(
            "magick", before, after, "-compose", "difference", "-composite",
            "-colorspace", "Gray", "-threshold", Fuzz, "-format", "%[fx:mean]", "info:").Output;
        return FloatPattern().Match(said) is { Success: true } found
               && double.TryParse(found.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var mean)
            ? mean
            : 0.0;
    }

    public const string Fuzz = "2%";

    public static int Number(Group group) => int.Parse(group.Value, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"Position:\s*(-?\d+),(-?\d+)")]
    private static partial Regex PositionPattern();

    [GeneratedRegex(@"Geometry:\s*(\d+)x(\d+)")]
    private static partial Regex SizePattern();

    [GeneratedRegex("([0-9.eE+-]+)")]
    private static partial Regex FloatPattern();
}
