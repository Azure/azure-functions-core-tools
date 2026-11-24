// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

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

        if (request.UnmatchedTokens.Count > 0)
        {
            return _workflow.RejectUnmatchedTokens(request.UnmatchedTokens);
        }

        var context = new InitContext(request.WorkingDirectory, request.ProjectName, language, request.Force);
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

        await initializer.InitializeAsync(context, request.ParseResult, cancellationToken);

        return await _workflow.ReportInitializedAsync(request.WorkingDirectory.Info, initializer.Stack, cancellationToken);
    }
}
