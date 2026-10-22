// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Frozen;
using System.CommandLine;
using System.CommandLine.Parsing;
using Azure.Functions.Cli.Commands;

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Retains original argv and maps only workload-owned implicit Boolean values to the native scalar grammar.
/// </summary>
internal sealed class NativeTemplateCommandInput
{
    private readonly IReadOnlyList<int> _sourceIndices;

    private NativeTemplateCommandInput(string[] originalTokens, string[] templateTokens, string[] nativeTokens,
        int[] sourceIndices, IReadOnlySet<string> reservedAliases)
    {
        OriginalTokens = Array.AsReadOnly(originalTokens);
        TemplateTokens = Array.AsReadOnly(templateTokens);
        NativeTokens = Array.AsReadOnly(nativeTokens);
        _sourceIndices = Array.AsReadOnly(sourceIndices);
        ReservedAliases = reservedAliases;
    }

    public IReadOnlyList<string> OriginalTokens { get; }

    public IReadOnlyList<string> TemplateTokens { get; }

    public IReadOnlyList<string> NativeTokens { get; }

    public IReadOnlySet<string> ReservedAliases { get; }

    public static NativeTemplateCommandInput Capture(
        FuncCliCommand command, ParseResult parsed, IReadOnlyList<string> originalTokens, IReadOnlySet<Option> hostOptions)
        => Capture(command, parsed, originalTokens, hostOptions, null, usePathIndex: false);

    internal static NativeTemplateCommandInput CaptureEntry(FuncCliCommand command, ParseResult parsed,
        IReadOnlyList<string> originalTokens, IReadOnlySet<Option> hostOptions, int? pathIndex)
        => Capture(command, parsed, originalTokens, hostOptions, pathIndex, usePathIndex: true);

    private static NativeTemplateCommandInput Capture(FuncCliCommand command, ParseResult parsed,
        IReadOnlyList<string> originalTokens, IReadOnlySet<Option> hostOptions, int? pathIndex, bool usePathIndex)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(parsed);
        ArgumentNullException.ThrowIfNull(originalTokens);
        ArgumentNullException.ThrowIfNull(hostOptions);
        if (!ReferenceEquals(parsed.CommandResult.Command, command) || parsed.Errors.Count > 0)
        {
            throw new ArgumentException("Capture requires a successful parse of the supplied command.", nameof(parsed));
        }

        if (originalTokens.Any(token => token is null))
        {
            throw new ArgumentException("Original tokens must not contain null entries.", nameof(originalTokens));
        }

        List<Option> options = [.. command.Options];
        HashSet<Option> consumedOptions = new(hostOptions, ReferenceEqualityComparer.Instance);
        for (Command? parent = command.Parents.OfType<Command>().FirstOrDefault(); parent is not null;
            parent = parent.Parents.OfType<Command>().FirstOrDefault())
        {
            Option[] recursive = [.. parent.Options.Where(option => option.Recursive)];
            options.AddRange(recursive);
            consumedOptions.UnionWith(recursive);
        }

        Dictionary<string, Option> aliases = new(StringComparer.Ordinal);
        foreach (Option option in options)
        {
            aliases.Add(option.Name, option);
            foreach (string alias in option.Aliases)
            {
                aliases.TryAdd(alias, option);
            }
        }

        string? path = usePathIndex ? pathIndex is { } position ? originalTokens[position] : null : command.PathArgument is { } pathArgument
            ? parsed.GetResult(pathArgument)?.Tokens.SingleOrDefault()?.Value : null;
        bool pathConsumed = path is null;
        bool afterDelimiter = false;
        List<string> template = [];
        List<string> native = [];
        List<int> sources = [];
        for (int index = 0; index < originalTokens.Count; index++)
        {
            string token = originalTokens[index];
            if (token == "--" && !afterDelimiter)
            {
                afterDelimiter = true;
                // A delimiter used only to introduce the public [path] is host syntax, not a template argument.
                if (!pathConsumed && index + 2 == originalTokens.Count && originalTokens[index + 1] == path)
                {
                    continue;
                }

                Add(token);
                continue;
            }

            Option? option = null;
            bool inline = false;
            if (!afterDelimiter && !aliases.TryGetValue(token, out option))
            {
                int separator = token.IndexOfAny(['=', ':']);
                if (separator > 0 && aliases.TryGetValue(token[..separator], out option))
                {
                    inline = true;
                }
            }

            if (option is not null)
            {
                bool host = consumedOptions.Contains(option);
                if (!host)
                {
                    Add(token);
                }

                if (!inline && option.Arity.MaximumNumberOfValues > 0)
                {
                    bool hasValue = index + 1 < originalTokens.Count
                        && (option.Arity.MinimumNumberOfValues > 0 || (option is Option<bool> && bool.TryParse(originalTokens[index + 1], out _)));
                    if (hasValue)
                    {
                        if (!host)
                        {
                            Add(originalTokens[index + 1]);
                        }

                        index++;
                    }
                    else if (!host && option is Option<bool>)
                    {
                        native.Add("true");
                        sources.Add(template.Count - 1);
                    }
                }

                continue;
            }

            if (!pathConsumed && (usePathIndex ? index == pathIndex : token == path))
            {
                pathConsumed = true;
                continue;
            }

            Add(token);
        }

        if (!pathConsumed)
        {
            throw new InvalidOperationException("The parsed path could not be accounted for in the original command tokens.");
        }

        return new([.. originalTokens], [.. template], [.. native], [.. sources],
            consumedOptions.SelectMany(option => option.Aliases.Prepend(option.Name)).ToFrozenSet(StringComparer.Ordinal));

        void Add(string token)
        {
            sources.Add(template.Count);
            template.Add(token);
            native.Add(token);
        }
    }

    public TemplateCandidateParseResult RestoreProvenance(TemplateCandidateParseResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result with
        {
            InvalidInputDiagnostics = Array.AsReadOnly<TemplateArgumentDiagnostic>([.. result.InvalidInputDiagnostics.Select(diagnostic =>
                diagnostic with
                {
                    TokenIndices = Array.AsReadOnly<int>([.. diagnostic.TokenIndices.Select(index => _sourceIndices[index]).Distinct().Order()]),
                })]),
        };
    }
}
