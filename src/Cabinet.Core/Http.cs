using System.Globalization;

namespace Cabinet.Core;

public sealed class Http(IProcessRunner runner)
{
    public string Text(string url)
    {
        var result = runner.Run(
            "curl", ["-sSL", "--retry", "2", "--max-time", "30", "-D", "/dev/stderr", url]);

        var host = new Uri(url).Host;

        if (!result.Ok)
        {
            throw new InvalidOperationException($"could not reach {host} — {LastLine(result.Stderr)}");
        }

        var status = StatusOf(result.Stderr);

        return status is null or "200"
            ? result.Stdout
            : throw new InvalidOperationException(
                $"{host} answered {status} for {new Uri(url).AbsolutePath}");
    }

    public void ToFile(
        string url,
        string target,
        Action<string>? onOutput = null,
        Action<double>? onProgress = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        onOutput?.Invoke($"Downloading {url}");

        var reported = -1d;
        var announced = 0;

        var fetched = runner.Run(
            "curl",
            ["-fL", "--progress-bar", "--retry", "2", "-o", target, url],
            onOutput: line =>
            {
                if (FractionOf(line) is not { } fraction)
                {
                    if (!Drawn(line))
                    {
                        onOutput?.Invoke(line);
                    }

                    return;
                }

                if (fraction == reported)
                {
                    return;
                }

                reported = fraction;

                if (onProgress is not null)
                {
                    onProgress(fraction);
                    return;
                }

                var tens = (int)(fraction * 10);

                if (tens != announced)
                {
                    announced = tens;
                    onOutput?.Invoke($"Downloading… {tens * 10}%");
                }
            });

        if (!fetched.Ok)
        {
            throw new InvalidOperationException($"could not download {url}");
        }

        onOutput?.Invoke($"Downloaded {new FileInfo(target).Length / 1024 / 1024} MB");
    }

    private static bool Drawn(string line) => line.Trim(' ', '#', '=', 'O', '-').Length == 0;

    private static double? FractionOf(string line) =>
        line.Split(' ', StringSplitOptions.RemoveEmptyEntries) is [.., var last]
        && last.EndsWith('%')
        && double.TryParse(last[..^1], CultureInfo.InvariantCulture, out var percent)
            ? Math.Clamp(percent / 100, 0, 1)
            : null;

    private static string? StatusOf(string headers)
    {
        string? status = null;

        foreach (var line in headers.Split('\n'))
        {
            if (line.StartsWith("HTTP/", StringComparison.Ordinal)
                && line.Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: > 1 } fields)
            {
                status = fields[1];
            }
        }

        return status;
    }

    private static string LastLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            is { Length: > 0 } lines
            ? lines[^1]
            : "curl said nothing";
}
