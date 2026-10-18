// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Text;

#if FUNC_PYTHON_TEMPLATE_HELPERS
namespace Azure.Functions.Cli.Templates.Python.Shared;
#else
namespace Azure.Functions.Cli.Workloads.Python;
#endif

/// <summary>
/// Checks statically declared Python symbols and function names without executing user code.
/// </summary>
internal sealed class PythonFunctionNameGuard
{
    internal const string ValidationLimitNotice = "Python name validation is partial: Blueprint or dynamic registration, "
        + "computed decorators, or unsupported syntax may hide additional function names. "
        + "Existing Python was not executed. Verify application indexing before deployment.";

    public string? GetDiagnostic(string existingSource, string generatedSnippet, CancellationToken cancellationToken) =>
        Analyze(existingSource, generatedSnippet, cancellationToken).Diagnostic;

    public PythonFunctionNameValidation Analyze(string existingSource, string generatedSnippet, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(existingSource);
        ArgumentNullException.ThrowIfNull(generatedSnippet);
        cancellationToken.ThrowIfCancellationRequested();
        bool existingComplete = TryReadNames(existingSource, cancellationToken, out Names existing);
        bool generatedComplete = TryReadNames(generatedSnippet, cancellationToken, out Names generated);

        HashSet<string> symbols = new(existing.Symbols, StringComparer.Ordinal);
        HashSet<string> functions = new(existing.Functions, StringComparer.Ordinal);
        foreach (string symbol in generated.Symbols)
        {
            if (!symbols.Add(symbol))
            {
                return new(Collision(symbol), existingComplete && generatedComplete);
            }
        }

        foreach (string function in generated.Functions)
        {
            if (!functions.Add(function))
            {
                return new(Collision(function), existingComplete && generatedComplete);
            }
        }

        return new(null, existingComplete && generatedComplete);
    }

    private static string Collision(string name) => $"Python function or symbol '{name}' already exists. "
        + "Choose a different function name. --force cannot replace user-owned Python code; nothing was appended.";

    private static bool TryReadNames(string source, CancellationToken cancellationToken, out Names names)
    {
        names = new([], []);
        bool complete = TryTokenize(source, cancellationToken, out List<Statement> statements);

        List<Statement> decorators = [];
        HashSet<string> receivers = new(StringComparer.Ordinal) { "app" };
        int? localScopeIndent = null;
        string? freshFunction = null;
        foreach (Statement statement in statements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<Token> tokens = statement.Tokens;
            bool decorator = tokens[0].Text == "@";
            if (HasRegistrationCall(tokens, decorator, receivers))
            {
                complete = false;
            }

            if (localScopeIndent is { } scope)
            {
                if (statement.Indent > scope)
                {
                    if (decorator && IsApplicationDecorator(tokens, receivers))
                    {
                        complete = false;
                    }

                    continue;
                }

                localScopeIndent = null;
            }

            complete &= UpdateApplicationBinding(statement, receivers, names);
            complete &= receivers.Contains("app");
            string? precedingFunction = freshFunction;
            freshFunction = null;
            if (!decorator && statement.Indent == 0)
            {
                ReadProgrammaticNames(tokens, receivers, names, precedingFunction);
            }

            if (decorator)
            {
                complete &= IsApplicationDecorator(tokens, receivers);
                decorators.Add(statement);
                continue;
            }

            int offset = tokens[0].Text == "async" ? 1 : 0;
            if (tokens.Count > offset + 1 && tokens[offset].Text is "def" or "class")
            {
                if (statement.Indent != 0)
                {
                    complete = false;
                    localScopeIndent = statement.Indent;
                    decorators.Clear();
                    continue;
                }

                string symbol = tokens[offset + 1].Text;
                names.Symbols.Add(symbol);
                if (tokens[offset].Text == "def" && decorators.Count == 0)
                {
                    freshFunction = symbol;
                }

                if (tokens[offset].Text == "def" && decorators.Any(item => IsApplicationDecorator(item.Tokens, receivers)))
                {
                    string function = symbol;
                    bool named = false;
                    bool known = true;
                    foreach (Statement item in decorators)
                    {
                        if (item.Indent != statement.Indent || !IsApplicationDecorator(item.Tokens, receivers))
                        {
                            complete = false;
                            known = false;
                            continue;
                        }

                        int nameIndex = item.Tokens.FindIndex(token => token.Text == "function_name");
                        if (nameIndex < 0)
                        {
                            continue;
                        }

                        if (named || !TryReadFunctionName(item.Tokens, nameIndex, out function))
                        {
                            complete = false;
                            known = false;
                        }

                        named = true;
                    }

                    if (known)
                    {
                        names.Functions.Add(function);
                    }
                }

                localScopeIndent = statement.Indent;
            }
            else if (decorators.Count > 0)
            {
                complete = false;
            }

            decorators.Clear();
        }

        return complete && decorators.Count == 0;
    }

