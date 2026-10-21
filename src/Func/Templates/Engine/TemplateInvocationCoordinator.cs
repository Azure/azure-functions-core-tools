// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using System.Text.Json;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Parameters;
using Microsoft.TemplateEngine.Edge.Template;
using Microsoft.TemplateEngine.Orchestrator.RunnableProjects.ConfigModel;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Coordinates selected-source checks, native preview and creation without owning the engine session.
/// </summary>
internal sealed class TemplateInvocationCoordinator(ITemplateInvocationEngine engine)
{
    private readonly ITemplateInvocationEngine _engine = engine ?? throw new ArgumentNullException(nameof(engine));

    public void ThrowIfDisposed() => _engine.ThrowIfDisposed();

    internal bool IsBoundTo(ITemplateInvocationEngine engine) => ReferenceEquals(_engine, engine);

    public async Task<TemplateInvocationResult> InvokeAsync(
        ITemplateInfo template, TemplateConfigurationSnapshot configuration, TemplateInvocationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.OutputDirectory);
        ArgumentNullException.ThrowIfNull(request.Parameters);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        TemplateInvocationResult? configurationFailure = await ValidateSelectedConfigurationAsync(template, configuration, cancellationToken);
        if (configurationFailure is not null)
        {
            return configurationFailure;
        }

        TemplateInvocationResult? admissionFailure = ValidateAdmission(template, configuration, request);
        if (admissionFailure is not null)
        {
            return admissionFailure;
        }

        Dictionary<string, string?> parameters = new(request.Parameters, StringComparer.Ordinal);
        string output = request.OutputDirectory.Info.FullName;
        ITemplateCreationResult preview = await _engine.InstantiateAsync(
            template, request, CreateInputData(configuration.Parameters, parameters), true, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (preview.Status != CreationResultStatus.Success)
        {
            return new(MapCreationStatus(preview.Status, cancellationToken), [], preview.ErrorMessage ?? preview.Status.ToString());
        }

        IReadOnlyList<TemplateFileChange> changes = Array.AsReadOnly<TemplateFileChange>(
            [.. (preview.CreationEffects?.FileChanges ?? []).Select(change => TemplateFileChange.FromNative(change.TargetRelativePath, change.ChangeKind))]);
        HashSet<string> directoryOutputs = GetDirectoryOutputs(configuration, preview.CreationEffects);
        bool requiresForce = !request.Force && changes.Any(change => change.Kind != TemplateFileChangeKind.Create);
        if (!request.DryRun && requiresForce)
        {
            return new(TemplateInvocationStatus.FileConflict, changes, "Template would change existing files. Explicit file-conflict force is required.");
        }

        string? outputFailure = ValidateOutputPaths(output, changes);
        if (outputFailure is not null)
        {
            return new(TemplateInvocationStatus.Failed, [], outputFailure);
        }

        if (request.DryRun)
        {
            return new(TemplateInvocationStatus.Success, changes, requiresForce ? "Actual creation requires explicit file-conflict force." : null);
        }

        cancellationToken.ThrowIfCancellationRequested();
        configurationFailure = await ValidateSelectedConfigurationAsync(template, configuration, cancellationToken);
        if (configurationFailure is not null)
        {
            return configurationFailure;
        }

        outputFailure = ValidateOutputPaths(output, changes);
        if (outputFailure is not null)
        {
            return new(TemplateInvocationStatus.Failed, [], outputFailure);
        }

        ITemplateCreationResult created = await _engine.InstantiateAsync(
            template, request, CreateInputData(configuration.Parameters, parameters), false, cancellationToken);
        if (created.Status != CreationResultStatus.Success)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(MapCreationStatus(created.Status, cancellationToken), [], created.ErrorMessage ?? created.Status.ToString());
        }

        // Native success may already have committed files; preserve its effects even if cancellation arrives afterward.
        if (created.CreationEffects is null)
        {
            return new(TemplateInvocationStatus.EngineFailure, [], "The template engine did not report creation effects.");
        }

        IReadOnlyList<TemplateFileChange> written = Array.AsReadOnly<TemplateFileChange>(
            [.. created.CreationEffects.FileChanges.Select(change => TemplateFileChange.FromNative(change.TargetRelativePath, change.ChangeKind))]);
        if (!written.Select(change => change.Path).Order(StringComparer.Ordinal).SequenceEqual(changes.Select(change => change.Path).Order(StringComparer.Ordinal), StringComparer.Ordinal)
            || ValidateOutputPaths(output, changes) is not null
            || written.Any(change => !HasExpectedOutputKind(output, change, directoryOutputs)))
        {
            return new(TemplateInvocationStatus.EngineFailure, [], "Template creation did not produce the validated output paths.");
        }

