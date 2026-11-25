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

    public async Task<int> CreateAsync(
        InitRequest request,
        IProjectInitializer initializer,
        string? language,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(initializer);

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

        InitContext context = new(request.WorkingDirectory, request.ProjectName, language, request.Force);
        request.WorkingDirectory.CreateIfNotExists();
        if (!await _workflow.PrepareDestinationAsync(context, cancellationToken))
        {
            return 1;
        }

        _workflow.WriteCliConfigurationFile(
            request.WorkingDirectory.Info,
            initializer.Stack,
            InitWorkflow.GetPersistedLanguage(initializer, language),
            request.Force);
        _workflow.Interaction.WriteBlankLine();

        await initializer.InitializeAsync(context, stackParse, cancellationToken);

        return await _workflow.ReportInitializedAsync(
            request.WorkingDirectory.Info, initializer.Stack, cancellationToken);
    }
}
