// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using System.Text.Json;
using Azure.Functions.Cli.Common;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Parameters;
using Microsoft.TemplateEngine.Edge.Template;
using Microsoft.TemplateEngine.Orchestrator.RunnableProjects.ConfigModel;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Coordinates selected-source checks, native preview and creation without owning the engine session.
/// </summary>
internal sealed class TemplateInvocationCoordinator(ITemplateInvocationEngine engine, IFileSystem fileSystem)
{
    private readonly ITemplateInvocationEngine _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

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

        if (configuration.Root.EnumerateObject().Any(property => string.Equals(property.Name, "postActions", StringComparison.OrdinalIgnoreCase)
            && property.Value.ValueKind != JsonValueKind.Null && (property.Value.ValueKind != JsonValueKind.Array || property.Value.GetArrayLength() > 0)))
        {
            return new(TemplateInvocationStatus.Failed, [], "Item post-actions require a qualified dispatcher and are not supported by this invocation path.");
        }

        string output = request.OutputDirectory.Info.FullName;
        using TemplateOutputImage image = new(_engine.FileSystem, output, cancellationToken);
        ITemplateCreationResult native;
        try
        {
            native = await _engine.RenderImageAsync(template, request,
                CreateInputData(configuration.Parameters, request.Parameters), image, cancellationToken);
        }
        catch (InvalidTemplateMetadataException exception)
        {
            return new(TemplateInvocationStatus.Failed, [], exception.Message);
        }
        catch (IOException exception)
        {
            return new(TemplateInvocationStatus.Failed, [], $"Native output could not be captured: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            return new(TemplateInvocationStatus.Failed, [], $"Native output could not be captured: {exception.Message}");
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (image.Diagnostic is { } diagnostic)
        {
            return new(TemplateInvocationStatus.Failed, [], diagnostic);
        }
        if (image.ConflictDiagnostic is { } conflict)
        {
            return new(TemplateInvocationStatus.FileConflict, [], conflict);
        }
        if (native.Status != CreationResultStatus.Success)
        {
            return new(MapCreationStatus(native.Status, cancellationToken), [], native.ErrorMessage ?? native.Status.ToString());
        }
        if (native.CreationEffects is null || native.CreationResult is null)
        {
            return new(TemplateInvocationStatus.EngineFailure, [], "The template engine did not report its creation result and effects.");
        }
        foreach (IFileChange change in native.CreationEffects.FileChanges)
        {
            _ = TemplateFileChange.FromNative(change.TargetRelativePath, change.ChangeKind);
        }
        if (native.CreationResult.PostActions.Count != 0)
        {
            return new(TemplateInvocationStatus.Failed, [], "Native creation produced unsupported item post-actions.");
        }

        IReadOnlyList<TemplateFileChange> changes = DescribeActualOutputs(image, configuration, native.CreationEffects);
        string? outputFailure = changes.Select(change => image.GetPathDiagnostic(image.GetTarget(change.Path)))
            .FirstOrDefault(diagnostic => diagnostic is not null);
        if (outputFailure is not null)
        {
            return new(TemplateInvocationStatus.Failed, [], outputFailure);
        }
        configurationFailure = await ValidateSelectedConfigurationAsync(template, configuration, cancellationToken);
        if (configurationFailure is not null)
        {
            return configurationFailure;
        }

        bool requiresForce = !request.Force && changes.Any(change => change.Kind != TemplateFileChangeKind.Create);
        if (request.DryRun)
        {
            return new(TemplateInvocationStatus.Success, changes, requiresForce ? "Actual creation requires explicit file-conflict force." : null);
        }
        if (requiresForce)
        {
            return new(TemplateInvocationStatus.FileConflict, changes, "Template would change existing files. Explicit file-conflict force is required.");
        }

        return await InstallAsync(template, configuration, request, image, changes, cancellationToken);
    }

    private IReadOnlyList<TemplateFileChange> DescribeActualOutputs(
        TemplateOutputImage image, TemplateConfigurationSnapshot configuration, ICreationEffects effects)
    {
        StringComparer comparer = Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        HashSet<string> files = new(image.Files, comparer);
        HashSet<string> directories = new(image.Directories, comparer);
        HashSet<string> reportedDirectories = new(GetDirectoryOutputs(configuration, effects)
            .Select(path => image.GetRelative(image.GetTarget(path))!)
            .Where(directories.Contains), comparer);
        foreach (string directory in directories.Where(path => path != "."))
        {
            if (!files.Any(file => IsDescendant(image.GetTarget(directory), image.GetTarget(file)))
                && !directories.Any(child => child != directory && IsDescendant(image.GetTarget(directory), image.GetTarget(child))))
            {
                reportedDirectories.Add(directory);
            }
        }

        List<TemplateFileChange> changes = [];
        HashSet<string> seen = new(comparer);
        foreach (string path in effects.FileChanges.Select(change => change.TargetRelativePath).Concat(image.Files).Concat(reportedDirectories))
        {
            string? relative = image.GetRelative(image.GetTarget(path));
            if (relative is null || (!files.Contains(relative) && !reportedDirectories.Contains(relative)) || !seen.Add(relative))
            {
                continue;
            }

            string target = image.GetTarget(relative);
            bool file = files.Contains(relative);
            if (!file && _engine.FileSystem.DirectoryExists(target))
            {
                continue;
            }
            bool overwrite = file && _engine.FileSystem.FileExists(target);
            changes.Add(new(path, overwrite ? TemplateFileChangeKind.Overwrite : TemplateFileChangeKind.Create));
        }

        return changes.AsReadOnly();
    }

    private static bool IsDescendant(string parent, string path)
    {
        string relative = Path.GetRelativePath(parent, path);
        return relative != "." && relative != ".." && !Path.IsPathRooted(relative)
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private async Task<TemplateInvocationResult> InstallAsync(
        ITemplateInfo template, TemplateConfigurationSnapshot configuration, TemplateInvocationRequest request,
        TemplateOutputImage image, IReadOnlyList<TemplateFileChange> changes, CancellationToken cancellationToken)
    {
        List<TemplateFileChange> completed = [];
        try
        {
            Dictionary<string, TargetState> targets = new(Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (string file in image.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string target = image.GetTarget(file);
                if (image.GetPathDiagnostic(target) is { } diagnostic)
                {
                    return new(TemplateInvocationStatus.Failed, [], diagnostic);
                }
                TargetState state = await ReadTargetAsync(target, cancellationToken);
                if (state.Directory)
                {
                    return new(TemplateInvocationStatus.FileConflict, [], $"A directory occupies native output file '{file}'.");
                }
                if (state.File && !request.Force)
                {
                    return new(TemplateInvocationStatus.FileConflict, changes, "Template would change existing files. Explicit file-conflict force is required.");
                }
                targets.Add(file, state);
            }

            foreach (string directory in image.Directories.OrderBy(path => path == "." ? -1 : path.Count(character => character == Path.DirectorySeparatorChar))
                .ThenBy(path => path, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                TemplateInvocationResult? invalidSource = await ValidateSelectedConfigurationAsync(template, configuration, cancellationToken);
                if (invalidSource is not null)
                {
                    return CompletedResult(invalidSource.Status, completed, invalidSource.Diagnostic);
                }
                string target = image.GetTarget(directory);
                if (image.GetPathDiagnostic(target) is { } diagnostic)
                {
                    return CompletedResult(TemplateInvocationStatus.Failed, completed, diagnostic);
                }
                if (_engine.FileSystem.FileExists(target))
                {
                    return CompletedResult(TemplateInvocationStatus.FileConflict, completed, $"A file occupies native output directory '{directory}'.");
                }
                if (!_engine.FileSystem.DirectoryExists(target))
                {
                    _fileSystem.CreateDirectory(target);
                    completed.Add(new(directory, TemplateFileChangeKind.Create));
                }
            }

            foreach (string file in changes.Select(change => image.GetRelative(image.GetTarget(change.Path))!)
                .Where(targets.ContainsKey))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string target = image.GetTarget(file);
                // The incoming file shares the destination volume, so the final move does not copy a partially written file into place.
                using TempDirectory incoming = _fileSystem.CreateTempDirectory(Path.GetDirectoryName(target)!);
                string incomingPath = Path.Combine(incoming.Path, "output");
                using (Stream content = image.OpenCaptured(file))
                {
                    await _fileSystem.SaveStreamToFileAsync(incomingPath, content, cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();
                TemplateInvocationResult? invalidSource = await ValidateSelectedConfigurationAsync(template, configuration, cancellationToken);
                if (invalidSource is not null)
                {
                    return CompletedResult(invalidSource.Status, completed, invalidSource.Diagnostic);
                }
                if (image.GetPathDiagnostic(target) is { } diagnostic)
                {
                    return CompletedResult(TemplateInvocationStatus.Failed, completed, diagnostic);
                }
                TargetState prior = targets[file];
                if (await ReadTargetAsync(target, cancellationToken) != prior)
                {
                    return CompletedResult(TemplateInvocationStatus.FileConflict, completed, $"Native output target '{file}' changed before installation. Rerun creation.");
                }

                _fileSystem.MoveFile(incomingPath, target, overwrite: prior.File && request.Force);
                completed.Add(new(file, prior.File ? TemplateFileChangeKind.Overwrite : TemplateFileChangeKind.Create));
            }

            if (image.Files.Any(file => !_engine.FileSystem.FileExists(image.GetTarget(file))
                    || _engine.FileSystem.DirectoryExists(image.GetTarget(file))
                    || image.GetPathDiagnostic(image.GetTarget(file)) is not null)
                || image.Directories.Any(directory => !_engine.FileSystem.DirectoryExists(image.GetTarget(directory))
                    || _engine.FileSystem.FileExists(image.GetTarget(directory))
                    || image.GetPathDiagnostic(image.GetTarget(directory)) is not null))
            {
                return CompletedResult(TemplateInvocationStatus.EngineFailure, completed, "Template installation did not retain the captured output paths and kinds.");
            }

            // These bytes are now real output. Cancellation arriving only after the last install must not erase its completed effects.
            return new(TemplateInvocationStatus.Success, changes) { HasCommittedOutput = completed.Count > 0 };
        }
        catch (OperationCanceledException) when (completed.Count > 0)
        {
            return CompletedResult(TemplateInvocationStatus.Failed, completed, "Template creation was canceled after the reported output entries were installed.");
        }
        catch (IOException exception)
        {
            return CompletedResult(TemplateInvocationStatus.EngineFailure, completed, $"Template output installation failed: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            return CompletedResult(TemplateInvocationStatus.Failed, completed, $"Template output installation was denied: {exception.Message}");
        }
    }

    private static TemplateInvocationResult CompletedResult(
        TemplateInvocationStatus status, IReadOnlyList<TemplateFileChange> completed, string? diagnostic)
        => new(status, Array.AsReadOnly<TemplateFileChange>([.. completed]), diagnostic) { HasCommittedOutput = completed.Count > 0 };

    private async Task<TargetState> ReadTargetAsync(string path, CancellationToken cancellationToken)
    {
        bool file = _engine.FileSystem.FileExists(path);
        bool directory = _engine.FileSystem.DirectoryExists(path);
        string? hash = file ? await _fileSystem.ComputeSha256Async(path, cancellationToken) : null;
        return new(file, directory, hash);
    }

    private sealed record TargetState(bool File, bool Directory, string? Hash);

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

}
