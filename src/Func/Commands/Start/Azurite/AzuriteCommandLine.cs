// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;

namespace Azure.Functions.Cli.Commands.Start.Azurite;

/// <summary>
/// Pure helpers for recognizing an Azurite process from its command line and
/// extracting the data directory it was launched against.
/// </summary>
internal static class AzuriteCommandLine
{
    /// <summary>
    /// Returns <c>true</c> when the command line looks like an Azurite process.
    /// </summary>
    public static bool LooksLikeAzurite(string? commandLine)
        => !string.IsNullOrWhiteSpace(commandLine)
            && commandLine.Contains("azurite", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Extracts the data directory from an Azurite command line, i.e. the value
    /// following the <c>-l</c> / <c>--location</c> switch.
    /// </summary>
    public static bool TryGetDataDirectory(string? commandLine, out string? dataDirectory)
    {
        dataDirectory = null;
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return false;
        }

        IReadOnlyList<string> tokens = Tokenize(commandLine);
        for (int i = 0; i < tokens.Count; i++)
        {
            string token = tokens[i];

            // Equals form: -l=<dir> / --location=<dir>.
            if (TryGetInlineValue(token, "-l", out string? inline)
                || TryGetInlineValue(token, "--location", out inline))
            {
                if (!string.IsNullOrWhiteSpace(inline))
                {
                    dataDirectory = inline;
                    return true;
                }

                continue;
            }

            // Space form: -l <dir> / --location <dir>.
            if (token is "-l" or "--location" && i + 1 < tokens.Count)
            {
                string candidate = tokens[i + 1];
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    dataDirectory = candidate;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryGetInlineValue(string token, string flag, out string? value)
    {
        value = null;
        string prefix = flag + "=";
        if (token.StartsWith(prefix, StringComparison.Ordinal))
        {
            value = token[prefix.Length..];
            return true;
        }

        return false;
    }

    /// <summary>
    /// Splits a command line into tokens, honoring single and double quotes so
    /// paths containing spaces survive as one token. Quotes are stripped.
    /// </summary>
    private static List<string> Tokenize(string commandLine)
    {
        List<string> tokens = [];
        StringBuilder current = new();
        bool inToken = false;
        char quote = '\0';

        foreach (char c in commandLine)
        {
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }
                else
                {
                    current.Append(c);
                }

                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
                inToken = true;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (inToken)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    inToken = false;
                }

                continue;
            }

            current.Append(c);
            inToken = true;
        }

        if (inToken)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }
}
