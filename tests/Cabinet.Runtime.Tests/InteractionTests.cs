using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Cabinet.Runtime.Tests;

public sealed class InteractionTests : IDisposable
{
    private readonly RuntimeTestLock runtimeLock = RuntimeTestLock.Acquire();

    private const int Colours = 16;
    private const double Reaction = 0.0005;
    private const int IdleMilliseconds = 250;
    private const int CallMilliseconds = 10000;

    private static readonly ConcurrentDictionary<string, Lazy<Measured>> Seen = new();

    private static readonly EditorCase[] Catalogue =
    [
        new("decent-sampler", "vst2", Fixtures.Native(".vst", "DecentSampler.so"),
            Known.NoInput | Known.Stalls),
        new("surge-xt", "clap", Fixtures.Native(".clap", "Surge XT.clap"), Known.None),
        new("sitala", "vst2", Fixtures.Windows(".vst", "Sitala.so"), Known.None),
        new("valhalla-supermassive", "vst2",
            Fixtures.Windows(".vst", "ValhallaSupermassive_x64.so"), Known.None),
        new("surge-xt-bridged", "clap", Fixtures.Windows(".clap", "Surge XT.clap"), Known.NoInput),
        new("sine-player", "vst3", Fixtures.Windows(".vst3", "SINE Player.vst3"), Known.NoInput),
    ];

    public static IEnumerable<object[]> EveryCase() => Group(_ => true);

    public static IEnumerable<object[]> RespondingCases() =>
        Group(plugin => !plugin.Known.HasFlag(Known.NoInput));

    public static IEnumerable<object[]> UnresponsiveCases() =>
        Group(plugin => plugin.Known.HasFlag(Known.NoInput));

    public static IEnumerable<object[]> SteadyCases() =>
        Group(plugin => !plugin.Known.HasFlag(Known.Stalls));

    public static IEnumerable<object[]> StallingCases() =>
        Group(plugin => plugin.Known.HasFlag(Known.Stalls));

    [Theory]
    [MemberData(nameof(EveryCase))]
    public void AnEditorDrawsItsInterface(EditorCase plugin)
    {
        var seen = Measure(plugin);

        Assert.True(
            seen.Colours >= Colours,
            $"{plugin.Name}'s editor window is {seen.Size} but holds only {seen.Colours} distinct "
            + "colours, so the frame the DAW shows is blank. The plugin may be drawing somewhere "
            + $"else entirely; no log records this. The capture is in {seen.Shots}.");
    }

    [Theory]
    [MemberData(nameof(RespondingCases))]
    public void AnEditorRespondsToThePointer(EditorCase plugin)
    {
        var seen = Measure(plugin);

        Assert.True(
            seen.Reaction > seen.Noise && seen.Reaction >= Reaction,
            $"{plugin.Name}'s editor draws but does not react: sweeping, clicking and dragging "
            + $"across it changed the frame by {seen.Reaction:F6}, against {seen.Noise:F6} measured "
            + "with the pointer held still. The editor is receiving no input, which is a plugin a "
            + $"user cannot operate. The captures are in {seen.Shots}.");
    }

    [Theory]
    [MemberData(nameof(UnresponsiveCases))]
    public void AnEditorKnownNotToRespondStillDoesNot(EditorCase plugin)
    {
        var seen = Measure(plugin);

        Assert.True(
            seen.Reaction < Reaction,
            $"{plugin.Name} now reacts to the pointer ({seen.Reaction:F6}). That defect is fixed: "
            + "take Known.NoInput off its case so the reaction is enforced from now on, and take "
            + "it out of the known defects in TESTS.md.");
    }

