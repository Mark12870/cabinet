namespace Cabinet.Core;

public sealed class PluginWatch(IReadOnlySet<string> bridged)
{
    private IReadOnlySet<string> settled = bridged;
    private IReadOnlySet<string> seen = bridged;

    public IReadOnlyList<string>? Appeared(IReadOnlySet<string> now) =>
        Since(now, now.SetEquals(seen));

    public IReadOnlyList<string>? Closed(IReadOnlySet<string> now) => Since(now, true);

    private IReadOnlyList<string>? Since(IReadOnlySet<string> now, bool steady)
    {
        seen = now;

        if (!steady)
        {
            return null;
        }

        var appeared = now.Except(settled, StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        settled = now;
        return appeared.Count > 0 ? appeared : null;
    }
}
