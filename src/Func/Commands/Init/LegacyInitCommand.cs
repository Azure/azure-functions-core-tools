// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands.Templating;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Commands.Init;

/// <summary>
/// Creates a project through the selected stack's existing <see cref="IProjectInitializer.InitializeAsync"/>
/// scaffolding when no installed project template applies.
/// </summary>
/// <remarks>
/// Transitional: removed together with <see cref="InitCommand"/> routing once every stack creates projects from
/// installed templates.
/// </remarks>
internal sealed class LegacyInitCommand(InitWorkflow workflow)
{
    private readonly InitWorkflow _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));

    /// <summary>
    /// Writes <c>.func/config.json</c>, then scaffolds the project with the stack's initializer.
    /// </summary>
    public async Task<int> CreateAsync(InitRequest request,
        InitResolution resolution, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(resolution);

        // Stack options are typed only on this route, so their validation cannot reject a template invocation.
        ParseResult stackParse = request.StackOptionsCommand.Parse(
            ["init", .. request.UnmatchedTokens], TemplateCommand.TokenParserConfiguration);
        if (TemplateCommand.GetInvalidBooleanValueError(stackParse) is { } invalidBooleanValue)
        {
            _workflow.Interaction.WriteError(invalidBooleanValue);
            return 1;
        }

        if (stackParse.UnmatchedTokens.Count > 0)
        {
            return _workflow.RejectUnmatchedTokens(stackParse.UnmatchedTokens);
        }

        if (stackParse.Errors.Count > 0)
        {
            _workflow.Interaction.WriteError(stackParse.Errors[0].Message);
            return 1;
        }

        InitContext context = new(request.WorkingDirectory, request.ProjectName, resolution.Language, request.Force);
        if (!await _workflow.PrepareDestinationAsync(context, cancellationToken))
        {
            return 1;
        }

        if (!await _workflow.SaveProjectConfigAsync(request, resolution, cancellationToken))
        {
            return 1;
        }

        _workflow.Interaction.WriteBlankLine();
        try
        {
            await resolution.Initializer.InitializeAsync(context, stackParse, cancellationToken);
        }
        catch (Exception ex) when (InitWorkflow.IsExpectedStackFailure(ex))
        {
            return _workflow.ReportCreationFailed(
                request, $"Couldn't create the {resolution.Stack.Descriptor.DisplayName} project: {ex.Message}");
        }

        cancellationToken.ThrowIfCancellationRequested();

        return await _workflow.ReportInitializedAsync(
            request.WorkingDirectory.Info, resolution.Stack, cancellationToken);
    }
}
