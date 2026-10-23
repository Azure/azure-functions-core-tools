// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Invocation;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli;

internal sealed class NativeEntryCommandBinding(IServiceProvider services, IReadOnlyList<string> originalArguments)
{
    private readonly IServiceProvider _services = services ?? throw new ArgumentNullException(nameof(services));
    private readonly Lazy<NativeTemplateWorkflowComposer> _composer = new(services.GetRequiredService<NativeTemplateWorkflowComposer>);
    private readonly NativeEntryArguments _arguments = new(originalArguments);
    private readonly IInteractionService _interaction = services.GetRequiredService<IInteractionService>();
    private readonly InitExecutionRenderer _projects = services.GetRequiredService<InitExecutionRenderer>();
    private readonly NewExecutionRenderer _items = services.GetRequiredService<NewExecutionRenderer>();
    private readonly Lazy<INewExecutionContextResolver> _contexts = new(services.GetRequiredService<INewExecutionContextResolver>);
    private readonly INativeTemplateAvailability _availability = services.GetRequiredService<INativeTemplateAvailability>();
    private readonly ITemplaterFactory _templates = services.GetRequiredService<ITemplaterFactory>();
    private readonly ITemplateArgumentParser _parser = services.GetRequiredService<ITemplateArgumentParser>();
    private readonly Lazy<NativeTemplateWorkflowRegistry> _workflows = new(services.GetRequiredService<NativeTemplateWorkflowRegistry>);
    private readonly Lazy<WorkloadNativeTemplatePolicies> _policies = new(services.GetRequiredService<WorkloadNativeTemplatePolicies>);
    private readonly Lazy<NativeTemplateStackCompatibility> _stackCompatibility = new(services.GetRequiredService<NativeTemplateStackCompatibility>);

