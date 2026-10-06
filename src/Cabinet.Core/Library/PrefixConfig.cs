namespace Cabinet.Core;

public sealed record PrefixConfig(
    string Version,
    int Revision,
    string? Runner,
    bool Dxvk,
    SyncMode Sync,
    IReadOnlyList<string> Winetricks,
    IReadOnlyDictionary<string, string> Env,
    bool Desktop)
{
    public const string FilePrefix = "prefix-";

    public string Label => $"{Version}, revision {Revision}";

    public static PrefixConfig For(IReadOnlyList<PrefixConfig> configs, string? version)
    {
        var ordered = configs.OrderBy(config => config.Version, VersionOrder.Instance).ToList();

        return version is null
            ? ordered[^1]
            : ordered.LastOrDefault(config => VersionOrder.Instance.Compare(config.Version, version) <= 0)
              ?? ordered[0];
    }

    public static PrefixConfig First(IReadOnlyList<PrefixConfig> configs) =>
        configs.OrderBy(config => config.Version, VersionOrder.Instance).First();

    private sealed class VersionOrder : IComparer<string?>
    {
        public static readonly VersionOrder Instance = new();

        public int Compare(string? first, string? second)
        {
            var left = Numbers(first);
            var right = Numbers(second);

            for (var at = 0; at < Math.Max(left.Count, right.Count); at++)
            {
                var order = (at < left.Count ? left[at] : 0).CompareTo(at < right.Count ? right[at] : 0);

                if (order != 0)
                {
                    return order;
                }
            }

            return 0;
        }

        private static IReadOnlyList<long> Numbers(string? version) =>
            [.. (version ?? "")
                .Split(['.', '-', '_', ' '], StringSplitOptions.RemoveEmptyEntries)
                .Select(part => long.TryParse(new string([.. part.TakeWhile(char.IsAsciiDigit)]), out var number)
                    ? number
                    : 0)];
    }
}