    private static bool UpdateApplicationBinding(Statement statement, HashSet<string> receivers, Names names)
    {
        string[] previousReceivers = [.. receivers];
        List<Token> tokens = statement.Tokens;
        int offset = tokens[0].Text == "async" ? 1 : 0;
        if (tokens.Count > offset + 1 && tokens[offset].Text is "def" or "class")
        {
            InvalidateReceiver(tokens[offset + 1].Text, receivers, names);
        }

        int depth = 0;
        for (int index = 0; index < tokens.Count; index++)
        {
            string text = tokens[index].Text;
            if (depth == 0 && text == "=")
            {
                foreach (string receiver in receivers.Where(receiver => tokens.Take(index).Any(token => token.Text == receiver)).ToArray())
                {
                    InvalidateReceiver(receiver, receivers, names);
                }

                break;
            }

            depth += text is "(" or "[" or "{" ? 1 : text is ")" or "]" or "}" ? -1 : 0;
        }

        if (statement.Indent != 0 || tokens.Count < 3 || tokens[1].Text != "=")
        {
            return previousReceivers.All(receivers.Contains);
        }

        if (tokens[0].Text == "app")
        {
            if (tokens.Count > 6 && tokens[3].Text == "." && tokens[4].Text is "FunctionApp" or "DFApp"
                && FindCallEnd(tokens, 5) == tokens.Count - 1)
            {
                receivers.Add("app");
            }
        }
        else if (tokens.Count == 3 && receivers.Contains(tokens[2].Text))
        {
            receivers.Add(tokens[0].Text);
        }

        return previousReceivers.All(receivers.Contains);
    }

    private static void InvalidateReceiver(string name, HashSet<string> receivers, Names names)
    {
        if (name == "app" && receivers.Contains(name))
        {
            receivers.Clear();
            names.Functions.Clear();
        }
        else
        {
            receivers.Remove(name);
        }
    }

    private static bool IsApplicationDecorator(List<Token> tokens, HashSet<string> receivers) =>
        tokens.Count > 3 && tokens[0].Text == "@" && receivers.Contains(tokens[1].Text) && tokens[2].Text == ".";

    private static bool HasRegistrationCall(List<Token> tokens, bool decorator, HashSet<string> receivers)
    {
        int offset = tokens[0].Text == "async" ? 1 : 0;
        bool definition = tokens.ElementAtOrDefault(offset)?.Text is "def" or "class";
        int depth = 0;
        for (int index = 1; index + 1 < tokens.Count; index++)
        {
            depth += tokens[index - 1].Text is "(" or "[" or "{" ? 1 : tokens[index - 1].Text is ")" or "]" or "}" ? -1 : 0;
            if (!decorator && !definition && depth > 0 && receivers.Contains(tokens[index].Text)
                && tokens[index - 1].Text is "(" or "," or "=" && tokens[index + 1].Text is ")" or ",")
            {
                return true;
            }

            if (index >= 2 && tokens[index - 1].Text == "." && tokens[index + 1].Text == "("
                && (tokens[index].Text is "register_functions" or "register_blueprint"
                    || (!decorator && (tokens[index].Text == "function_name"
                        || receivers.Contains(tokens[index - 2].Text)))))
            {
                return true;
            }
        }

        return false;
    }