    internal static bool IsJsonOutput(NewCommand command, ParseResult parsed)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(parsed);
        return string.Equals(parsed.GetResult(command.FormatOption)?.GetValueOrDefault<string?>(), "json", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsNonInteractive(NewCommand command, ParseResult parsed) => IsJsonOutput(command, parsed)
        || parsed.GetResult(command.NonInteractiveOption)?.GetValueOrDefault<bool>() == true;

    public IReadOnlyList<FuncCliCommand> CreateCommands()
    {
        InitCommand init = ActivatorUtilities.CreateInstance<InitCommand>(_services);
        NewCommand item = ActivatorUtilities.CreateInstance<NewCommand>(_services);
        init.TreatUnmatchedTokensAsErrors = false;
        init.PathArgument!.CustomParser = _ => Common.WorkingDirectory.FromCwd();
        item.PathArgument!.CustomParser = _ => Common.WorkingDirectory.FromCwd();
        init.SetAction((parsed, token) => ExecuteInitAsync(init, parsed, token));
        item.SetAction((parsed, token) => ExecuteNewAsync(item, parsed, token));
        return [init, item];
    }

    private async Task<int> ExecuteNewAsync(NewCommand command, ParseResult parsed, CancellationToken cancellationToken)
    {
        NewCommand.ValidateRenderingOptions(parsed);
        string? output = parsed.GetValue(command.FormatOption);
        bool json = IsJsonOutput(command, parsed);
        if (output is not null && !json && !string.Equals(output, "plain", StringComparison.OrdinalIgnoreCase))
        {
            return _items.Render(NewExecutionResult.Reject("invalidOutput", "Use --format plain or --format json."), false);
        }

        (IReadOnlyList<string> Tokens, int? PathIndex, Common.WorkingDirectory Directory) raw;
        try
        {
            raw = _arguments.Read(parsed, command);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return _items.Render(NewExecutionResult.Reject("invalidPath", $"Supply a valid project path. {exception.Message}"), json);
        }

        HashSet<Option> host = [command.NameOption, command.TemplateOption, command.ForceOption, command.NonInteractiveOption, command.ListOption, command.FormatOption];
        var input = NativeTemplateCommandInput.CaptureEntry(command, parsed, raw.Tokens, host, raw.PathIndex);
        var request = new NewExecutionRequest(raw.Directory, parsed.GetValue(command.TemplateOption), parsed.GetValue(command.NameOption),
            null, parsed.GetValue(command.ForceOption), IsNonInteractive(command, parsed), false, parsed.GetValue(command.ListOption),
            json, input.NativeTokens, input.ReservedAliases) { OutputIntent = NativeItemOutputIntent.LegacyWorkflowRelative };
        NewExecutionResult result = await _composer.Value.ExecuteItemAsync(request, cancellationToken);
        return _items.Render(result with
        {
            ArgumentResults = Array.AsReadOnly<TemplateCandidateParseResult>([.. result.ArgumentResults.Select(input.RestoreProvenance)]),
        }, json);
    }

    private async Task<int> ExecuteInitAsync(InitCommand command, ParseResult parsed, CancellationToken cancellationToken)
    {
        (IReadOnlyList<string> Tokens, int? PathIndex, Common.WorkingDirectory Directory) raw;
        try
        {
            raw = _arguments.Read(parsed, command);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return _projects.Render(InitExecutionResult.Reject("invalidPath", $"Supply a valid project path. {exception.Message}"), false);
        }
        var input = NativeTemplateCommandInput.CaptureEntry(command, parsed, raw.Tokens,
            new HashSet<Option> { command.StackOption, command.NameOption, command.LanguageOption, command.ForceOption }, raw.PathIndex);
        var request = new InitExecutionRequest(raw.Directory, null, parsed.GetValue(command.NameOption),
            parsed.GetValue(command.StackOption), parsed.GetValue(command.LanguageOption), parsed.GetValue(command.ForceOption),
            !_interaction.IsInteractive, false, false, input.NativeTokens, input.ReservedAliases)
        {
            TargetPolicy = InitExecutionTargetPolicy.PublicCompatible,
            ExistingProjectTokens = NativeTemplateCommandInput.CaptureEntry(command, parsed, raw.Tokens,
                command.Options.ToHashSet(), raw.PathIndex).NativeTokens,
        };
        InitExecutionResult result = await _composer.Value.ExecuteProjectAsync(request, cancellationToken);
        return _projects.Render(result with
        {
            ArgumentResults = Array.AsReadOnly<TemplateCandidateParseResult>([.. result.ArgumentResults.Select(input.RestoreProvenance)]),
        }, false);
    }

    public CommandLineAction CreateHelpAction(HelpCommand help, CommandLineAction fallback)
        => new NativeHelpAction(this, help, fallback);

    private sealed class NativeHelpAction(NativeEntryCommandBinding binding, HelpCommand help, CommandLineAction fallback) : AsynchronousCommandLineAction
    {
        public override async Task<int> InvokeAsync(ParseResult parseResult, CancellationToken cancellationToken = default)
        {
            if (parseResult.CommandResult.Command is not (InitCommand or NewCommand))
            {
                return fallback is AsynchronousCommandLineAction asynchronous
                    ? await asynchronous.InvokeAsync(parseResult, cancellationToken)
                    : ((SynchronousCommandLineAction)fallback).Invoke(parseResult);
            }

            Command command = parseResult.CommandResult.Command;
            help.RenderCommandHelp(command);
            if (command is not NewCommand item || parseResult.GetValue(item.TemplateOption) is not { } reference)
            {
                return 0;
            }

            (IReadOnlyList<string> Tokens, int? PathIndex, Common.WorkingDirectory Directory) raw = binding._arguments.Read(parseResult, item);
            var request = new NewExecutionRequest(raw.Directory, reference, null, null, false, true, false, false, false, [], new HashSet<string>());
            NewExecutionContextResult context = await binding._contexts.Value.ResolveAsync(request, false, cancellationToken);
            if (context.Failure is { } failure)
            {
                foreach (string diagnostic in failure.Diagnostics) binding._interaction.WriteError(diagnostic);
                return 1;
            }

            TemplateConstraintEvaluationSnapshot availability = await binding._availability.CaptureAsync(cancellationToken);
            availability = binding._stackCompatibility.Value.ForStack(availability, context.Context!.Project!.Stack!);
            using Templater templater = await binding._templates.CreateAsync(context.Context!, availability, cancellationToken);
            TemplateResolutionResult resolved;
            try
            {
                resolved = await templater.ResolveGroupAsync(reference, TemplateType.Item, cancellationToken);
            }
            catch (NativeTemplateCohortException exception)
            {
                binding._interaction.WriteError(exception.Message);
                return 1;
            }
            if (resolved.Status != TemplateResolutionStatus.Success)
            {
                binding._interaction.WriteError($"The requested item is unavailable: {resolved.Status}.");
                return 1;
            }

            ResolvedTemplate[] visible = [.. resolved.Group!.Where(candidate =>
                candidate.Entry.Language == context.Context!.Project!.Language && candidate.Entry.DeclaredInternalRole is null)];
            if (visible.Length == 0)
            {
                binding._interaction.WriteError("The selected item has no ordinary help surface in this project.");
                return 1;
            }

            var input = NativeTemplateCommandInput.CaptureEntry(item, parseResult, raw.Tokens,
                item.Options.ToHashSet(), raw.PathIndex);
            foreach (ResolvedTemplate selected in visible)
            {
                INativeItemWorkflowProvider? provider;
                try
                {
                    provider = await binding._policies.Value.FindAsync(selected, cancellationToken)
                        ?? binding._workflows.Value.FindItem(context.Context!.Project!.Stack!);
                }
                catch (Exception exception) when (exception is InvalidTemplateMetadataException or IOException or UnauthorizedAccessException)
                {
                    binding._interaction.WriteError(exception.Message);
                    return 1;
                }
                NativeItemHelpProjection overlay = provider is null ? new(selected.Parameters, [])
                    : await provider.DescribeHelpAsync(new(selected, context.Context), cancellationToken);
                if (overlay.Parameters.Count != selected.Parameters.Count || selected.Parameters.Any(parameter =>
                    !overlay.Parameters.Any(value => value.CanonicalName == parameter.CanonicalName
                        && (parameter with { DefaultValue = value.DefaultValue, Description = value.Description }) == value)))
                {
                    binding._interaction.WriteError("The owned help projection changed more than contextual defaults or descriptions.");
                    return 1;
                }

                foreach (string diagnostic in overlay.Diagnostics)
                {
                    binding._interaction.WriteHint(diagnostic);
                }

                TemplateHelpInfo projected = new NativeTemplateHelpProjector(binding._parser)
                    .Project(selected.Entry with { Parameters = overlay.Parameters }, input.ReservedAliases, cancellationToken);
                help.WriteOptionsSection($"Template Options ({projected.TemplateId})", projected.HydratedOptions);
            }

            return 0;
        }
    }
}
