using System.Globalization;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace Cabinet.Runtime.Tests.Scenarios;

internal sealed class ImapOtp(string sender, string evidence)
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PollLimit = TimeSpan.FromSeconds(30);
    private static readonly Regex LiteralLength = new(@"\{([0-9]+)\+?\}$", RegexOptions.CultureInvariant);
    private static readonly Regex UidSearch = new(@"(?m)^\* SEARCH(?: ([0-9 ]*))?$", RegexOptions.CultureInvariant);
    private static readonly Regex LabelledCode = new(
        @"(?i)(?:\bcode\b[^\d\r\n]{0,40}(?<code>(?<![\d#])\d{6}(?!\d))|(?<code>(?<![\d#])\d{6}(?!\d))[^\d\r\n]{0,40}\bcode\b)",
        RegexOptions.CultureInvariant);
    private static readonly Regex InternalDate = new(
        "INTERNALDATE \" ?(?<time>[^\"]+) (?<zone>[+-][0-9]{2})(?<minutes>[0-9]{2})\"", RegexOptions.CultureInvariant);
    private static readonly Regex SixDigits = new(@"(?<![\d#])\d{6}(?!\d)", RegexOptions.CultureInvariant);

    private readonly IReadOnlyDictionary<string, string> credentials = Credentials.Read();

    public async Task<(string Code, uint Uid)> WaitForCodeAsync(
        TimeSpan validity,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        var oldest = DateTimeOffset.UtcNow - validity;
        var rejected = new HashSet<uint>();

        while (true)
        {
            using var poll = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            poll.CancelAfter(PollLimit);
            try
            {
                if (await Newest(oldest, rejected, poll.Token) is { } found)
                {
                    return found;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Log($"poll gave up after {PollLimit.TotalSeconds:F0}s");
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException($"No {sender} verification code arrived.");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    public async Task DeleteAsync(uint uid, CancellationToken cancellationToken = default)
    {
        await using var mailbox = await Connect(cancellationToken);
        await mailbox.Delete(uid, cancellationToken);
        Log($"deleted {uid}");
    }

    internal static string? CodeFromMessage(string message, string sender)
    {
        if (!Regex.IsMatch(message, $@"(?im)^From:[^\r\n]*{Regex.Escape(sender)}"))
        {
            return null;
        }

        var labelled = LabelledCode.Match(message);
        if (labelled.Success)
        {
            return labelled.Groups["code"].Value;
        }

        var candidates = SixDigits.Matches(message);
        return candidates.Count == 1 ? candidates[0].Value : null;
    }

    private async Task<(string Code, uint Uid)?> Newest(
        DateTimeOffset oldest,
        HashSet<uint> rejected,
        CancellationToken cancellationToken)
    {
        await using var mailbox = await Connect(cancellationToken);
        var uids = await mailbox.UidsSince(oldest, cancellationToken);
        Log($"since {oldest:HH:mm:ss}: [{string.Join(' ', uids)}]");
        foreach (var uid in uids.Where(uid => !rejected.Contains(uid)).OrderDescending())
        {
            if (await mailbox.Received(uid, cancellationToken) is not { } received || received < oldest)
            {
                rejected.Add(uid);
                continue;
            }

            if (CodeFromMessage(await mailbox.Fetch(uid, cancellationToken), sender) is { } code)
            {
                Log($"code in {uid} received {received:HH:mm:ss}");
                return (code, uid);
            }

            rejected.Add(uid);
        }

        return null;
    }

    private Task<Mailbox> Connect(CancellationToken cancellationToken) => Mailbox.Connect(
        credentials["EMAIL_IMAP_HOST"],
        credentials["EMAIL"],
        credentials["EMAIL_IMAP_PASSWORD"],
        cancellationToken);

    private void Log(string line)
    {
        Directory.CreateDirectory(evidence);
        File.AppendAllText(Path.Combine(evidence, "imap.log"), $"{DateTime.UtcNow:HH:mm:ss} {line}\n");
    }

    private sealed class Mailbox : IAsyncDisposable
    {
        private readonly TcpClient client;
        private readonly SslStream stream;
        private readonly WireReader reader;
        private int sequence;

        private Mailbox(TcpClient client, SslStream stream)
        {
            this.client = client;
            this.stream = stream;
            reader = new WireReader(stream);
        }

        public static async Task<Mailbox> Connect(
            string host,
            string username,
            string password,
            CancellationToken cancellationToken)
        {
            var client = new TcpClient();
            try
            {
                await client.ConnectAsync(host, 993, cancellationToken);
                var stream = new SslStream(client.GetStream());
                await stream.AuthenticateAsClientAsync(
                    new SslClientAuthenticationOptions { TargetHost = host }, cancellationToken);
                var mailbox = new Mailbox(client, stream);
                var greeting = await mailbox.reader.ReadLine(cancellationToken);
                if (greeting.StartsWith("* PREAUTH", StringComparison.OrdinalIgnoreCase))
                {
                    await mailbox.SelectInbox(cancellationToken);
                    return mailbox;
                }

                if (!greeting.StartsWith("* OK", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("The IMAP server did not greet the client.");
                }

                await mailbox.Command($"LOGIN {Quote(username)} {Quote(password)}", cancellationToken);
                await mailbox.SelectInbox(cancellationToken);
                return mailbox;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        public Task<uint[]> UidsSince(DateTimeOffset time, CancellationToken cancellationToken) =>
            Search(
                $"UID SEARCH SINCE {time.UtcDateTime.AddDays(-1).ToString("d-MMM-yyyy", CultureInfo.InvariantCulture)}",
                cancellationToken);

        public async Task<DateTimeOffset?> Received(uint uid, CancellationToken cancellationToken)
        {
            var date = InternalDate.Match(await Command($"UID FETCH {uid} (INTERNALDATE)", cancellationToken));
            return !date.Success ? null : DateTimeOffset.ParseExact(
                $"{date.Groups["time"].Value} {date.Groups["zone"].Value}:{date.Groups["minutes"].Value}",
                "d-MMM-yyyy HH:mm:ss zzz",
                CultureInfo.InvariantCulture);
        }

        public Task<string> Fetch(uint uid, CancellationToken cancellationToken) =>
            Command($"UID FETCH {uid} (BODY.PEEK[])", cancellationToken);

        public async Task Delete(uint uid, CancellationToken cancellationToken)
        {
            await Command($@"UID STORE {uid} +FLAGS.SILENT (\Deleted)", cancellationToken);
            await Command($"UID EXPUNGE {uid}", cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await stream.DisposeAsync();
            client.Dispose();
        }

        private async Task SelectInbox(CancellationToken cancellationToken)
        {
            await Command("SELECT INBOX", cancellationToken);
        }

        private async Task<uint[]> Search(string command, CancellationToken cancellationToken)
        {
            var response = await Command(command, cancellationToken);
            var result = UidSearch.Match(response);
            return result.Success && result.Groups[1].Length > 0
                ? result.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(value => uint.Parse(value, CultureInfo.InvariantCulture))
                    .ToArray()
                : [];
        }

        private async Task<string> Command(string command, CancellationToken cancellationToken)
        {
            var tag = $"A{++sequence:D4}";
            var request = Encoding.ASCII.GetBytes($"{tag} {command}\r\n");
            await stream.WriteAsync(request, cancellationToken);
            var response = new StringBuilder();

            while (true)
            {
                var line = await reader.ReadLine(cancellationToken);
                response.AppendLine(line);
                var literal = LiteralLength.Match(line);
                if (literal.Success)
                {
                    var length = int.Parse(literal.Groups[1].Value, CultureInfo.InvariantCulture);
                    if (length > 10_000_000)
                    {
                        throw new InvalidOperationException("The IMAP message was too large to read.");
                    }

                    response.Append(Encoding.Latin1.GetString(await reader.ReadExactly(length, cancellationToken)));
                    response.AppendLine(await reader.ReadLine(cancellationToken));
                }

                if (!line.StartsWith(tag + " ", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!line.StartsWith(tag + " OK", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("The IMAP request failed.");
                }

                return response.ToString();
            }
        }

        private static string Quote(string value) => $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
    }

    private sealed class WireReader(Stream stream)
    {
        private readonly byte[] buffer = new byte[8192];
        private int offset;
        private int length;

        public async Task<string> ReadLine(CancellationToken cancellationToken)
        {
            var line = new List<byte>();
            byte next;
            do
            {
                next = await ReadByte(cancellationToken);
                if (next != '\n')
                {
                    line.Add(next);
                }
            } while (next != '\n');

            if (line.Count > 0 && line[^1] == '\r')
            {
                line.RemoveAt(line.Count - 1);
            }

            return Encoding.Latin1.GetString([.. line]);
        }

        public async Task<byte[]> ReadExactly(int count, CancellationToken cancellationToken)
        {
            var result = new byte[count];
            var written = 0;
            while (written < count)
            {
                if (offset == length)
                {
                    length = await stream.ReadAsync(buffer, cancellationToken);
                    offset = 0;
                    if (length == 0)
                    {
                        throw new InvalidOperationException("The IMAP connection closed unexpectedly.");
                    }
                }

                var copied = Math.Min(length - offset, count - written);
                buffer.AsSpan(offset, copied).CopyTo(result.AsSpan(written));
                offset += copied;
                written += copied;
            }

            return result;
        }

        private async ValueTask<byte> ReadByte(CancellationToken cancellationToken)
        {
            if (offset == length)
            {
                length = await stream.ReadAsync(buffer, cancellationToken);
                offset = 0;
                if (length == 0)
                {
                    throw new InvalidOperationException("The IMAP connection closed unexpectedly.");
                }
            }

            return buffer[offset++];
        }
    }
}
