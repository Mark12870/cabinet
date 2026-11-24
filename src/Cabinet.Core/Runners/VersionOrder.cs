using System.Globalization;
using System.Text.RegularExpressions;

namespace Cabinet.Core;

public sealed partial class VersionOrder : IComparer<string>
{
    public static readonly VersionOrder Instance = new();

    private VersionOrder()
    {
    }

    public int Compare(string? x, string? y)
    {
        var left = Parts(x);
        var right = Parts(y);

        for (var index = 0; index < Math.Min(left.Length, right.Length); index++)
        {
            var order = ComparePart(left[index], right[index]);

            if (order != 0)
            {
                return order;
            }
        }

        return left.Length.CompareTo(right.Length);
    }

    public static string FamilyOf(string name) =>
        string.Join('-', name.Split('-').Where(part => !part.Any(char.IsAsciiDigit)));

    private static int ComparePart(string left, string right) =>
        Number(left) is { } one && Number(right) is { } other
            ? one.CompareTo(other)
            : string.CompareOrdinal(left, right);

    private static ulong? Number(string part) =>
        ulong.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static string[] Parts(string? version) => Digits().Split(version ?? "");

    [GeneratedRegex(@"(\d+)")]
    private static partial Regex Digits();
}