    private static void ReadProgrammaticNames(List<Token> tokens, HashSet<string> receivers, Names names, string? freshFunction)
    {
        if (freshFunction is null || tokens.Count(token => token.Text == "function_name") != 1)
        {
            return;
        }

        int index = tokens.Count > 1 && tokens[1].Text == "=" ? 4 : 2;
        if (tokens.Count <= index + 1 || tokens[index].Text != "function_name" || tokens[index - 1].Text != "."
            || !receivers.Contains(tokens[index - 2].Text) || tokens[index + 1].Text != "(")
        {
            return;
        }

        int end = FindCallEnd(tokens, index + 1);
        int appliedEnd = FindCallEnd(tokens, end + 1);
        if (end > index + 1 && appliedEnd == tokens.Count - 1
            && IsFreshFunctionTarget(tokens.GetRange(end + 2, appliedEnd - end - 2), receivers, freshFunction)
            && TryReadFunctionName(tokens.GetRange(0, end + 1), index, out string name))
        {
            names.Functions.Add(name);
        }
    }

    private static bool IsFreshFunctionTarget(List<Token> tokens, HashSet<string> receivers, string freshFunction)
    {
        if (tokens.Count == 1)
        {
            return tokens[0].Text == freshFunction;
        }

        if (tokens.Count < 7 || !receivers.Contains(tokens[0].Text) || tokens[1].Text != "."
            || (tokens[2].Text != "route" && !tokens[2].Text.EndsWith("_trigger", StringComparison.Ordinal)))
        {
            return false;
        }

        int bindingEnd = FindCallEnd(tokens, 3);
        return bindingEnd >= 0 && tokens.Count == bindingEnd + 4 && tokens[bindingEnd + 1].Text == "("
            && tokens[^1].Text == ")" && tokens[bindingEnd + 2].Text == freshFunction;
    }

