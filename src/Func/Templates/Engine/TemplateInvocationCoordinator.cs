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
internal sealed partial class TemplateInvocationCoordinator(
    ITemplateInvocationEngine engine, IFileSystem fileSystem, TemplateProjectSupport? projectSupport = null)
{
    private readonly ITemplateInvocationEngine _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly TemplateProjectSupport? _projectSupport = projectSupport;

    public bool ProjectTemplatesEnabled => _projectSupport is not null;

    public IReadOnlyList<TemplateProjectConfigurationDeclaration> ValidateProjectConfiguration(string configuration)
    {
        if (_projectSupport is null)
        {
            throw new InvalidOperationException("Project templates are not enabled for this invocation flow.");
        }

        return ProjectSupport(configuration)?.DotNetLayout is { } layout
            ? _projectSupport.Planner.ReadDotNetLayout(configuration, layout)
            : _projectSupport.Planner.Read(configuration);
    }

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
        TemplateInvocationResult? configurationFailure = await ValidateGuardedSourcesAsync(template, configuration, request, cancellationToken);
        if (configurationFailure is not null)
        {
            return configurationFailure;
        }

        string? bindingFailure = QualifyBindings(template, configuration, request) ?? GetFSharpContextDiagnostic(template, configuration, request);
        if (bindingFailure is not null)
        {
            return new(TemplateInvocationStatus.InvalidArguments, [], bindingFailure);
        }

        TemplateProjectSupport? project = configuration.Type == TemplateType.Project ? ProjectSupport(configuration.Content) : null;
        if (project is not null)
        {
            try
            {
                ValidateProjectConfiguration(configuration.Content);
            }
            catch (InvalidTemplateMetadataException exception)
            {
                return new(TemplateInvocationStatus.InvalidTemplate, [], exception.Message);
            }
            if (project.DotNetLayout is not null && request.EffectSupport is null)
            {
                return new(TemplateInvocationStatus.EffectFailure, [], "Qualified .NET project creation requires an explicit controlled dependency capability.");
            }
        }
        else if (request.EffectSupport is null && configuration.Root.EnumerateObject().Any(property => string.Equals(property.Name, "postActions", StringComparison.OrdinalIgnoreCase)
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
        NativeTemplatePreparationSnapshot metadata = new(new(template.Identity, $"{template.MountPointUri}::{template.ConfigPlace}", configuration.Fingerprint),
            native.CreationEffects, new Dictionary<string, ReadOnlyMemory<byte>>(), native.CreationResult);
        ICreationEffects effects = metadata.Effects;
        if (request.SourceSnapshot is not null || request.EffectSupport is not null || request.ExpectedPreparation is not null || request.RequireCapturedSources)
        {
            configurationFailure = await ValidateGuardedSourcesAsync(template, configuration, request, cancellationToken);
            if (configurationFailure is not null)
            {
                return configurationFailure;
            }
        }
        foreach (IFileChange change in effects.FileChanges)
        {
            _ = TemplateFileChange.FromNative(change.TargetRelativePath, change.ChangeKind);
        }
        if (project is null && request.EffectSupport is null && effects.CreationResult.PostActions.Count != 0)
        {
            return new(TemplateInvocationStatus.Failed, [], "Native creation produced unsupported item post-actions.");
        }

        IReadOnlyList<TemplateFileChange> nativeChanges = DescribeActualOutputs(image, configuration, effects);
        string? captureFailure = ValidateCapturedAuthority(template, configuration, request, image, effects);
        if (captureFailure is not null)
        {
            return new(TemplateInvocationStatus.InvalidTemplate, [], captureFailure);
        }
        bool guarded = request.SourceSnapshot is not null || request.EffectSupport is not null
            || request.ExpectedPreparation is not null || request.RequireCapturedSources;
        NativeTemplatePreparationSnapshot capture;
        try
        {
            capture = await CreateSnapshotAsync(template, configuration, image, effects, effects.CreationResult, guarded, cancellationToken);
        }
        catch (FileNotFoundException exception)
        {
            return new(TemplateInvocationStatus.InvalidTemplate, [], exception.Message);
        }
        catch (InvalidTemplateMetadataException exception)
        {
            return new(TemplateInvocationStatus.InvalidTemplate, [], exception.Message);
        }
        if (request.SourceSnapshot is { } qualified && qualified.Outputs.Any(expected =>
            !capture.OutputSources.Any(actual => actual.SourceRelativePath == NativeTemplateSourceSnapshot.Normalize(expected.SourceRelativePath)
                && SameSourcePin(actual.Source, expected.Source))))
        {
            return new(TemplateInvocationStatus.InvalidTemplate, [], "A native source mapping is not bound to its exact selected package source pin.");
        }
        request = request with { CapturedSources = capture.Sources };
        if (request.ExpectedPreparation is { } expected && !SameCapture(expected, capture))
        {
            return new(TemplateInvocationStatus.TemplateChanged, [], "The actual native capture differs from the qualified preparation plan. Rerun preparation.");
        }
        IReadOnlyList<TemplateFileChange> changes = nativeChanges;
        IReadOnlyList<TemplateProjectConfigurationPlan> configurationPlans = [];
        if (project is not null)
        {
            try
            {
                HashSet<string> directories = new(image.Directories.Select(image.GetTarget),
                    Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
                IReadOnlyList<TemplateFileChange> actual = DescribeActualOutputs(image, configuration, effects, includeExistingDirectories: true);
                configurationPlans = project.DotNetLayout is { } layout
                    ? project.Planner.CreateDotNetPlans(configuration.Content, effects.CreationResult, output, actual, directories,
                        layout, request.Parameters, effects)
                    : project.Planner.CreatePlans(configuration.Content, effects.CreationResult, output, actual, directories);
                if (configurationPlans.Any(project.Writer.ConfigurationExists))
                {
                    return new(TemplateInvocationStatus.FileConflict, changes, TemplateProjectConfigurationWriter.ExistingConfigurationDiagnostic)
                    {
                        ProjectConfigurations = configurationPlans,
                    };
                }

                changes = Array.AsReadOnly<TemplateFileChange>([.. nativeChanges,
                    .. configurationPlans.Select(plan => new TemplateFileChange(Path.GetRelativePath(output, plan.ConfigurationPath), TemplateFileChangeKind.Create))]);
            }
            catch (InvalidTemplateMetadataException exception)
            {
                return new(TemplateInvocationStatus.InvalidTemplate, [], exception.Message)
                {
                    ProjectConfigurations = configurationPlans,
                };
            }
        }

        NativeTemplateEffectResult? effectPreview;
        try
        {
            effectPreview = await PlanEffectsAsync(template, configuration, request, image, capture.Effects, capture,
                configurationPlans, project?.DotNetLayout is not null, cancellationToken);
        }
        catch (InvalidTemplateMetadataException exception)
        {
            return new(TemplateInvocationStatus.EffectFailure, [], exception.Message) { Capture = capture, ProjectConfigurations = configurationPlans };
        }
        if (effectPreview is { Succeeded: false })
        {
            return new(TemplateInvocationStatus.EffectFailure, [], string.Join(" ", effectPreview.Diagnostics))
            {
                Effects = effectPreview,
                ProjectConfigurations = configurationPlans,
                Capture = capture,
            };
        }

        string? outputFailure = GetOutputDiagnostic(image, configurationPlans);
        if (outputFailure is not null)
        {
            return new(TemplateInvocationStatus.Failed, [], outputFailure) { ProjectConfigurations = configurationPlans };
        }
        configurationFailure = await ValidateGuardedSourcesAsync(template, configuration, request, cancellationToken);
        if (configurationFailure is not null)
        {
            return configurationFailure with { ProjectConfigurations = configurationPlans };
        }

        bool requiresForce = !request.Force && changes.Any(change => change.Kind != TemplateFileChangeKind.Create);
        if (request.DryRun)
        {
            return new(TemplateInvocationStatus.Success, changes, requiresForce ? "Actual creation requires explicit file-conflict force." : null)
            {
                ProjectConfigurations = configurationPlans,
                Effects = effectPreview,
                Capture = capture,
            };
        }
        if (requiresForce)
        {
            return new(TemplateInvocationStatus.FileConflict, changes, "Template would change existing files. Explicit file-conflict force is required.")
            {
                ProjectConfigurations = configurationPlans,
            };
        }

        TemplateInvocationResult installed = await InstallAsync(template, configuration, request, image, nativeChanges, cancellationToken);
        if (installed.Status != TemplateInvocationStatus.Success)
        {
            return installed with { ProjectConfigurations = configurationPlans, Effects = effectPreview, Capture = capture };
        }

        TemplateInvocationResult configured = project is null ? installed
            : await ConfigureAsync(template, configuration, project, image, configurationPlans, installed, request, cancellationToken);
        if (configured.Status != TemplateInvocationStatus.Success || effectPreview is null || request.EffectSupport is not { } support)
        {
            return configured with { Effects = effectPreview, Capture = capture };
        }
        if (effectPreview.Plan.Effects.Count == 0)
        {
            return configured with { Effects = effectPreview with { Kind = "applied" }, Capture = capture };
        }
        if (cancellationToken.IsCancellationRequested)
        {
            NativeTemplateEffectResult incomplete = effectPreview with
            {
                Kind = "incomplete",
                Succeeded = false,
                Diagnostics = ["Cancellation stopped controlled effects before any dependency started. Native output was retained."],
            };
            return configured with
            {
                Status = TemplateInvocationStatus.EffectFailure,
                Diagnostic = incomplete.Diagnostics[0],
                Effects = incomplete,
                Capture = capture,
            };
        }

        TemplateInvocationResult? changedSource = await ValidateGuardedSourcesAsync(template, configuration, request, cancellationToken);
        if (changedSource is not null)
        {
            return configured with { Status = TemplateInvocationStatus.EffectFailure, Diagnostic = changedSource.Diagnostic, Effects = effectPreview, Capture = capture };
        }
        NativeTemplateEffectResult applied = await new NativeTemplateEffectWorkflow().ExecuteAsync(
            effectPreview.Plan, support.Capability, dryRun: false, support.Confirmed, cancellationToken);
        return configured with
        {
            Status = applied.Succeeded ? TemplateInvocationStatus.Success : TemplateInvocationStatus.EffectFailure,
            Diagnostic = applied.Succeeded ? null : "Native files were generated and retained, but controlled effects did not complete. " + string.Join(" ", applied.Diagnostics),
            Effects = applied,
            Capture = capture,
        };
    }

    private string? GetOutputDiagnostic(TemplateOutputImage image, IReadOnlyList<TemplateProjectConfigurationPlan> plans)
    {
        foreach (string path in image.Files.Concat(image.Directories).Select(image.GetTarget).Concat(plans.Select(plan => plan.ConfigurationPath)))
        {
            if (image.GetPathDiagnostic(path) is { } diagnostic)
            {
                return diagnostic;
            }

            string? parent = Path.GetDirectoryName(path);
            while (parent is not null && image.GetRelative(parent) is { } relative && relative != ".")
            {
                if (_engine.FileSystem.FileExists(parent))
                {
                    return "A template output directory ancestor is an existing file. Choose an output directory without file collisions.";
                }

                parent = Path.GetDirectoryName(parent);
            }
        }

        return null;
    }

    private async Task<TemplateInvocationResult> ConfigureAsync(
        ITemplateInfo template, TemplateConfigurationSnapshot configuration, TemplateProjectSupport project,
        TemplateOutputImage image, IReadOnlyList<TemplateProjectConfigurationPlan> plans,
        TemplateInvocationResult installed, TemplateInvocationRequest request, CancellationToken cancellationToken)
    {
        List<TemplateFileChange> completed = [.. installed.Changes];
        List<string> configured = [];
        try
        {
            foreach (TemplateProjectConfigurationPlan plan in plans)
            {
                cancellationToken.ThrowIfCancellationRequested();
                TemplateInvocationResult? changed = await ValidateGuardedSourcesAsync(template, configuration, request, cancellationToken);
                if (changed is not null)
                {
                    return Result(changed.Status, changed.Diagnostic);
                }
                if (GetOutputDiagnostic(image, plans) is { } diagnostic)
                {
                    return Result(TemplateInvocationStatus.ConfigurationFailure, diagnostic);
                }

                await project.Writer.WriteAsync(plan, cancellationToken);
                configured.Add(plan.Root);
                completed.Add(new(Path.GetRelativePath(image.Output, plan.ConfigurationPath), TemplateFileChangeKind.Create));
            }

            return Result(TemplateInvocationStatus.Success, null);
        }
        catch (OperationCanceledException) when (installed.HasCommittedOutput || configured.Count > 0)
        {
            return Result(TemplateInvocationStatus.ConfigurationFailure,
                "Mandatory project configuration was canceled. Generated files were preserved.");
        }
        catch (IOException exception)
        {
            return Failure(exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            return Failure(exception);
        }
        catch (InvalidOperationException exception)
        {
            return Failure(exception);
        }

        TemplateInvocationResult Failure(Exception exception)
            => Result(TemplateInvocationStatus.ConfigurationFailure,
                $"Mandatory project configuration failed. Generated files were preserved: {exception.Message}");

        TemplateInvocationResult Result(TemplateInvocationStatus status, string? diagnostic) => new(status, completed.AsReadOnly(), diagnostic)
        {
            ProjectConfigurations = plans,
            ConfiguredProjects = configured.AsReadOnly(),
            HasCommittedOutput = installed.HasCommittedOutput || configured.Count > 0,
        };
    }

    private IReadOnlyList<TemplateFileChange> DescribeActualOutputs(
        TemplateOutputImage image, TemplateConfigurationSnapshot configuration, ICreationEffects effects, bool includeExistingDirectories = false)
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
            if (!file && !includeExistingDirectories && _engine.FileSystem.DirectoryExists(target))
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
                TemplateInvocationResult? invalidSource = await ValidateGuardedSourcesAsync(template, configuration, request, cancellationToken);
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
                TemplateInvocationResult? invalidSource = await ValidateGuardedSourcesAsync(template, configuration, request, cancellationToken);
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

    internal async Task<TemplateInvocationResult?> ValidateSelectedConfigurationAsync(
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

        bool qualified = current.Type == TemplateType.Item || (current.Type == TemplateType.Project && ProjectTemplatesEnabled);
        return qualified && current.Type == configuration.Type
            ? null
            : new(TemplateInvocationStatus.InvalidTemplate, [], ProjectTemplatesEnabled
                ? "Current mounted template type is not qualified for this invocation flow. Correct its type and rerun selection."
                : "Current mounted template type is not a qualified item template. Correct its type and rerun selection.");
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