        return new(TemplateInvocationStatus.Success, written);
    }

    private TemplateInvocationResult? ValidateAdmission(
        ITemplateInfo template, TemplateConfigurationSnapshot configuration, TemplateInvocationRequest request)
    {
        string? topologyFailure = TemplateOutputPathValidator.GetDiagnostic(
            configuration.Content, _engine.GetPopulatedParameters(template, configuration, request));
        if (topologyFailure is not null)
        {
            return new(TemplateInvocationStatus.InvalidTemplate, [], topologyFailure);
        }

        return configuration.Root.EnumerateObject().Any(property => string.Equals(property.Name, "postActions", StringComparison.OrdinalIgnoreCase)
            && property.Value.ValueKind != JsonValueKind.Null && (property.Value.ValueKind != JsonValueKind.Array || property.Value.GetArrayLength() > 0))
            ? new(TemplateInvocationStatus.Failed, [], "Item post-actions require a qualified dispatcher and are not supported by this invocation path.")
            : null;
    }

    private static HashSet<string> GetDirectoryOutputs(TemplateConfigurationSnapshot configuration, ICreationEffects? effects)
    {
        using var configurationStream = new MemoryStream(Encoding.UTF8.GetBytes(configuration.Content));
        string? placeholder = TemplateConfigModel.FromStream(configurationStream).PlaceholderFilename;
        IReadOnlyList<string> placeholderFilenames = string.IsNullOrWhiteSpace(placeholder) ? ["-.-", "_._"] : [placeholder];
        // Native placeholder source files represent directory effects, including when the directory is empty.
        return new((effects?.FileChanges ?? []).OfType<IFileChange2>()
            .Where(change => placeholderFilenames.Contains(Path.GetFileName(change.SourceRelativePath), StringComparer.Ordinal))
            .Select(change => change.TargetRelativePath), StringComparer.Ordinal);
    }

    private async Task<TemplateInvocationResult?> ValidateSelectedConfigurationAsync(
        ITemplateInfo template, TemplateConfigurationSnapshot configuration, CancellationToken cancellationToken)
    {
        TemplateConfigurationSnapshot current;
        try
        {
            current = await _engine.ReadConfigurationAsync(template, cancellationToken);
        }
        catch (InvalidTemplateMetadataException exception)
        {
            return new(TemplateInvocationStatus.Failed, [], $"Selected template configuration is invalid. Rerun selection: {exception.Message}");
        }
        catch (IOException exception)
        {
            return new(TemplateInvocationStatus.Failed, [], $"Selected template configuration is unreadable. Rerun selection: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            return new(TemplateInvocationStatus.Failed, [], $"Selected template configuration is unreadable. Rerun selection: {exception.Message}");
        }

        if (!string.Equals(current.Fingerprint, configuration.Fingerprint, StringComparison.Ordinal)
            || !string.Equals(current.HostConfigPlace, configuration.HostConfigPlace, StringComparison.Ordinal)
            || !string.Equals(current.HostData, configuration.HostData, StringComparison.Ordinal))
        {
            return new(TemplateInvocationStatus.TemplateChanged, [], "Selected template configuration changed. Rerun selection.");
        }

        return current.Type == TemplateType.Item && current.Type == configuration.Type
            ? null
            : new(TemplateInvocationStatus.InvalidTemplate, [], "Current mounted template type is not a qualified item template. Correct its type and rerun selection.");
    }

    private static TemplateInvocationStatus MapCreationStatus(CreationResultStatus status, CancellationToken cancellationToken)
        => status switch
        {
            CreationResultStatus.Success => TemplateInvocationStatus.Success,
            CreationResultStatus.InvalidParamValues => TemplateInvocationStatus.InvalidArguments,
            CreationResultStatus.MissingMandatoryParam => TemplateInvocationStatus.MissingRequiredParameters,
            CreationResultStatus.DestructiveChangesDetected => TemplateInvocationStatus.FileConflict,
            CreationResultStatus.NotFound => TemplateInvocationStatus.TemplateUnavailable,
            CreationResultStatus.TemplateIssueDetected or CreationResultStatus.CondtionsEvaluationMismatch => TemplateInvocationStatus.InvalidTemplate,
            CreationResultStatus.Cancelled => throw new OperationCanceledException(cancellationToken),
            _ => TemplateInvocationStatus.EngineFailure,
        };

    private static InputDataSet CreateInputData(IReadOnlyList<ITemplateParameter> definitions, IReadOnlyDictionary<string, string?> parameters)
    {
        List<InputParameterData> input = [];
        foreach (ITemplateParameter definition in definitions)
        {
            bool supplied = parameters.TryGetValue(definition.Name, out string? value);
            input.Add(new(definition, value, supplied ? DataSource.User : DataSource.NoSource,
                supplied ? InputDataStateUtil.GetInputDataState(value) : InputDataState.Unset));
        }

        return new(new ParameterDefinitionSet(definitions), input);
    }

    private bool HasExpectedOutputKind(string output, TemplateFileChange change, HashSet<string> directoryOutputs)
    {
        string path = Path.Combine(output, change.Path);
        bool file = _engine.FileSystem.FileExists(path);
        bool directory = _engine.FileSystem.DirectoryExists(path);
        if (change.Kind == TemplateFileChangeKind.Delete)
        {
            return !file && !directory;
        }

        return directoryOutputs.Contains(change.Path) ? directory && !file : file && !directory;
    }

    private string? ValidateOutputPaths(string output, IReadOnlyList<TemplateFileChange> changes)
    {
        foreach (TemplateFileChange change in changes)
        {
            string? path = Path.GetFullPath(Path.Combine(output, change.Path));
            string relative = Path.GetRelativePath(output, path);
            if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                return "Template output escapes the requested output directory.";
            }

            // The requested output directory is the boundary; linked parents do not redirect individual template paths.
            while (path is not null && !string.Equals(Path.TrimEndingDirectorySeparator(path), Path.TrimEndingDirectorySeparator(output), StringComparison.Ordinal))
            {
                try
                {
                    if ((_engine.FileSystem.GetFileAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    {
                        return "Template output uses a symbolic link or reparse point. Choose an unlinked output directory.";
                    }
                }
                catch (FileNotFoundException)
                {
                    path = Path.GetDirectoryName(path);
                    continue;
                }
                catch (DirectoryNotFoundException)
                {
                    path = Path.GetDirectoryName(path);
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    return "Template output path cannot be checked safely.";
                }
                catch (IOException)
                {
                    return "Template output path cannot be checked safely.";
                }

                path = Path.GetDirectoryName(path);
            }
        }

        return null;
    }
}
