namespace Cabinet.Cli;

internal sealed class UsageException(string message) : Exception(message);

internal sealed class CommandLine
{
    private const string Delimiter = "--";
    private const string JsonOption = "--json";

    private static readonly HashSet<string> Flags =
        new([JsonOption, "--installed", "--not-installed"], StringComparer.Ordinal);

    private static readonly HashSet<string> Valued =
        new(["--prefix", "--search", "--category", "--developer", "--kind"], StringComparer.Ordinal);

    private static readonly HashSet<string> PassingThrough = new(["run", "winetricks"], StringComparer.Ordinal);

    private readonly List<string> words = [];
    private readonly Dictionary<string, string?> options = new(StringComparer.Ordinal);
    private readonly HashSet<string> accepted = new(StringComparer.Ordinal);
    private readonly List<string> name = [];
    private int taken;

    private CommandLine()
    {
    }

    public bool Help { get; private set; }

    public IReadOnlyList<string> PassedThrough { get; private set; } = [];

    public static CommandLine Parse(IReadOnlyList<string> args)
    {
        var line = new CommandLine();
        var literal = false;

        for (var index = 0; index < args.Count; index++)
        {
            var arg = args[index];

            if (line.words.Count == 2 && PassingThrough.Contains(line.words[0]))
            {
                line.PassedThrough = [.. args.Skip(!literal && arg == Delimiter ? index + 1 : index)];
                break;
            }

            if (literal || arg == "-" || !arg.StartsWith('-'))
            {
                line.words.Add(arg);
            }
            else if (arg == Delimiter)
            {
                literal = true;
            }
            else if (arg is "-h" or "--help")
            {
                line.Help = true;
            }
            else if (Flags.Contains(arg))
            {
                line.Add(arg, null);
            }
            else if (Valued.Contains(arg))
            {
                line.Add(arg, ++index < args.Count
                    ? args[index]
                    : throw new UsageException($"{arg} needs something after it"));
            }
            else
            {
                throw new UsageException($"unknown option '{arg}'");
            }
        }

        line.Help |= line.words.FirstOrDefault() == "help";
        return line;
    }

    public string Verb() =>
        Subcommand() ?? throw new UsageException("expected a command — `cabinet --help` lists them");

    public string? Subcommand()
    {
        var word = OptionalWord();

        if (word is not null)
        {
            name.Add(word);
        }

        return word;
    }

    public string Word(string what) =>
        OptionalWord() ?? throw new UsageException($"expected {what}");

    public string? OptionalWord() =>
        taken < words.Count ? words[taken++] : null;

    public string? Option(string option)
    {
        accepted.Add(option);
        return options.GetValueOrDefault(option);
    }

    public bool Flag(string option)
    {
        accepted.Add(option);
        return options.ContainsKey(option);
    }

    public UsageException Unknown(string command) =>
        new($"unknown command '{command}' — `cabinet --help` lists them");

    public Func<int> Then(Func<int> act)
    {
        if (taken < words.Count)
        {
            throw new UsageException($"unexpected '{words[taken]}' after `cabinet {Named}`");
        }

        if (options.Keys.FirstOrDefault(option => !accepted.Contains(option)) is { } stray)
        {
            throw new UsageException($"{stray} does not apply to `cabinet {Named}`");
        }

        return act;
    }

    public Func<int> Then(Func<bool, int> act)
    {
        var json = Flag(JsonOption);
        return Then(() => act(json));
    }

    private string Named => string.Join(' ', name);

    private void Add(string option, string? value)
    {
        if (!options.TryAdd(option, value))
        {
            throw new UsageException($"{option} is given twice");
        }
    }
}
