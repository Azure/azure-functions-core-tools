// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Commands.Templating;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;

namespace Azure.Functions.Cli.Commands.Init;

/// <summary>
/// The template-based <c>func init</c> flow: creates projects from the installed <c>func-{stack}</c> project template,
/// and adopts or completes existing projects.
/// </summary>
/// <remarks>
/// Project content is owned by the template, which also owns the meaning of every option after the project path. The
/// selected stack contributes only non-scaffolding setup through
/// <see cref="IProjectInitializer.ConfigureProjectAsync"/>, and stack init options are never parsed in this flow.
/// The stack and language are validated by <see cref="InitWorkflow.ResolveAsync"/> before this handler runs.
/// </remarks>
internal sealed class TemplatedInitHandler(InitWorkflow workflow, ITemplaterFactory templaterFactory)
{
    private readonly InitWorkflow _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
    private readonly ITemplaterFactory _templaterFactory = templaterFactory
        ?? throw new ArgumentNullException(nameof(templaterFactory));

    /// <summary>
    /// Adopts a project that predates <c>.func/config.json</c>, or completes a config missing its language, without
    /// creating project content.
    /// </summary>
    /// <remarks>
    /// The stack and language are resolved by <see cref="InitWorkflow.ResolveAsync"/>, which never auto-selects the
    /// stack of an existing project. Completion adds only the missing language and preserves every other key in the
    /// config.
    /// </remarks>
    public async Task<int> ReconcileExistingAsync(InitRequest request,
        InitResolution resolution, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(resolution);
        DirectoryInfo directory = request.WorkingDirectory.Info;
        switch (resolution.Mode)
        {
            case InitMode.Complete:
                await _workflow.Configuration.CompleteLanguageAsync(
                    directory, resolution.Stack, resolution.Language, cancellationToken);
                _workflow.WriteSuccess("Existing project completed for ", resolution.Stack);
                return 0;

            case InitMode.Adopt:
                await _workflow.Configuration.CreateAsync(
                    directory, resolution.Stack, resolution.Language, cancellationToken);
                _workflow.WriteConfigWritten();
                _workflow.WriteSuccess("Existing project adopted for ", resolution.Stack);
                return 0;

            default:
                throw new ArgumentOutOfRangeException(nameof(resolution), resolution.Mode, null);
        }
    }

