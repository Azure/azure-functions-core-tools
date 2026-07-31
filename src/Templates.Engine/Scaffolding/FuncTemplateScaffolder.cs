// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Constraints;
using Microsoft.TemplateEngine.Edge.Template;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Materializes a template through the engine's <see cref="TemplateCreator"/>.
/// Create-flow templates dry-run into the project first (conflicts without
/// <c>--force</c> become <see cref="TemplateApplicationResult.AlreadyExists"/>
/// with nothing written); append-flow templates instantiate into a staging
/// directory and let the append post-action touch the project. Post actions
/// are dispatched through the allowlisted <see cref="IFuncPostActionDispatcher"/>.
/// </summary>
internal sealed class FuncTemplateScaffolder(
    IFuncTemplateEngineSession session,
    IFuncTemplateConstraintEvaluator constraintEvaluator,
    IFuncTemplateMountFileReader mountFileReader,
    IFuncPostActionDispatcher postActionDispatcher,
    IFuncExtensionBundleContextAccessor bundleContextAccessor,
    IFuncProjectDirectoryAccessor projectDirectoryAccessor,
    IFuncTemplateStagingArea stagingArea,
    ILogger<FuncTemplateScaffolder> logger) : IFuncTemplateScaffolder
{
    private const string DefaultBundleId = "Microsoft.Azure.Functions.ExtensionBundle";

    private readonly IFuncTemplateEngineSession _session = session ?? throw new ArgumentNullException(nameof(session));
    private readonly IFuncTemplateConstraintEvaluator _constraintEvaluator = constraintEvaluator ?? throw new ArgumentNullException(nameof(constraintEvaluator));
    private readonly IFuncTemplateMountFileReader _mountFileReader = mountFileReader ?? throw new ArgumentNullException(nameof(mountFileReader));
    private readonly IFuncPostActionDispatcher _postActionDispatcher = postActionDispatcher ?? throw new ArgumentNullException(nameof(postActionDispatcher));
    private readonly IFuncExtensionBundleContextAccessor _bundleContextAccessor = bundleContextAccessor ?? throw new ArgumentNullException(nameof(bundleContextAccessor));
    private readonly IFuncProjectDirectoryAccessor _projectDirectoryAccessor = projectDirectoryAccessor ?? throw new ArgumentNullException(nameof(projectDirectoryAccessor));
    private readonly IFuncTemplateStagingArea _stagingArea = stagingArea ?? throw new ArgumentNullException(nameof(stagingArea));
    private readonly ILogger<FuncTemplateScaffolder> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public async Task<TemplateApplicationResult> ApplyAsync(NewContext context, ParseResult parseResult, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(parseResult);

        string projectDirectory = Path.GetFullPath(context.WorkingDirectory.Info.FullName);

        ITemplateInfo? engineTemplate = await ResolveTemplateAsync(context, cancellationToken);
        if (engineTemplate is null)
        {
            return new TemplateApplicationResult.Failed(new TemplateApplicationFailure.ProviderError(
                $"Template '{context.Template.Id}' was not found for stack '{context.Template.Stack}'.", null));
        }

        TemplateApplicationResult? restriction = await CheckConstraintAsync(context, engineTemplate, cancellationToken);
        if (restriction is not null)
        {
            return restriction;
        }

        Dictionary<string, string?> inputParameters = BuildInputParameters(context.UserOptionValues);
        var creator = new TemplateCreator(_session.Settings);

        _projectDirectoryAccessor.Current = projectDirectory;
        try
        {
            return engineTemplate.PostActions.Contains(FuncPostActionIds.Append)
                ? await ApplyAppendFlowAsync(context, engineTemplate, creator, inputParameters, projectDirectory, cancellationToken)
                : await ApplyCreateFlowAsync(context, engineTemplate, creator, inputParameters, projectDirectory, cancellationToken);
        }
        finally
        {
            _projectDirectoryAccessor.Current = null;
        }
    }

    private async Task<TemplateApplicationResult> ApplyCreateFlowAsync(
        NewContext context,
        ITemplateInfo engineTemplate,
        TemplateCreator creator,
        Dictionary<string, string?> inputParameters,
        string projectDirectory,
        CancellationToken cancellationToken)
    {
        ITemplateCreationResult dryRun = await creator.InstantiateAsync(
            engineTemplate, context.FunctionName, context.FunctionName, projectDirectory,
            inputParameters, forceCreation: false, baselineName: null!, dryRun: true, cancellationToken);

        if (MapStatusFailure(dryRun) is { } dryRunFailure)
        {
            return new TemplateApplicationResult.Failed(dryRunFailure);
        }

        if (!context.Force)
        {
            IReadOnlyList<string> conflicts = FileChangesOf(dryRun)
                .Where(change => change.ChangeKind is ChangeKind.Overwrite or ChangeKind.Change or ChangeKind.Delete)
                .Select(change => change.TargetRelativePath)
                .ToList();
            if (conflicts.Count > 0)
            {
                return new TemplateApplicationResult.AlreadyExists(conflicts);
            }
        }

        ITemplateCreationResult creation = await creator.InstantiateAsync(
            engineTemplate, context.FunctionName, context.FunctionName, projectDirectory,
            inputParameters, forceCreation: context.Force, baselineName: null!, dryRun: false, cancellationToken);

        if (MapStatusFailure(creation) is { } failure)
        {
            return new TemplateApplicationResult.Failed(failure);
        }

        IReadOnlyList<string> createdFiles = WrittenFiles(creation);
        IReadOnlyDictionary<string, string?> parameterValues = BuildResolvedParameterValues(engineTemplate, inputParameters);

        FuncPostActionDispatchResult dispatch = await _postActionDispatcher.DispatchAsync(
            PostActionsOf(creation), projectDirectory, createdFiles, projectDirectory, context.FunctionName, parameterValues, cancellationToken);

        if (!dispatch.Succeeded)
        {
            return new TemplateApplicationResult.Failed(new TemplateApplicationFailure.ProviderError(
                dispatch.FailureMessage ?? "A post action failed.", dispatch.FailureException));
        }

        return new TemplateApplicationResult.Created(createdFiles)
        {
            Modified = dispatch.ModifiedFiles,
            Messages = dispatch.Messages,
        };
    }

    private async Task<TemplateApplicationResult> ApplyAppendFlowAsync(
        NewContext context,
        ITemplateInfo engineTemplate,
        TemplateCreator creator,
        Dictionary<string, string?> inputParameters,
        string projectDirectory,
        CancellationToken cancellationToken)
    {
        InjectAppendBindings(engineTemplate, inputParameters);

        string stagingDirectory = _stagingArea.Create();
        bool preserveStaging = false;
        try
        {
            ITemplateCreationResult creation = await creator.InstantiateAsync(
                engineTemplate, context.FunctionName, context.FunctionName, stagingDirectory,
                inputParameters, forceCreation: true, baselineName: null!, dryRun: false, cancellationToken);

            if (MapStatusFailure(creation) is { } failure)
            {
                return new TemplateApplicationResult.Failed(failure);
            }

            IReadOnlyList<string> stagedFiles = WrittenFiles(creation);
            IReadOnlyDictionary<string, string?> parameterValues = BuildResolvedParameterValues(engineTemplate, inputParameters);

            FuncPostActionDispatchResult dispatch = await _postActionDispatcher.DispatchAsync(
                PostActionsOf(creation), stagingDirectory, stagedFiles, projectDirectory, context.FunctionName, parameterValues, cancellationToken);

            if (!dispatch.Succeeded)
            {
                // Honour the handler's promise: when it pointed the user at the
                // staged snippet for manual recovery, that path must survive.
                preserveStaging = dispatch.PreserveStagedContent;
                return new TemplateApplicationResult.Failed(new TemplateApplicationFailure.ProviderError(
                    dispatch.FailureMessage ?? "The append post action failed.", dispatch.FailureException));
            }

            return new TemplateApplicationResult.Created([])
            {
                Modified = dispatch.ModifiedFiles,
                Messages = dispatch.Messages,
            };
        }
        finally
        {
            if (!preserveStaging)
            {
                _stagingArea.Cleanup(stagingDirectory);
            }
        }
    }

    private void InjectAppendBindings(ITemplateInfo engineTemplate, Dictionary<string, string?> inputParameters)
    {
        string? templateJson = _mountFileReader.TryReadFile(engineTemplate, engineTemplate.ConfigPlace);
        FuncAppendActionConfig? appendConfig = FuncTemplateConfigParser.TryReadAppendAction(templateJson);
        if (appendConfig?.TargetFileParam is not { } targetFileParam)
        {
            return;
        }

        string? targetDefault = GetParameterDefault(engineTemplate, targetFileParam);
        string? targetFile = inputParameters.TryGetValue(targetFileParam, out string? supplied) ? supplied : targetDefault;

        // A custom target file binds to a blueprint object; the default app file binds to the app.
        bool isCustomTarget = !string.IsNullOrEmpty(targetFile)
            && !string.Equals(targetFile, targetDefault, StringComparison.OrdinalIgnoreCase);
        string appObject = isCustomTarget ? "bp" : "app";

        if (targetFile is not null)
        {
            inputParameters[targetFileParam] = targetFile;
        }

        if (appendConfig.AppObjectParam is { } appObjectParam)
        {
            inputParameters[appObjectParam] = appObject;
        }
    }

    private async Task<ITemplateInfo?> ResolveTemplateAsync(NewContext context, CancellationToken cancellationToken)
    {
        IReadOnlyList<ITemplateInfo> all = await _session.PackageManager.GetTemplatesAsync(cancellationToken);
        return all.FirstOrDefault(t =>
            t.IsItemTemplate()
            && t.MatchesStack(context.Template.Stack)
            && t.MatchesLanguage(context.Language)
            && t.ShortNameList.Contains(context.Template.Id, StringComparer.OrdinalIgnoreCase));
    }

    private async Task<TemplateApplicationResult?> CheckConstraintAsync(
        NewContext context,
        ITemplateInfo engineTemplate,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, TemplateConstraintEvaluation> constraints =
            await _constraintEvaluator.EvaluateAsync([engineTemplate], cancellationToken);

        if (!constraints.TryGetValue(engineTemplate.Identity, out TemplateConstraintEvaluation? evaluation) || evaluation.IsAllowed)
        {
            return null;
        }

        if (_bundleContextAccessor.Current is null)
        {
            _logger.LogDebug("Template {TemplateId} is restricted and no extension bundle is configured.", engineTemplate.Identity);
            return new TemplateApplicationResult.Failed(new TemplateApplicationFailure.MissingExtensionBundle(
                context.Template.Stack, ReadBundleId(engineTemplate)));
        }

        return new TemplateApplicationResult.Failed(
            new TemplateApplicationFailure.ProviderError(evaluation.ToRestrictionMessage(), null));
    }

    private static Dictionary<string, string?> BuildInputParameters(IReadOnlyDictionary<string, string?>? userOptionValues)
    {
        var inputParameters = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (userOptionValues is null)
        {
            return inputParameters;
        }

        foreach ((string key, string? value) in userOptionValues)
        {
            if (value is not null)
            {
                inputParameters[key] = value;
            }
        }

        return inputParameters;
    }

    private static IReadOnlyDictionary<string, string?> BuildResolvedParameterValues(
        ITemplateInfo template,
        IReadOnlyDictionary<string, string?> inputParameters)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (ITemplateParameter parameter in template.ParameterDefinitions)
        {
            values[parameter.Name] = parameter.DefaultValue;
        }

        foreach ((string key, string? value) in inputParameters)
        {
            values[key] = value;
        }

        return values;
    }

    private static IReadOnlyList<IPostAction> PostActionsOf(ITemplateCreationResult creation)
        => creation.CreationResult?.PostActions ?? [];

    private static IReadOnlyList<IFileChange> FileChangesOf(ITemplateCreationResult creation)
        => creation.CreationEffects?.FileChanges ?? [];

    private static IReadOnlyList<string> WrittenFiles(ITemplateCreationResult creation)
        => FileChangesOf(creation)
            .Where(change => change.ChangeKind != ChangeKind.Delete)
            .Select(change => change.TargetRelativePath)
            .ToList();

    private static string? GetParameterDefault(ITemplateInfo template, string name)
        => template.ParameterDefinitions
            .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))?.DefaultValue;

    private static string ReadBundleId(ITemplateInfo template)
    {
        foreach (TemplateConstraintInfo constraint in template.Constraints)
        {
            if (!string.Equals(constraint.Type, FuncTemplateTags.ExtensionBundleConstraintType, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(constraint.Args))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(constraint.Args);
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("id", out JsonElement id)
                    && id.ValueKind == JsonValueKind.String
                    && id.GetString() is { Length: > 0 } bundleId)
                {
                    return bundleId;
                }
            }
            catch (JsonException)
            {
                // Fall through to the default id when the constraint args are malformed.
            }
        }

        return DefaultBundleId;
    }

    private static TemplateApplicationFailure? MapStatusFailure(ITemplateCreationResult result) => result.Status switch
    {
        CreationResultStatus.Success => null,
        CreationResultStatus.MissingMandatoryParam => new TemplateApplicationFailure.InvalidPrompt(
            "(missing)", result.ErrorMessage ?? "A required parameter was not supplied."),
        CreationResultStatus.InvalidParamValues => new TemplateApplicationFailure.InvalidPrompt(
            "(invalid)", result.ErrorMessage ?? "One or more parameter values were invalid."),
        CreationResultStatus.CreateFailed => new TemplateApplicationFailure.WriteFailed(
            result.OutputBaseDirectory ?? string.Empty, result.ErrorMessage ?? "The template could not be created."),
        _ => new TemplateApplicationFailure.ProviderError(
            result.ErrorMessage ?? $"Template creation failed with status {result.Status}.", null),
    };
}