    private static int FindCallEnd(List<Token> tokens, int start)
    {
        if (tokens.ElementAtOrDefault(start)?.Text != "(")
        {
            return -1;
        }

        int depth = 0;
        for (int index = start; index < tokens.Count; index++)
        {
            depth += tokens[index].Text == "(" ? 1 : tokens[index].Text == ")" ? -1 : 0;
            if (depth == 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static bool TryReadFunctionName(List<Token> tokens, int nameIndex, out string name)
    {
        name = string.Empty;
        if (nameIndex < 2 || tokens[nameIndex - 1].Text != "." || tokens.ElementAtOrDefault(nameIndex + 1)?.Text != "(")
        {
            return false;
        }

        int start = nameIndex + 2;
        int depth = 0;
        string? value = null;
        for (int index = start; index < tokens.Count; index++)
        {
            string text = tokens[index].Text;
            if (depth == 0 && text is "," or ")")
            {
                int argument = start;
                bool keyword = tokens.ElementAtOrDefault(start + 1)?.Text == "=";
                if (keyword && tokens[start].Text == "name")
                {
                    argument += 2;
                }
                else if (keyword || start != nameIndex + 2)
                {
                    start = index + 1;
                    continue;
                }

                if (value is not null || !TryReadLiteral(tokens.GetRange(argument, index - argument), out value))
                {
                    return false;
                }

                start = index + 1;
            }

            depth += text is "(" or "[" or "{" ? 1 : text is ")" or "]" or "}" ? -1 : 0;
        }

        name = value ?? string.Empty;
        return value is not null;
    }

    private static bool TryReadLiteral(List<Token> tokens, out string value)
    {
        while (tokens.Count >= 2 && tokens[0].Text == "(" && tokens[^1].Text == ")")
        {
            tokens = tokens.GetRange(1, tokens.Count - 2);
        }

        value = string.Empty;
        if (tokens.Count == 0 || tokens.Any(token => !token.IsString || token.Literal is null))
        {
            return false;
        }

        value = string.Concat(tokens.Select(token => token.Literal));
        return true;
    }

    private static bool TryTokenize(string source, CancellationToken cancellationToken, out List<Statement> statements)
    {
        statements = [];
        List<Token> tokens = [];
        Stack<char> brackets = [];
        int indent = 0;
        bool lineStart = true;
        bool complete = true;
        for (int index = 0; index < source.Length;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            char value = source[index];
            if (lineStart && brackets.Count == 0 && tokens.Count == 0)
            {
                indent = 0;
                while (index < source.Length && source[index] is ' ' or '\t' or '\f' or '\ufeff')
                {
                    indent = source[index] == '\t' ? ((indent / 8) + 1) * 8 : indent + 1;
                    index++;
                }

                if (index == source.Length)
                {
                    break;
                }

                value = source[index];
            }

            lineStart = false;
            if (value == '#')
            {
                while (index < source.Length && source[index] is not '\n' and not '\r')
                {
                    index++;
                }

                continue;
            }

            if (value == '\\' && index + 1 < source.Length && source[index + 1] is '\r' or '\n')
            {
                index += source[index + 1] == '\r' && index + 2 < source.Length && source[index + 2] == '\n' ? 3 : 2;
                continue;
            }

            if (value is '\r' or '\n' || (value == ';' && brackets.Count == 0))
            {
                if (brackets.Count == 0 && tokens.Count > 0)
                {
                    statements.Add(new(indent, tokens));
                    tokens = [];
                }

                lineStart = value != ';';
                index++;
                continue;
            }

            if (char.IsWhiteSpace(value))
            {
                index++;
                continue;
            }

            int start = index;
            // Keep unfamiliar scalars in one token instead of reserving a truncated identifier.
            while (index < source.Length && Rune.TryGetRuneAt(source, index, out Rune rune)
                && (IsIdentifier(rune) || (rune.Value > 127 && !Rune.IsWhiteSpace(rune))))
            {
                complete &= IsIdentifier(rune);
                index += rune.Utf16SequenceLength;
            }

            string prefix = source[start..index];
            if (index < source.Length && source[index] is '\'' or '"' && (prefix.Length == 0 || IsStringPrefix(prefix)))
            {
                if (!TryReadString(source, ref index, prefix, cancellationToken, out string? literal))
                {
                    return false;
                }

                tokens.Add(new(string.Empty, true, literal));
                continue;
            }

            if (index > start)
            {
                string normalized = prefix.Normalize(NormalizationForm.FormKC);
                complete &= normalized.EnumerateRunes().All(IsIdentifier);
                tokens.Add(new(normalized));
                continue;
            }

            if (!Rune.TryGetRuneAt(source, index, out _))
            {
                return false;
            }

            if (value is '(' or '[' or '{')
            {
                brackets.Push(value);
            }
            else if (value is ')' or ']' or '}')
            {
                if (!brackets.TryPop(out char opening) || (opening, value) is not (('(', ')') or ('[', ']') or ('{', '}')))
                {
                    return false;
                }
            }

            tokens.Add(new(value.ToString()));
            index++;
        }

        if (tokens.Count > 0)
        {
            statements.Add(new(indent, tokens));
        }

        return complete && brackets.Count == 0;
    }

    private static bool IsIdentifier(Rune value) => Rune.IsLetterOrDigit(value) || value.Value == '_'
        || value.Value is 0x2118 or 0x212e or 0x00b7 or 0x0387 or 0x19da or (>= 0x1369 and <= 0x1371)
        || Rune.GetUnicodeCategory(value) is UnicodeCategory.LetterNumber or UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.ConnectorPunctuation;

    private static bool IsStringPrefix(string value) => value.ToLowerInvariant() is "r" or "u" or "b" or "f" or "br" or "rb" or "fr" or "rf";

    private static bool TryReadString(string source, ref int index, string prefix, CancellationToken cancellationToken, out string? literal)
    {
        char quote = source[index++];
        bool triple = index + 1 < source.Length && source[index] == quote && source[index + 1] == quote;
        if (triple)
        {
            index += 2;
        }

        int start = index;
        while (index < source.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (source[index] == '\\')
            {
                index += 2;
                continue;
            }

            if (source[index] == quote && (!triple || (index + 2 < source.Length && source[index + 1] == quote && source[index + 2] == quote)))
            {
                string content = source[start..index];
                index += triple ? 3 : 1;
                if (prefix.Contains('f', StringComparison.OrdinalIgnoreCase) && !HasBalancedInterpolation(content))
                {
                    literal = null;
                    return false;
                }

                // Interpolated and byte strings are not statically known SDK names.
                literal = prefix.Contains('f', StringComparison.OrdinalIgnoreCase) || prefix.Contains('b', StringComparison.OrdinalIgnoreCase)
                    ? null : prefix.Contains('r', StringComparison.OrdinalIgnoreCase) ? content : DecodeLiteral(content);
                return true;
            }

            if (!triple && source[index] is '\r' or '\n')
            {
                break;
            }

            index++;
        }

        literal = null;
        return false;
    }

    private static bool HasBalancedInterpolation(string content)
    {
        int depth = 0;
        for (int index = 0; index < content.Length; index++)
        {
            char value = content[index];
            if (depth == 0 && value is '{' or '}' && index + 1 < content.Length && content[index + 1] == value)
            {
                index++;
            }
            else if (value == '{')
            {
                depth++;
            }
            else if (value == '}' && --depth < 0)
            {
                return false;
            }
        }

        return depth == 0;
    }

    private static string? DecodeLiteral(string content)
    {
        StringBuilder value = new();
        for (int index = 0; index < content.Length; index++)
        {
            char current = content[index];
            if (current != '\\')
            {
                value.Append(current);
                continue;
            }

            if (++index == content.Length)
            {
                return null;
            }

            current = content[index];
            if (current is 'x' or 'u' or 'U')
            {
                int count = current == 'x' ? 2 : current == 'u' ? 4 : 8;
                if (index + count >= content.Length
                    || !int.TryParse(content.AsSpan(index + 1, count), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int code)
                    || !Rune.IsValid(code))
                {
                    return null;
                }

                value.Append(char.ConvertFromUtf32(code));
                index += count;
            }
            else if (current is >= '0' and <= '7')
            {
                int code = current - '0';
                for (int count = 1; count < 3 && index + 1 < content.Length && content[index + 1] is >= '0' and <= '7'; count++)
                {
                    code = (code * 8) + content[++index] - '0';
                }

                value.Append((char)code);
            }
            else if (current == 'N')
            {
                return null;
            }
            else if (current == '\r' && index + 1 < content.Length && content[index + 1] == '\n')
            {
                index++;
            }
            else if (current is not '\r' and not '\n')
            {
                value.Append(current switch
                {
                    'a' => '\a',
                    'b' => '\b',
                    'f' => '\f',
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    'v' => '\v',
                    '\\' or '\'' or '"' => current,
                    _ => '\\',
                });
                if (current is not ('a' or 'b' or 'f' or 'n' or 'r' or 't' or 'v' or '\\' or '\'' or '"'))
                {
                    value.Append(current);
                }
            }
        }

        return value.ToString();
    }

    private sealed record Token(string Text, bool IsString = false, string? Literal = null);

    private sealed record Statement(int Indent, List<Token> Tokens);

    private sealed record Names(List<string> Symbols, List<string> Functions);
}

internal sealed record PythonFunctionNameValidation(string? Diagnostic, bool Complete);