    [Theory]
    [MemberData(nameof(SteadyCases))]
    public void AHostKeepsRespondingWhileAnEditorIsOpen(EditorCase plugin)
    {
        var seen = Measure(plugin);

        Assert.True(
            seen.Idle <= IdleMilliseconds,
            $"the host stopped servicing its loop for {seen.Idle} ms while {plugin.Name}'s editor "
            + "was open. That is the DAW going unresponsive under the user's hands.");

        Assert.True(
            seen.Slowest <= CallMilliseconds,
            $"{plugin.Name} held the host for {seen.Slowest} ms in one call (open {seen.Open} ms, "
            + $"close {seen.Close} ms, remove {seen.Remove} ms, shutdown {seen.Shutdown} ms). "
            + "A stall in shutdown is the DAW freeze described in AGENTS.md.");
    }

    [Theory]
    [MemberData(nameof(StallingCases))]
    public void APluginKnownToStallTheHostStillDoes(EditorCase plugin)
    {
        var seen = Measure(plugin);

        Assert.True(
            seen.Idle > IdleMilliseconds,
            $"{plugin.Name} no longer stalls the host ({seen.Idle} ms). That defect is fixed: take "
            + "Known.Stalls off its case so the bound is enforced from now on, and take it out of "
            + "the known defects in TESTS.md.");
    }

    [Theory]
    [MemberData(nameof(EveryCase))]
    public void AnEditorSurvivesBeingOpenedAndClosed(EditorCase plugin)
    {
        var seen = Measure(plugin);

        Assert.True(
            seen.Reopened,
            $"{plugin.Name}'s editor did not come back when it was opened a second time, so the "
            + "user gets one look at it per session.");

        Assert.DoesNotContain("crashed while being torn down", seen.Said, StringComparison.Ordinal);
    }

    private static IEnumerable<object[]> Group(Func<EditorCase, bool> wanted) =>
        Catalogue.Where(wanted).Select(plugin => new object[] { plugin });

    private static Measured Measure(EditorCase plugin) =>
        Seen.GetOrAdd(plugin.Name, _ => new Lazy<Measured>(() => Probe(plugin))).Value;

    private static Measured Probe(EditorCase plugin)
    {
        var shots = EditorProbe.Artefacts(plugin.Name);
        var said = EditorProbe.Run(
            "editor-interaction.py", plugin.Name, plugin.Plugin, plugin.Format, shots).Said;

        var seen = Regex.Match(
            said,
            @"EDITOR=(\S+) SIZE=(\S+) COLOURS=(\d+) REACTION=([0-9.]+) NOISE=([0-9.]+) "
            + @"IDLE_MS=(\d+) OPEN_MS=(\d+) CLOSE_MS=(\d+) REMOVE_MS=(\d+) SHUTDOWN_MS=(\d+) "
            + @"REOPEN=(yes|no)");

        Assert.True(
            seen.Success,
            $"the interaction probe did not report a measurement for {plugin.Name}: {said}");

        int Count(int group) => int.Parse(seen.Groups[group].Value, CultureInfo.InvariantCulture);
        double Ratio(int group) =>
            double.Parse(seen.Groups[group].Value, CultureInfo.InvariantCulture);

        return new Measured(
            seen.Groups[2].Value,
            Count(3),
            Ratio(4),
            Ratio(5),
            Count(6),
            Count(7),
            Count(8),
            Count(9),
            Count(10),
            seen.Groups[11].Value == "yes",
            shots,
            said);
    }

    public void Dispose() => runtimeLock.Dispose();
}

[Flags]
public enum Known
{
    None = 0,
    NoInput = 1,
    Stalls = 2,
}

public sealed record EditorCase(string Name, string Format, string Plugin, Known Known)
{
    public override string ToString() => Name;
}

internal sealed record Measured(
    string Size,
    int Colours,
    double Reaction,
    double Noise,
    int Idle,
    int Open,
    int Close,
    int Remove,
    int Shutdown,
    bool Reopened,
    string Shots,
    string Said)
{
    public int Slowest => Math.Max(Math.Max(Open, Close), Math.Max(Remove, Shutdown));
}
