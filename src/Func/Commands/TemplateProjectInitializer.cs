// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Parsing;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Creates a project from the unique <c>func-{stack}</c> project template through the Template Engine.
/// </summary>
/// <remarks>
/// <see cref="InitCommand"/> owns option registration, adoption, healing, and project configuration; this type owns
/// template selection, binding of <c>func.host.json</c> aliases to native parameters, preflight, and creation.
/// </remarks>
internal sealed class TemplateProjectInitializer(IInteractionService interaction)
{
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));

    /// <summary>
    /// Selects the default template from <paramref name="candidates"/>, validates its creation with a dry run, and
    /// creates the project after <paramref name="prepareDestinationAsync"/> approves the destination, then invokes
    /// <paramref name="writeProjectConfiguration"/>.
    /// </summary>
    /// <returns><c>0</c> when the project was created; otherwise <c>1</c> after reporting the failure.</returns>
    public async Task<int> InitializeAsync(
        Templater templater,
        IReadOnlyList<FuncTemplateInfo> candidates,
        InitContext context,
        string stack,
        ParseResult parseResult,
        IReadOnlyList<Option> stackOptions,
        IReadOnlyList<string> templateArguments,
        Func<CancellationToken, Task<bool>> prepareDestinationAsync,
        Action writeProjectConfiguration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(templater);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        ArgumentNullException.ThrowIfNull(parseResult);
        ArgumentNullException.ThrowIfNull(stackOptions);
        ArgumentNullException.ThrowIfNull(templateArguments);
        ArgumentNullException.ThrowIfNull(prepareDestinationAsync);
        ArgumentNullException.ThrowIfNull(writeProjectConfiguration);
        if (candidates.Count == 0)
        {
            throw new ArgumentException("At least one project template candidate is required.", nameof(candidates));
        }

        string defaultShortName = $"func-{stack}";
        FuncTemplateInfo[] defaults = [.. candidates
            .Where(template => template.ShortNameList.Contains(defaultShortName, StringComparer.OrdinalIgnoreCase))];
        if (defaults.Length != 1)
        {
            string identities = string.Join(", ", candidates.Select(template => template.Identity));
            _interaction.WriteError(
                $"Expected one default project template named '{defaultShortName}' for language '{context.Language}', " +
                $"but found {defaults.Length}. Matching templates: {identities}.");
            return 1;
        }

        FuncTemplateInfo template = defaults[0];
        IReadOnlyDictionary<string, string> parameters;
        try
        {
            parameters = BindTemplateParameters(template, parseResult, stackOptions, templateArguments);
        }
        catch (InvalidOperationException ex)
        {
            _interaction.WriteError(ex.Message);
            return 1;
        }

        string outputPath = context.WorkingDirectory.Info.FullName;
        ITemplateCreationResult preflightResult = await templater.InstantiateAsync(
            template,
            context.ProjectName,
            outputPath,
            parameters,
            context.Force,
            cancellationToken,
            dryRun: true);
        if ((preflightResult.Status != CreationResultStatus.Success
                && !(context.Force && preflightResult.Status == CreationResultStatus.DestructiveChangesDetected))
            || (!context.Force && HasExistingFileChanges(preflightResult)))
        {
            _interaction.WriteError(DescribeCreationFailure(template, preflightResult));
            return 1;
        }

        if (!await prepareDestinationAsync(cancellationToken))
        {
            return 1;
        }

        ITemplateCreationResult result = await templater.InstantiateAsync(
            template,
            context.ProjectName,
            outputPath,
            parameters,
            context.Force,
            cancellationToken);
        if (result.Status != CreationResultStatus.Success)
        {
            _interaction.WriteError(DescribeCreationFailure(template, result));
            return 1;
        }

        writeProjectConfiguration();
        return 0;
    }

    private static IReadOnlyDictionary<string, string> BindTemplateParameters(
        FuncTemplateInfo template,
        ParseResult parseResult,
        IReadOnlyList<Option> stackOptions,
        IReadOnlyList<string> templateArguments)
    {
        var templateCommand = new Command("template");
        List<Option> declaredOptions = GetDeclaredOptions(parseResult.CommandResult.Command);
        Dictionary<string, ITemplateParameter> parametersByAlias = [with(StringComparer.OrdinalIgnoreCase)];
        Dictionary<Option, ITemplateParameter> parametersByOption = [];

        foreach ((string parameterName, FuncTemplateSymbol symbol) in template.HostData.SymbolInfo)
        {
            if (!template.ParameterDefinitions.TryGetValue(parameterName, out ITemplateParameter? parameter))
            {
                continue;
            }

            string? longName = ReadTemplateOptionName(template, parameterName, "longName", symbol.LongName);
            if (longName is null)
            {
                continue;
            }

            string? shortName = ReadTemplateOptionName(template, parameterName, "shortName", symbol.ShortName);
            string[] aliases = shortName is null ? [$"--{longName}"] : [$"--{longName}", $"-{shortName}"];
            foreach (string alias in aliases)
            {
                if (!parametersByAlias.TryAdd(alias, parameter))
                {
                    throw new InvalidOperationException(
                        $"Template '{template.Identity}' has conflicting func.host.json option '{alias}'.");
                }

                Option? commandOption = declaredOptions
                    .FirstOrDefault(option => HasOptionAlias(option, alias));
                if (commandOption is not null && !stackOptions.Contains(commandOption))
                {
                    throw new InvalidOperationException(
                        $"Template '{template.Identity}' has func.host.json option '{alias}' that conflicts with a built-in init option.");
                }
            }

            Option[] matchingStackOptions = [.. stackOptions
                .Where(option => aliases.Any(alias => HasOptionAlias(option, alias)))
                .Distinct()];
            if (matchingStackOptions.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Template '{template.Identity}' has func.host.json aliases that map to multiple init options.");
            }

            if (matchingStackOptions.Length == 1)
            {
                continue;
            }

            Option templateOption = IsBooleanParameter(parameter)
                ? new Option<bool>(aliases[0], aliases[1..])
                : new Option<string>(aliases[0], aliases[1..])
                {
                    Arity = string.IsNullOrEmpty(parameter.DefaultIfOptionWithoutValue)
                        ? ArgumentArity.ExactlyOne
                        : ArgumentArity.ZeroOrOne
                };
            templateCommand.Options.Add(templateOption);
            parametersByOption.Add(templateOption, parameter);
        }

        Dictionary<string, string> values = [with(StringComparer.Ordinal)];
        foreach (Option option in stackOptions)
        {
            if (parseResult.GetResult(option) is not { Implicit: false } optionResult)
            {
                continue;
            }

            string alias = optionResult.IdentifierToken?.Value ?? option.Name;
            if (!parametersByAlias.TryGetValue(alias, out ITemplateParameter? parameter))
            {
                throw new InvalidOperationException($"Option '{alias}' is not supported by template '{template.Identity}'.");
            }

            AddTemplateParameterValue(values, parameter, optionResult);
        }

        ParseResult templateParseResult = templateCommand.Parse(["template", .. templateArguments]);
        if (templateParseResult.UnmatchedTokens.Count > 0)
        {
            string token = templateParseResult.UnmatchedTokens[0];
            throw new InvalidOperationException(token.StartsWith('-')
                ? $"Unrecognized option '{token}'."
                : $"Unrecognized argument '{token}'. Specify the project path before template options.");
        }

        if (templateParseResult.Errors.Count > 0)
        {
            throw new InvalidOperationException(templateParseResult.Errors[0].Message);
        }

        foreach ((Option option, ITemplateParameter parameter) in parametersByOption)
        {
            AddTemplateParameterValue(values, parameter, templateParseResult.GetResult(option));
        }

        return values;
    }

    private static void AddTemplateParameterValue(
        Dictionary<string, string> values,
        ITemplateParameter parameter,
        OptionResult? optionResult)
    {
        if (optionResult is null || optionResult.Implicit)
        {
            return;
        }

        string alias = optionResult.IdentifierToken?.Value ?? optionResult.Option.Name;
        if (optionResult.IdentifierTokenCount > 1)
        {
            throw new InvalidOperationException($"Option '{alias}' was specified more than once.");
        }

        if (optionResult.Tokens.Count > 0)
        {
            values[parameter.Name] = optionResult.Tokens[0].Value;
        }
        else if (!string.IsNullOrEmpty(parameter.DefaultIfOptionWithoutValue))
        {
            values[parameter.Name] = parameter.DefaultIfOptionWithoutValue;
        }
        else if (IsBooleanParameter(parameter))
        {
            values[parameter.Name] = "true";
        }
        else
        {
            throw new InvalidOperationException($"Option '{alias}' requires a value.");
        }
    }

    private static string? ReadTemplateOptionName(
        FuncTemplateInfo template,
        string parameterName,
        string propertyName,
        string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        if (!char.IsLetterOrDigit(name[0]) || !name.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.'))
        {
            throw new InvalidOperationException(
                $"Template '{template.Identity}' has invalid func.host.json {propertyName} for symbol '{parameterName}'.");
        }

        return name;
    }

    private static bool IsBooleanParameter(ITemplateParameter parameter)
        => string.Equals(parameter.DataType, "bool", StringComparison.OrdinalIgnoreCase)
            || string.Equals(parameter.DataType, "boolean", StringComparison.OrdinalIgnoreCase);

    private static bool HasOptionAlias(Option option, string alias)
        => string.Equals(option.Name, alias, StringComparison.OrdinalIgnoreCase)
            || option.Aliases.Contains(alias, StringComparer.OrdinalIgnoreCase);

    private static List<Option> GetDeclaredOptions(Command command)
    {
        List<Option> options = [.. command.Options];
        for (Command? parent = command.Parents.OfType<Command>().FirstOrDefault(); parent is not null;
            parent = parent.Parents.OfType<Command>().FirstOrDefault())
        {
            options.AddRange(parent.Options.Where(option => option.Recursive));
        }

        return options;
    }

    private static string DescribeCreationFailure(FuncTemplateInfo template, ITemplateCreationResult result)
    {
        if (HasExistingFileChanges(result) || result.Status == CreationResultStatus.DestructiveChangesDetected)
        {
            string files = string.Join(", ", result.CreationEffects?.FileChanges
                .Where(change => change.ChangeKind != ChangeKind.Create)
                .Select(change => change.TargetRelativePath) ?? []);
            return $"Cannot initialize the project because these files already exist: {files}. "
                + "Use --force to replace the directory contents.";
        }

        return $"Failed to initialize the project from template '{template.Identity}': {result.ErrorMessage ?? result.Status.ToString()}";
    }

    private static bool HasExistingFileChanges(ITemplateCreationResult result)
        => result.CreationEffects?.FileChanges.Any(change => change.ChangeKind != ChangeKind.Create) == true;
}
