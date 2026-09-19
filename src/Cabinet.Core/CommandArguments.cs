using System.Text;

namespace Cabinet.Core;

public static class CommandArguments
{
    public static IReadOnlyList<string> Parse(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var arguments = new List<string>();
        var current = new StringBuilder();
        var singleQuoted = false;
        var doubleQuoted = false;
        var argumentStarted = false;
        var escaped = false;
        var escapedInDoubleQuotes = false;

        foreach (var character in input)
        {
            if (escaped)
            {
                if (!escapedInDoubleQuotes || character is '$' or '`' or '"' or '\\' or '\n')
                {
                    current.Append(character);
                }
                else
                {
                    current.Append('\\');
                    current.Append(character);
                }

                escaped = false;
                escapedInDoubleQuotes = false;
                continue;
            }

            if (singleQuoted)
            {
                if (character == '\'')
                {
                    singleQuoted = false;
                }
                else
                {
                    current.Append(character);
                }

                continue;
            }

            if (character == '\\')
            {
                escaped = true;
                escapedInDoubleQuotes = doubleQuoted;
                argumentStarted = true;
                continue;
            }

            if (doubleQuoted)
            {
                if (character == '"')
                {
                    doubleQuoted = false;
                }
                else
                {
                    current.Append(character);
                }

                continue;
            }

            if (character == '\'')
            {
                singleQuoted = true;
                argumentStarted = true;
                continue;
            }

            if (character == '"')
            {
                doubleQuoted = true;
                argumentStarted = true;
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                if (argumentStarted)
                {
                    arguments.Add(current.ToString());
                    current.Clear();
                    argumentStarted = false;
                }

                continue;
            }

            current.Append(character);
            argumentStarted = true;
        }

        if (escaped)
        {
            throw new ArgumentException("command ends with an unfinished escape", nameof(input));
        }

        if (singleQuoted)
        {
            throw new ArgumentException("command has an unterminated single quote", nameof(input));
        }

        if (doubleQuoted)
        {
            throw new ArgumentException("command has an unterminated double quote", nameof(input));
        }

        if (argumentStarted)
        {
            arguments.Add(current.ToString());
        }

        if (arguments.Count == 0)
        {
            throw new ArgumentException("command is empty", nameof(input));
        }

        return arguments;
    }
}
