using System.Text;
using System.Text.Json;

namespace Cabinet.Core;

internal static class ModarttTrial
{
    private const string Host = "www.modartt.com";
    private const string Api = $"https://{Host}/api/0/download";

    public static bool Serves(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri is { Scheme: "https", Host: Host, AbsolutePath: "/try" };

    public static string FileName(string url) =>
        new Uri(url).Query.TrimStart('?').Split('&')
            .Select(pair => pair.Split('=', 2))
            .FirstOrDefault(pair => pair is ["file", { Length: > 0 }])?[1]
        ?? throw new InvalidOperationException($"{url} names no file= to download");

    public static string Resolve(
        Http http, string url, string cookies, CancellationToken cancellationToken = default)
    {
        var file = FileName(url);
        var signature = Signature(http.Text(url, cancellationToken, cookies), file);
        var (link, reason) = Answer(
            http.PostJson(Api, Request(file, signature), cancellationToken, cookies));

        return link ?? throw new InvalidOperationException(
            $"{Host} would not hand out {file} — {reason}");
    }

    private static (string? Link, string Reason) Answer(string text)
    {
        const string Unanswered = "no download link in its answer";

        try
        {
            using var reply = JsonDocument.Parse(text);
            var answer = reply.RootElement;

            if (answer.ValueKind != JsonValueKind.Object)
            {
                return (null, Unanswered);
            }

            if (answer.TryGetProperty("result", out var result)
                && result.ValueKind == JsonValueKind.Object
                && result.TryGetProperty("url", out var link)
                && link.ValueKind == JsonValueKind.String
                && Uri.TryCreate(link.GetString(), UriKind.Absolute, out var uri)
                && uri is { Scheme: "https", Host: Host })
            {
                return (uri.OriginalString, "");
            }

            return answer.TryGetProperty("error", out var error)
                   && error.ValueKind == JsonValueKind.Object
                   && error.TryGetProperty("message", out var message)
                   && message.ValueKind == JsonValueKind.String
                ? (null, message.GetString()!)
                : (null, Unanswered);
        }
        catch (JsonException)
        {
            return (null, Unanswered);
        }
    }

    private static string Signature(string page, string file)
    {
        var marker = $"download?file={file}&amp;signature=";
        var at = page.IndexOf(marker, StringComparison.Ordinal);
        var signature = at < 0
            ? ""
            : new string(page[(at + marker.Length)..]
                .TakeWhile(character => char.IsAsciiHexDigit(character) || character == ':')
                .ToArray());

        return signature.Length > 0
            ? signature
            : throw new InvalidOperationException(
                $"{Host}'s trial page no longer offers {file}");
    }

    private static string Request(string file, string signature)
    {
        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            writer.WriteNumber("id", 1);
            writer.WriteString("method", "download");
            writer.WriteStartObject("params");
            writer.WriteString("file", file);
            writer.WriteString("signature", signature);
            writer.WriteString("get", "url");
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
