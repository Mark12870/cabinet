using System.Diagnostics;
using Cabinet.Core;

namespace Cabinet.Runtime.Tests.Scenarios;

internal sealed class InstallPhases(string? download)
{
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Dictionary<string, TimeSpan> taken = [];
    private string phase = "prefix";
    private TimeSpan since;

    public void Heard(string line)
    {
        var next = Next(line);
        if (next == phase)
        {
            return;
        }

        Close();
        phase = next;
    }

    public IReadOnlyDictionary<string, TimeSpan> Finish()
    {
        Close();
        return taken;
    }

    private string Next(string line) => line switch
    {
        _ when line.StartsWith("Fetching Wine ", StringComparison.Ordinal) => "runner",
        _ when phase == "runner" && line.StartsWith("Downloaded ", StringComparison.Ordinal) => "prefix",
        _ when download is not null && line.StartsWith($"Downloading {download}", StringComparison.Ordinal) =>
            "download",
        _ when phase == "download" && line.StartsWith("Downloaded ", StringComparison.Ordinal) => "installer",
        _ when line.StartsWith($"Downloading {Dxvk.Url}", StringComparison.Ordinal) => "dxvk",
        "Bridging what is installed…" => "bridge",
        _ => phase,
    };

    private void Close()
    {
        var now = clock.Elapsed;
        taken[phase] = taken.GetValueOrDefault(phase) + (now - since);
        since = now;
    }
}
