using System.Net;
using System.Security.Authentication;
using System.Text.RegularExpressions;

namespace Cabinet.Ci;

internal static partial class VendorOutage
{
    public const string Record = "unavailable.txt";

    public static string? InInstall(string said) =>
        said.Contains("could not download", StringComparison.Ordinal) && CurlFailure().Match(said) is { Success: true } found
            ? found.Value.Trim()
            : null;

    public static string? Of(Exception exception) => exception switch
    {
        HttpRequestException { StatusCode: null, InnerException: not AuthenticationException } refused => refused.Message,
        HttpRequestException { StatusCode: { } status } answered when Transient(status) => answered.Message,
        TaskCanceledException { InnerException: TimeoutException } => "the vendor's server did not answer in time",
        _ => null,
    };

    private static bool Transient(HttpStatusCode status) =>
        status == HttpStatusCode.TooManyRequests || (int)status >= 500;

    [GeneratedRegex(@"curl: \((?:6|7|28|35|52|56)\)[^\n]*|curl: \(22\)[^\n]*error: (?:429|5\d\d)[^\n]*")]
    private static partial Regex CurlFailure();
}
