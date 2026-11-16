namespace Cabinet.Ci;

internal static class ScenarioFormats
{
    public static IReadOnlyList<string> Tested(IReadOnlyList<string> declared) =>
        [.. declared.Where(format => format != "VST2" || !declared.Contains("VST3"))];
}
