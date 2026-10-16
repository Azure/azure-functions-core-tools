// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Parsing;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Composes fresh public command definitions with native runners for local parity harnesses only.
/// </summary>
internal sealed class NativeTemplateCommandAdapter(
    IInitExecutionRunner init,
    INewExecutionRunner items,
    InitExecutionRenderer initRenderer,
    NewExecutionRenderer itemRenderer,
    TemplateConstraintEvaluationSnapshot availability,
    INativeItemCompatibility? itemCompatibility = null,
    INativeSelectedItemEffects? selectedItemEffects = null,
    INativeProjectPreparation? projectPreparation = null)
{
    private readonly IInitExecutionRunner _init = init ?? throw new ArgumentNullException(nameof(init));
    private readonly INewExecutionRunner _items = items ?? throw new ArgumentNullException(nameof(items));
    private readonly InitExecutionRenderer _initRenderer = initRenderer ?? throw new ArgumentNullException(nameof(initRenderer));
    private readonly NewExecutionRenderer _itemRenderer = itemRenderer ?? throw new ArgumentNullException(nameof(itemRenderer));
    private readonly TemplateConstraintEvaluationSnapshot _availability = availability ?? throw new ArgumentNullException(nameof(availability));
    private readonly INativeItemCompatibility? _itemCompatibility = itemCompatibility;
    private readonly INativeSelectedItemEffects? _selectedItemEffects = selectedItemEffects;
    private readonly INativeProjectPreparation? _projectPreparation = projectPreparation;

    public ParseResult ParseInitInvocation(InitCommand command, string[] tokens, NativeInitExecutionOptions? execution = null)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(tokens);
        ValidateFreshCommand(command);
        string[] original = [.. tokens];
        execution ??= new();
        NativeInitExecutionOptions settings = execution;
        HashSet<Option> hostOptions = [command.StackOption, command.NameOption, command.LanguageOption, command.ForceOption];
        command.TreatUnmatchedTokensAsErrors = false;
        command.SetAction(async (parsed, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var input = NativeTemplateCommandInput.Capture(command, parsed, original, hostOptions);
            var request = new InitExecutionRequest(parsed.GetValue(command.PathArgument!)!, settings.TemplateReference,
                parsed.GetValue(command.NameOption), parsed.GetValue(command.StackOption), parsed.GetValue(command.LanguageOption),
                parsed.GetValue(command.ForceOption), settings.NonInteractive, settings.DryRun, settings.JsonOutput,
                input.NativeTokens, input.ReservedAliases)
            {
                ProjectPreparation = _projectPreparation,
            };
            InitExecutionResult result = await _init.ExecuteAsync(request, _availability, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return _initRenderer.Render(result with
            {
                ArgumentResults = Array.AsReadOnly<TemplateCandidateParseResult>([.. result.ArgumentResults.Select(input.RestoreProvenance)]),
            }, settings.JsonOutput);
        });
        return Parse(command, original);
    }

    public ParseResult ParseNewInvocation(NewCommand command, string[] tokens, bool dryRun = false)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(tokens);
        ValidateFreshCommand(command);
        string[] original = [.. tokens];
        HashSet<Option> hostOptions =
        [
            command.NameOption, command.TemplateOption, command.ForceOption, command.NonInteractiveOption,
            command.ListOption, command.OutputOption,
        ];
        command.SetAction(async (parsed, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? output = parsed.GetValue(command.OutputOption);
            bool json = string.Equals(output, "json", StringComparison.OrdinalIgnoreCase);
            if (output is not null && !json && !string.Equals(output, "plain", StringComparison.OrdinalIgnoreCase))
            {
                return _itemRenderer.Render(NewExecutionResult.Reject("invalidOutput", "Use --output plain or --output json."), jsonOutput: false);
            }

            var input = NativeTemplateCommandInput.Capture(command, parsed, original, hostOptions);
            var request = new NewExecutionRequest(parsed.GetValue(command.PathArgument!)!, parsed.GetValue(command.TemplateOption),
                parsed.GetValue(command.NameOption), ExplicitLanguage: null, parsed.GetValue(command.ForceOption),
                parsed.GetValue(command.NonInteractiveOption), dryRun, parsed.GetValue(command.ListOption), json,
                input.NativeTokens, input.ReservedAliases)
            {
                ItemCompatibility = _itemCompatibility,
                SelectedItemEffects = _selectedItemEffects,
            };
            NewExecutionResult result = await _items.ExecuteAsync(request, _availability, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return _itemRenderer.Render(result with
            {
                ArgumentResults = Array.AsReadOnly<TemplateCandidateParseResult>([.. result.ArgumentResults.Select(input.RestoreProvenance)]),
            }, json);
        });
        return Parse(command, original);
    }

    private static ParseResult Parse(FuncCliCommand command, string[] tokens)
    {
        FuncRootCommand root = [command];
        return root.Parse([root.Name, command.Name, .. tokens], new ParserConfiguration
        {
            EnablePosixBundling = false,
            ResponseFileTokenReplacer = null,
        });
    }

    private static void ValidateFreshCommand(FuncCliCommand command)
    {
        if (command.Parents.Any())
        {
            throw new ArgumentException("Use a fresh, unparented command definition for each parity invocation.", nameof(command));
        }
    }
}

internal sealed record NativeInitExecutionOptions(
    string? TemplateReference = null, bool NonInteractive = false, bool DryRun = false, bool JsonOutput = false);
