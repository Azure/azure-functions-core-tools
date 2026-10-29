// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Immutable;
using System.CommandLine;
using System.CommandLine.Parsing;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Integration;
using Azure.Functions.Cli.Templates.PackageIntake;

namespace Azure.Functions.Cli.Commands.Templates;

/// <summary>
/// Binds the isolated package candidate grammar without registering a public command or hydrating template options.
/// </summary>
internal abstract class PackageTemplateCommand : FuncCliCommand
{
    private readonly PackageTemplateApplication _application;
    private readonly PackageTemplateCommandExecutor _executor;
    private readonly TemplateType _type;

    protected PackageTemplateCommand(
        string name, string description, TemplateType type, PackageTemplateApplication application, PackageTemplateCommandExecutor executor)
        : base(name, description)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _type = type;
        AddPathArgument();
        Options.Add(PackageOption);
        Options.Add(PackageIdOption);
        Options.Add(PackageVersionOption);
        Options.Add(PackageSha256Option);
        Options.Add(TemplateOption);
        Options.Add(NameOption);
        Options.Add(LanguageOption);
        Options.Add(FormatOption);
        Options.Add(DryRunOption);
        TreatUnmatchedTokensAsErrors = false;
    }

    public Option<string> PackageOption { get; } = new("--package") { Description = "Exact absolute local template archive path.", Required = true };
    public Option<string> PackageIdOption { get; } = new("--package-id") { Description = "Expected package ID.", Required = true };
    public Option<string> PackageVersionOption { get; } = new("--package-version") { Description = "Expected exact package version.", Required = true };
    public Option<string> PackageSha256Option { get; } = new("--package-sha256") { Description = "Expected archive SHA-256.", Required = true };
    public Option<string> TemplateOption { get; } = new("--template") { Description = "Exact template identity or short name.", Required = true };
    public Option<string?> NameOption { get; } = new("--name") { Description = "Native template name." };
    public Option<string?> LanguageOption { get; } = new("--language") { Description = "Template language variant filter." };
    public Option<string?> FormatOption { get; } = new("--format") { Description = "Result format: plain or json." };
    public Option<bool> DryRunOption { get; } = new("--dry-run") { Description = "Capture and preview without publishing target files." };

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        string format = parseResult.GetValue(FormatOption) ?? "plain";
        if (format is not ("plain" or "json"))
        {
            throw new GracefulException("Use '--format plain' or '--format json'.", isUserError: true);
        }

        ImmutableArray<string> tokens = TemplateTokens(parseResult);
        PackageTemplateRequest request;
        try
        {
            request = new(
                new(parseResult.GetValue(PackageOption)!, parseResult.GetValue(PackageIdOption)!,
                    parseResult.GetValue(PackageVersionOption)!, "FuncTemplate", parseResult.GetValue(PackageSha256Option)),
                new(parseResult.GetValue(TemplateOption)!, _type, tokens,
                    GetReservedAliases(parseResult),
                    parseResult.GetValue(LanguageOption)),
                parseResult.GetValue(PathArgument!)!.Info.FullName, parseResult.GetValue(NameOption));
        }
        catch (ArgumentException exception)
        {
            throw new GracefulException(exception.Message, exception, isUserError: true);
        }

        return await _executor.ExecuteAsync(Name, request, format == "json", parseResult.GetValue(DryRunOption),
            token => _application.PrepareAsync(request, token), cancellationToken);
    }

    private static ImmutableArray<string> TemplateTokens(ParseResult result)
    {
        Token[] tokens = [.. result.Tokens];
        int boundary = Array.FindIndex(tokens, token => token.Type == TokenType.DoubleDash);
        ImmutableArray<string> values = boundary < 0 ? [] : [.. tokens.Skip(boundary + 1).Select(token => token.Value)];
        if (!result.UnmatchedTokens.SequenceEqual(values, StringComparer.Ordinal))
        {
            throw new GracefulException(
                "Unrecognized candidate input. Put template-only options after '--'; use '--format', not '--output', for result formatting.",
                isUserError: true);
        }

        string[] repeated = [.. tokens.Take(boundary < 0 ? tokens.Length : boundary)
            .Where(token => token.Type == TokenType.Option)
            .GroupBy(token => token.Value, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key)];
        if (repeated.Length != 0)
        {
            throw new GracefulException($"Candidate options cannot be repeated: {string.Join(", ", repeated)}.", isUserError: true);
        }

        return values;
    }

    private static ImmutableArray<string> GetReservedAliases(ParseResult result)
    {
        HashSet<string> aliases = new(StringComparer.Ordinal) { "--output" };
        for (CommandResult? command = result.CommandResult; command is not null; command = command.Parent as CommandResult)
        {
            foreach (Option option in command.Command.Options)
            {
                if (command == result.CommandResult || option.Recursive)
                {
                    aliases.Add(option.Name);
                    aliases.UnionWith(option.Aliases);
                }
            }
        }

        return [.. aliases];
    }
}