    /// <summary>
    /// Creates the project from the installed <c>func-{stack}</c> project template for the resolved language, then
    /// writes <c>.func/config.json</c>.
    /// </summary>
    /// <returns>
    /// The exit code, or <see langword="null"/> when no project template is installed for the resolved language.
    /// </returns>
    public async Task<int?> TryCreateAsync(InitRequest request,
        InitResolution resolution, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(resolution);

        IProjectInitializer initializer = resolution.Initializer;
        string stackId = resolution.Stack.Descriptor.Identifier;
        string language = TemplateLanguage.ToTemplateTag(resolution.Language);
        using Templater templater = _templaterFactory.Create(new TemplateEngineContext(
            request.WorkingDirectory,
            new TemplateEngineProjectContext(request.WorkingDirectory, stackId, language)));
        IReadOnlyList<FuncTemplateInfo> candidates;
        try
        {
            candidates = (await templater.ListProjectTemplatesAsync(language, cancellationToken)).Templates;
        }
        catch (Exception ex)
            when (ex is InvalidOperationException or JsonException or IOException or UnauthorizedAccessException)
        {
            _workflow.Interaction.WriteError(ex.Message);
            return 1;
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        // Once project templates exist for the language, failures never fall back to stack scaffolding.
        string defaultShortName = $"func-{stackId}";
        FuncTemplateInfo[] defaults = [.. candidates
            .Where(template => template.ShortNameList.Contains(defaultShortName, StringComparer.OrdinalIgnoreCase))];
        if (defaults.Length != 1)
        {
            string identities = string.Join(", ", candidates.Select(template => template.Identity));
            _workflow.Interaction.WriteError(
                $"Expected one default project template named '{defaultShortName}' for language '{language}', " +
                $"but found {defaults.Length}. Matching templates: {identities}.");
            return 1;
        }

        FuncTemplateInfo template = defaults[0];
        IReadOnlyDictionary<string, string> parameters;
        try
        {
            parameters = TemplateArgumentBinder.Bind(
                template,
                request.UnmatchedTokens,
                request.ReservedOptions,
                out IReadOnlyList<string> unrecognizedTokens);
            if (unrecognizedTokens.Count > 0)
            {
                return _workflow.RejectUnmatchedTokens(unrecognizedTokens);
            }
        }
        catch (InvalidOperationException ex)
        {
            _workflow.Interaction.WriteError(ex.Message);
            return 1;
        }

        InitContext context = new(request.WorkingDirectory, request.ProjectName, resolution.Language, request.Force);
        IProjectStack stack = resolution.Stack;
        string outputPath = request.WorkingDirectory.Info.FullName;
        ITemplateCreationResult preflight = await template.InstantiateAsync(
            context.ProjectName, outputPath, parameters, context.Force, dryRun: true, cancellationToken);
        if ((preflight.Status != CreationResultStatus.Success
                && !(context.Force && preflight.Status == CreationResultStatus.DestructiveChangesDetected))
            || (!context.Force && HasExistingFileChanges(preflight)))
        {
            _workflow.Interaction.WriteError(DescribeCreationFailure(stack, template, preflight));
            return 1;
        }

        if (!await _workflow.PrepareDestinationAsync(context, cancellationToken))
        {
            return 1;
        }

        ITemplateCreationResult result;
        try
        {
            result = await template.InstantiateAsync(
                context.ProjectName, outputPath, parameters, context.Force, dryRun: false, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The engine reports generation failures in the result; outside it, only loading the template's files
            // from its package is expected to fail.
            return _workflow.ReportCreationFailed(request, DescribeTemplateFailure(stack, template, ex.Message));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (result.Status != CreationResultStatus.Success)
        {
            // Files that appeared after the dry run block creation before anything is written.
            if (IsConflict(result))
            {
                _workflow.Interaction.WriteError(DescribeCreationFailure(stack, template, result));
                return 1;
            }

            string reason = result.ErrorMessage ?? result.Status.ToString();
            return _workflow.ReportCreationFailed(request, DescribeTemplateFailure(stack, template, reason));
        }

        try
        {
            await initializer.ConfigureProjectAsync(context, cancellationToken);
        }
        catch (Exception ex) when (InitWorkflow.IsExpectedStackFailure(ex))
        {
            return _workflow.ReportCreationFailed(
                request, $"Couldn't set up the {stack.Descriptor.DisplayName} project: {ex.Message}");
        }

        // Stack setup may observe cancellation and return normally; a cancelled init never reports success.
        cancellationToken.ThrowIfCancellationRequested();

        // Template post-actions aren't run yet; tracked in #6023.

        // The config is written last, so a failed init leaves none behind.
        if (!await _workflow.SaveCreatedProjectConfigAsync(request, resolution, cancellationToken))
        {
            return 1;
        }

        return await _workflow.ReportInitializedAsync(request.WorkingDirectory.Info, stack, cancellationToken);
    }

    private static string DescribeCreationFailure(IProjectStack stack, FuncTemplateInfo template,
        ITemplateCreationResult result)
    {
        if (IsConflict(result))
        {
            string files = string.Join(", ", result.CreationEffects?.FileChanges
                .Where(change => change.ChangeKind != ChangeKind.Create)
                .Select(change => change.TargetRelativePath) ?? []);
            return $"Cannot initialize the project because these files already exist: {files}. "
                + "Use --force to replace the directory contents.";
        }

        return DescribeTemplateFailure(stack, template, result.ErrorMessage ?? result.Status.ToString());
    }

    private static string DescribeTemplateFailure(IProjectStack stack, FuncTemplateInfo template, string reason)
        => $"Couldn't create the {stack.Descriptor.DisplayName} project from template '{template.Identity}': {reason}";

    private static bool IsConflict(ITemplateCreationResult result)
        => HasExistingFileChanges(result) || result.Status == CreationResultStatus.DestructiveChangesDetected;

    private static bool HasExistingFileChanges(ITemplateCreationResult result)
        => result.CreationEffects?.FileChanges.Any(change => change.ChangeKind != ChangeKind.Create) == true;
}
