namespace Cabinet.Core;

public sealed record PluginChange(IReadOnlyList<string> Appeared, IReadOnlyList<string> Gone);

public sealed class PluginWatch(IReadOnlySet<string> bridged)
{
    private IReadOnlySet<string> settled = bridged;
    private IReadOnlySet<string> seen = bridged;

    public PluginChange? Changed(IReadOnlySet<string> now) => Since(now, now.SetEquals(seen));

    public PluginChange? Closed(IReadOnlySet<string> now) => Since(now, true);

    public bool Pending => !seen.SetEquals(settled);

    public void Accept() => settled = seen;

    private PluginChange? Since(IReadOnlySet<string> now, bool steady)
    {
        seen = now;

        if (!steady)
        {
            return null;
        }

        var appeared = Sorted(now.Except(settled, StringComparer.Ordinal));
        var gone = Sorted(settled.Except(now, StringComparer.Ordinal));

        return appeared.Count > 0 || gone.Count > 0 ? new PluginChange(appeared, gone) : null;
    }

    private static IReadOnlyList<string> Sorted(IEnumerable<string> bundles) =>
        [.. bundles.OrderBy(path => path, StringComparer.Ordinal)];
}
