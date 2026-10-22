// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.ObjectModel;
using System.Security.Cryptography;
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
internal sealed class TemplateInvocationCoordinator(
    ITemplateInvocationEngine engine, IFileSystem fileSystem, TemplateProjectSupport? projectSupport = null)
{
    private readonly ITemplateInvocationEngine _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly TemplateProjectSupport? _projectSupport = projectSupport;
    private readonly Dictionary<string, DotNetProjectLayoutQualification> _projectLayouts = new(StringComparer.Ordinal);

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

    internal async Task QualifyProjectConfigurationAsync(ITemplateInfo template, TemplateCatalogEntry entry,
        TemplateConfigurationSnapshot configuration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(configuration);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (_projectSupport?.QualifyLayoutAsync is not { } qualify)
        {
            return;
        }

        NativeProjectTemplateSource source = new(entry, configuration, $"{template.MountPointUri}::{template.ConfigPlace}",
            (path, token) => _engine.ReadPackageFileAsync(template, path, token));
        DotNetProjectLayoutQualification? layout = await qualify(source, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (layout is not null)
        {
            if (layout.TemplateIdentity != entry.Identity
                || !string.Equals(layout.ConfigurationFingerprint, configuration.Fingerprint, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidTemplateMetadataException("Project layout qualification does not match the exact mounted candidate.");
            }

            _projectLayouts[configuration.Content] = layout;
        }
    }

    private TemplateProjectSupport? ProjectSupport(string configuration) => _projectSupport is { } support
        && _projectLayouts.TryGetValue(configuration, out DotNetProjectLayoutQualification? layout)
        ? support with { DotNetLayout = layout } : _projectSupport;

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
        TemplateInvocationResult? configurationFailure = await ValidateInvocationSourceAsync(template, configuration, request.SourceSnapshot, cancellationToken);
        if (configurationFailure is not null)
        {
            return configurationFailure;
        }

        string? bindingFailure = _engine.QualifyMsBuildBindings(template, configuration, request);
        if (bindingFailure is not null)
        {
            return new(TemplateInvocationStatus.InvalidArguments, [], bindingFailure);
        }

        if (request.EffectSupport?.FSharpRepair is not null
            && (configuration.Type != TemplateType.Item
                || !template.TagsCollection.TryGetValue("language", out string? language) || language != "F#"
                || !string.Equals(_engine.Context.Project?.Stack, "dotnet", StringComparison.OrdinalIgnoreCase)
                || _engine.Context.Project?.Language != "F#"
                || request.MsBuildBindings?.TargetFramework != "net10.0"
                || Path.GetExtension(request.EffectSupport.ExactProjectPath) != ".fsproj"))
        {
            return new(TemplateInvocationStatus.InvalidArguments, [],
                "F# compile repair requires the selected F# item, an exact evaluated net10.0 .fsproj snapshot and its controlled dependency capability.");
        }

        if ((request.EffectSupport is not null || request.SourceSnapshot is not null)
            && TemplateOutputPathValidator.GetDiagnostic(configuration.Content,
                _engine.GetPopulatedParameters(template, configuration, request), _engine.Context) is { } topologyFailure)
        {
            return new(TemplateInvocationStatus.InvalidTemplate, [], topologyFailure);
        }

        TemplateProjectSupport? project = configuration.Type == TemplateType.Project ? ProjectSupport(configuration.Content) : null;
        if (request.PreserveExistingFiles && project is null)
        {
            return new(TemplateInvocationStatus.InvalidArguments, [], "Existing-file preservation is only supported for project initialization.");
        }
        if (request.PreviewAfterDirectoryCleanup && (!request.DryRun || !request.Force || project is null))
        {
            return new(TemplateInvocationStatus.InvalidArguments, [],
                "Directory-cleanup intent is only supported for a forced project preview, never actual invocation.");
        }
        bool qualifiedDotNetProject = project?.DotNetLayout is not null;
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

            if (qualifiedDotNetProject && request.EffectSupport is null)
            {
                return new(TemplateInvocationStatus.EffectFailure, [], "Qualified .NET project creation requires an explicit controlled dependency capability.");
            }
        }
        else if ((configuration.Type != TemplateType.Item || request.EffectSupport is null)
            && configuration.Root.EnumerateObject().Any(property => string.Equals(property.Name, "postActions", StringComparison.OrdinalIgnoreCase)
            && property.Value.ValueKind != JsonValueKind.Null && (property.Value.ValueKind != JsonValueKind.Array || property.Value.GetArrayLength() > 0)))
        {
            return new(TemplateInvocationStatus.Failed, [], "Item post-actions require a qualified dispatcher and are not supported by this invocation path.");
        }

        string output = request.OutputDirectory.Info.FullName;
        using TemplateOutputImage image = new(_engine.FileSystem, output, cancellationToken, request.PreviewAfterDirectoryCleanup);
        ITemplateCreationResult native;
        try
        {
            native = await _engine.RenderImageAsync(template, request with { Force = request.Force || request.PreserveExistingFiles },
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
        if (project is null && request.EffectSupport is null && native.CreationResult.PostActions.Count != 0)
        {
            return new(TemplateInvocationStatus.Failed, [], "Native creation produced unsupported item post-actions.");
        }

        if (request.SourceSnapshot is { } sourceSnapshot
            && (GetDirectoryOutputs(configuration, native.CreationEffects).Count > 0
                || !MatchesOutputSources(sourceSnapshot, native.CreationEffects.FileChanges)
                || image.Files.Count != native.CreationEffects.FileChanges.Count
                || image.Files.Any(path => !native.CreationEffects.FileChanges.Any(change =>
                    string.Equals(image.GetRelative(image.GetTarget(change.TargetRelativePath)), path,
                        Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))))
        {
            return new(TemplateInvocationStatus.InvalidTemplate, [], "Owned source qualification requires exactly the selected regular-file sources, not placeholder directories or extra outputs.");
        }

        IReadOnlyList<TemplateFileChange> nativeChanges = DescribeActualOutputs(image, configuration, native.CreationEffects);
        if (request.PreserveExistingFiles)
        {
            nativeChanges = Array.AsReadOnly<TemplateFileChange>([.. nativeChanges.Select(change =>
                _engine.FileSystem.FileExists(image.GetTarget(change.Path)) ? change with { Kind = TemplateFileChangeKind.Preserve } : change)]);
        }
        IReadOnlyList<TemplateFileChange> changes = nativeChanges;
        NativeTemplateEffectResult? effectPreview = null;
        IReadOnlyList<TemplateProjectConfigurationPlan> configurationPlans = [];
        if (project is not null)
        {
            try
            {
                HashSet<string> directories = new(image.Directories.Select(image.GetTarget),
                    Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
                IReadOnlyList<TemplateFileChange> actualOutputs =
                    DescribeActualOutputs(image, configuration, native.CreationEffects, includeExistingDirectories: true);
                configurationPlans = project.DotNetLayout is { } layout
                    ? project.Planner.CreateDotNetPlans(configuration.Content, native.CreationEffects, output, layout, request.Parameters,
                        actualOutputs, directories)
                    : project.Planner.CreatePlans(configuration.Content, native.CreationResult, output, actualOutputs, directories);
                if (!request.PreviewAfterDirectoryCleanup && configurationPlans.Any(project.Writer.ConfigurationExists))
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

        if ((configuration.Type == TemplateType.Item || qualifiedDotNetProject) && request.EffectSupport is { } effectSupport)
        {
            ArgumentNullException.ThrowIfNull(effectSupport.ProjectDirectory);
            ArgumentNullException.ThrowIfNull(effectSupport.Capability);
            if (qualifiedDotNetProject)
            {
                StringComparer comparer = Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
                if (!Path.IsPathFullyQualified(effectSupport.ExactProjectPath)
                    || !comparer.Equals(Path.GetFullPath(effectSupport.ExactProjectPath), configurationPlans.Single().PrimaryOutput)
                    || !comparer.Equals(Path.TrimEndingDirectorySeparator(effectSupport.ProjectDirectory.Info.FullName),
                        Path.TrimEndingDirectorySeparator(output)))
                {
                    return new(TemplateInvocationStatus.EffectFailure, [],
                        "Qualified .NET dependency effects must target the native-generated project in the exact invocation root.")
                    {
                        ProjectConfigurations = configurationPlans,
                    };
                }
            }

            TemplateEffectSource selectedSource = new(template.Identity, $"{template.MountPointUri}::{template.ConfigPlace}", configuration.Fingerprint);
            NativeTemplateEffectPlanner planner = new();
            NativeTemplateEffectPlanningResult planned = effectSupport.FSharpRepair is { } repair
                ? planner.PlanFSharpItemDependencies(effectSupport.ProjectDirectory, selectedSource, native.CreationEffects,
                    request.OutputDirectory, effectSupport.ExactProjectPath, repair)
                : planner.PlanDependencies(effectSupport.ProjectDirectory, selectedSource, native.CreationResult,
                    qualifiedDotNetProject ? configurationPlans.Single().PrimaryOutput : effectSupport.ExactProjectPath);
            if (planned.Plan is null)
            {
                return new(TemplateInvocationStatus.EffectFailure, [], string.Join(" ", planned.Diagnostics));
            }

            effectPreview = await new NativeTemplateEffectWorkflow().ExecuteAsync(planned.Plan,
                effectSupport.Capability, dryRun: true, confirmed: false, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!effectPreview.Succeeded)
            {
                return new(TemplateInvocationStatus.EffectFailure, [], string.Join(" ", effectPreview.Diagnostics)) { Effects = effectPreview };
            }

            if (!request.DryRun && !effectSupport.Confirmed)
            {
                return new(TemplateInvocationStatus.EffectFailure, [], "The controlled effect plan was not confirmed; no native files or effects were applied.")
                {
                    Effects = effectPreview with { Kind = "declined", Succeeded = false },
                };
            }
        }

        string? outputFailure = GetOutputDiagnostic(image, configurationPlans);
        if (outputFailure is not null)
        {
            return new(TemplateInvocationStatus.Failed, [], outputFailure) { ProjectConfigurations = configurationPlans };
        }
        configurationFailure = await ValidateInvocationSourceAsync(template, configuration, request.SourceSnapshot, cancellationToken);
        if (configurationFailure is not null)
        {
            return configurationFailure with { ProjectConfigurations = configurationPlans };
        }

        bool requiresForce = !request.Force && changes.Any(change => change.Kind != TemplateFileChangeKind.Create
            && !(request.PreserveExistingFiles && change.Kind == TemplateFileChangeKind.Preserve));
        if (request.DryRun)
        {
            return new(TemplateInvocationStatus.Success, changes, requiresForce ? "Actual creation requires explicit file-conflict force." : null)
            {
                ProjectConfigurations = configurationPlans,
                Effects = effectPreview,
            };
        }
        if (requiresForce)
        {
            return new(TemplateInvocationStatus.FileConflict, changes, "Template would change existing files. Explicit file-conflict force is required.")
            {
                ProjectConfigurations = configurationPlans,
                Effects = effectPreview,
            };
        }

        TemplateInvocationResult installed = await InstallAsync(template, configuration, request, image, nativeChanges, cancellationToken);
        installed = installed with { ProjectConfigurations = configurationPlans, Effects = effectPreview };
        if (installed.Status != TemplateInvocationStatus.Success)
        {
            return installed;
        }

        if (project is not null)
        {
            installed = await ConfigureAsync(template, configuration, project, image, configurationPlans, installed, cancellationToken);
            installed = installed with { Effects = effectPreview };
            if (installed.Status != TemplateInvocationStatus.Success)
            {
                return installed;
            }
        }

        if (effectPreview is not null && request.EffectSupport is { } effects)
        {
            string? effectDiagnostic = GetOutputDiagnostic(image, configurationPlans);
            HashSet<string> directoryOutputs = new(image.Directories,
                Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            if (effectDiagnostic is not null || nativeChanges.Any(change => !HasExpectedOutputKind(output, change, directoryOutputs)))
            {
                effectDiagnostic ??= "Native outputs changed before dependency execution.";
                return installed with
                {
                    Status = TemplateInvocationStatus.EffectFailure,
                    Diagnostic = effectDiagnostic,
                    Effects = effectPreview with { Kind = "preflightFailed", Succeeded = false, Diagnostics = [effectDiagnostic] },
                };
            }

            NativeTemplateEffectResult applied = await new NativeTemplateEffectWorkflow().ExecuteAsync(
                effectPreview.Plan, effects.Capability, dryRun: false, confirmed: effects.Confirmed, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return installed with
            {
                Status = applied.Succeeded ? TemplateInvocationStatus.Success : TemplateInvocationStatus.EffectFailure,
                Diagnostic = applied.Succeeded ? null : qualifiedDotNetProject
                    ? "Native project files and CLI configuration were generated, but controlled dependencies did not complete. "
                        + string.Join(" ", applied.Diagnostics)
                    : "Native template files were generated, but controlled effects did not complete. "
                        + string.Join(" ", applied.Diagnostics),
                Effects = applied,
            };
        }

        return installed;
    }

    internal async Task<NativeTemplatePreparationResult> CaptureNativePreparationAsync(ITemplateInfo template,
        TemplateConfigurationSnapshot configuration, TemplateInvocationRequest request, IFileSystem fileSystem,
        bool expandFiles, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        TemplateInvocationResult? invalid = await ValidateInvocationSourceAsync(template, configuration, request.SourceSnapshot, cancellationToken);
        if (invalid is not null)
        {
            return new(null, invalid);
        }

        string? diagnostic = _engine.QualifyMsBuildBindings(template, configuration, request)
            ?? TemplateOutputPathValidator.GetDiagnostic(configuration.Content,
                _engine.GetPopulatedParameters(template, configuration, request), _engine.Context);
        if (diagnostic is not null)
        {
            return Failed(diagnostic);
        }

        ITemplateCreationResult preview = await _engine.InstantiateAsync(template, request,
            CreateInputData(configuration.Parameters, request.Parameters), true, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (preview.Status != CreationResultStatus.Success || preview.CreationEffects is null)
        {
            return Failed(preview.ErrorMessage ?? "Native preparation did not report evaluated creation effects.");
        }

        ICreationEffects effects = preview.CreationEffects;
        using var sourceConfiguration = new MemoryStream(Encoding.UTF8.GetBytes(configuration.Content));
        string? sourcePlaceholder = TemplateConfigModel.FromStream(sourceConfiguration).PlaceholderFilename;
        IReadOnlyList<string> sourcePlaceholders = string.IsNullOrWhiteSpace(sourcePlaceholder) ? ["-.-", "_._"] : [sourcePlaceholder];
        if (request.SourceSnapshot is { } snapshot && (!MatchesOutputSources(snapshot, effects.FileChanges)
            || effects.FileChanges.OfType<IFileChange2>().Any(change => sourcePlaceholders.Contains(Path.GetFileName(change.SourceRelativePath), StringComparer.Ordinal))))
        {
            return Failed("Native preparation does not match the qualified regular-file sources.");
        }

        TemplateFileChange[] changes =
            [.. effects.FileChanges.Select(change => TemplateFileChange.FromNative(change.TargetRelativePath, change.ChangeKind))];
        bool cleanupPreview = request.PreviewAfterDirectoryCleanup && request.DryRun && request.Force && ProjectTemplatesEnabled
            && configuration.Type == TemplateType.Project;
        diagnostic = ValidateOutputPaths(request.OutputDirectory.Info.FullName, changes, cleanupPreview);
        if (diagnostic is not null)
        {
            return Failed(diagnostic);
        }

        Dictionary<string, ReadOnlyMemory<byte>> expanded = new(StringComparer.Ordinal);
        if (expandFiles)
        {
            if (string.IsNullOrWhiteSpace(request.Name) || effects.FileChanges.Any(change => change.ChangeKind != ChangeKind.Create
                && !(cleanupPreview && change.ChangeKind is ChangeKind.Change or ChangeKind.Overwrite)))
            {
                return Failed("Private native expansion requires an explicit effective name and create-only native outputs.");
            }

            using TempDirectory temporary = fileSystem.CreateTempDirectory();
            var output = WorkingDirectory.FromExplicit(temporary.Path);
            TemplateInvocationRequest expansionRequest = request with
            {
                OutputDirectory = output,
                EffectSupport = null,
                MsBuildBindings = null,
                Force = false,
                PreserveExistingFiles = false,
                PreviewAfterDirectoryCleanup = false,
            };
            diagnostic = ValidateOutputPaths(output.Info.FullName, changes);
            if (diagnostic is not null)
            {
                return Failed(diagnostic);
            }

            // Native instantiation reports post-actions but does not dispatch them.
            invalid = await ValidateInvocationSourceAsync(template, configuration, request.SourceSnapshot, cancellationToken);
            if (invalid is not null)
            {
                return new(null, invalid);
            }

            ITemplateCreationResult created = await _engine.InstantiateAsync(template, expansionRequest,
                CreateInputData(configuration.Parameters, expansionRequest.Parameters), false, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            HashSet<string> directories = new(effects.FileChanges.OfType<IFileChange2>()
                .Where(change => sourcePlaceholders.Contains(Path.GetFileName(change.SourceRelativePath), StringComparer.Ordinal))
                .Select(change => change.TargetRelativePath), StringComparer.Ordinal);
            if (created.Status != CreationResultStatus.Success || created.CreationEffects is null
                || !created.CreationEffects.FileChanges.Select(change => change.TargetRelativePath).Order(StringComparer.Ordinal)
                    .SequenceEqual(effects.FileChanges.Select(change => change.TargetRelativePath).Order(StringComparer.Ordinal), StringComparer.Ordinal)
                || created.CreationEffects.FileChanges.Any(change => change.ChangeKind != ChangeKind.Create)
                || (request.SourceSnapshot is { } expandedSources && !MatchesOutputSources(expandedSources, created.CreationEffects.FileChanges))
                || ValidateOutputPaths(output.Info.FullName, changes) is not null
                || changes.Any(change => !HasExpectedOutputKind(output.Info.FullName, change, directories)))
            {
                return Failed("Private native expansion did not produce the exact previewed source output paths.");
            }

            foreach (IFileChange change in created.CreationEffects.FileChanges)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string path = Path.GetFullPath(Path.Combine(output.Info.FullName, change.TargetRelativePath));
                if (fileSystem.FileExists(path))
                {
                    using Stream stream = _engine.FileSystem.OpenRead(path);
                    using MemoryStream content = new();
                    await stream.CopyToAsync(content, cancellationToken);
                    expanded.Add(change.TargetRelativePath, content.ToArray());
                }
                else if (!fileSystem.DirectoryExists(path))
                {
                    return Failed("Private native expansion contains a missing or unsupported output kind.");
                }
            }

            StringComparer comparer = Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            string[] expectedFiles = [.. expanded.Keys.Select(path => Path.GetFullPath(Path.Combine(output.Info.FullName, path))).Order(comparer)];
            if (!fileSystem.GetFiles(output.Info.FullName).Select(Path.GetFullPath).Order(comparer).SequenceEqual(expectedFiles, comparer))
            {
                return Failed("Private native expansion produced unexpected source files.");
            }

            HashSet<string> allowed = new(comparer);
            foreach (TemplateFileChange change in changes)
            {
                for (string? path = Path.GetFullPath(Path.Combine(output.Info.FullName, change.Path));
                    path is not null && !comparer.Equals(path, output.Info.FullName); path = Path.GetDirectoryName(path))
                {
                    allowed.Add(path);
                }
            }

            Queue<string> inspect = new([output.Info.FullName]);
            while (inspect.TryDequeue(out string? directory))
            {
                foreach (string entry in fileSystem.GetFileSystemEntries(directory))
                {
                    if (!allowed.Contains(Path.GetFullPath(entry)))
                    {
                        return Failed("Private native expansion produced unexpected source entries.");
                    }

                    if (fileSystem.DirectoryExists(entry))
                    {
                        inspect.Enqueue(entry);
                    }
                }
            }
        }

        invalid = await ValidateSelectedConfigurationAsync(template, configuration, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return invalid is null ? new(new(new(template.Identity, $"{template.MountPointUri}::{template.ConfigPlace}", configuration.Fingerprint),
            effects, new ReadOnlyDictionary<string, ReadOnlyMemory<byte>>(expanded)), null) : new(null, invalid);

        static NativeTemplatePreparationResult Failed(string message) =>
            new(null, new(TemplateInvocationStatus.InvalidTemplate, [], message));
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
                if (image.ReadFileSystem.FileExists(parent))
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
        TemplateInvocationResult installed, CancellationToken cancellationToken)
    {
        List<TemplateFileChange> completed = [.. installed.Changes];
        List<string> configured = [];
        try
        {
            foreach (TemplateProjectConfigurationPlan plan in plans)
            {
                cancellationToken.ThrowIfCancellationRequested();
                TemplateInvocationResult? changed = await ValidateSelectedConfigurationAsync(template, configuration, cancellationToken);
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
            if (!file && !includeExistingDirectories && image.ReadFileSystem.DirectoryExists(target))
            {
                continue;
            }
            bool overwrite = file && image.ReadFileSystem.FileExists(target);
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
            HashSet<string> preserved = new(changes.Where(change => change.Kind == TemplateFileChangeKind.Preserve)
                .Select(change => image.GetRelative(image.GetTarget(change.Path))!),
                Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
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
                if (preserved.Contains(file))
                {
                    if (!state.File)
                    {
                        return new(TemplateInvocationStatus.FileConflict, [], $"Preserved native output file '{file}' changed before installation. Rerun initialization.");
                    }
                    continue;
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
                TemplateInvocationResult? invalidSource = await ValidateInvocationSourceAsync(template, configuration, request.SourceSnapshot, cancellationToken);
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
                TemplateInvocationResult? invalidSource = await ValidateInvocationSourceAsync(template, configuration, request.SourceSnapshot, cancellationToken);
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

    private async Task<TemplateInvocationResult?> ValidateInvocationSourceAsync(ITemplateInfo template,
        TemplateConfigurationSnapshot configuration, NativeTemplateSourceSnapshot? snapshot, CancellationToken cancellationToken)
    {
        TemplateInvocationResult? invalid = await ValidateSelectedConfigurationAsync(template, configuration, cancellationToken);
        if (invalid is not null || snapshot is null)
        {
            return invalid;
        }

        if (configuration.Type != TemplateType.Item || snapshot.TemplateIdentity != template.Identity
            || !string.Equals(snapshot.ConfigurationSha256, configuration.Fingerprint, StringComparison.OrdinalIgnoreCase))
        {
            return new(TemplateInvocationStatus.InvalidTemplate, [], "Source qualification is not bound to the exact selected item configuration.");
        }

        foreach (TemplateEffectSource pin in snapshot.Sources)
        {
            ReadOnlyMemory<byte> source;
            try
            {
                source = await _engine.ReadPackageFileAsync(template, pin.Identity, cancellationToken);
            }
            catch (FileNotFoundException exception)
            {
                return new(TemplateInvocationStatus.TemplateChanged, [], exception.Message);
            }

            if (!string.Equals(Convert.ToHexString(SHA256.HashData(source.Span)), pin.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return new(TemplateInvocationStatus.TemplateChanged, [], "A selected package source changed. Rerun selection.");
            }
        }

        return null;
    }

    private static bool MatchesOutputSources(NativeTemplateSourceSnapshot snapshot, IEnumerable<IFileChange> changes)
    {
        IFileChange[] files = [.. changes];
        return files.Length == snapshot.Outputs.Count
            && files.All(change => change.ChangeKind is ChangeKind.Create or ChangeKind.Change or ChangeKind.Overwrite
                && change is IFileChange2 source && snapshot.Outputs.Count(expected =>
                NativeTemplateSourceSnapshot.Normalize(expected.SourceRelativePath) == NativeTemplateSourceSnapshot.Normalize(source.SourceRelativePath)) == 1)
            && files.OfType<IFileChange2>().Select(file => NativeTemplateSourceSnapshot.Normalize(file.SourceRelativePath)).Distinct(StringComparer.Ordinal).Count() == files.Length
            && files.Select(file => file.TargetRelativePath).Distinct(StringComparer.Ordinal).Count() == files.Length;
    }

    private bool HasExpectedOutputKind(string output, TemplateFileChange change, HashSet<string> directoryOutputs)
    {
        string path = Path.GetFullPath(Path.Combine(output, change.Path));
        bool file = _engine.FileSystem.FileExists(path);
        bool directory = _engine.FileSystem.DirectoryExists(path);
        if (change.Kind == TemplateFileChangeKind.Delete)
        {
            return !file && !directory;
        }

        return directoryOutputs.Contains(change.Path) || directoryOutputs.Contains(Path.GetRelativePath(output, path))
            ? directory && !file : file && !directory;
    }

    private string? ValidateOutputPaths(string output, IReadOnlyList<TemplateFileChange> changes, bool cleanupPreview = false)
    {
        PlannedCleanupTemplateFileSystem? planned = cleanupPreview ? new(_engine.FileSystem, output) : null;
        foreach (TemplateFileChange change in changes)
        {
            string? path = Path.GetFullPath(Path.Combine(output, change.Path));
            string target = path;
            string relative = Path.GetRelativePath(output, path);
            if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                return "Template output escapes the requested output directory.";
            }
            if (planned?.IsPreservedPath(path) == true)
            {
                return "A force preview cannot write preserved .git content.";
            }

            // The requested output directory is the boundary; linked parents do not redirect individual template paths.
            while (path is not null && !string.Equals(Path.TrimEndingDirectorySeparator(path), Path.TrimEndingDirectorySeparator(output), StringComparison.Ordinal))
            {
                try
                {
                    FileAttributes attributes = _engine.FileSystem.GetFileAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        return "Template output uses a symbolic link or reparse point. Choose an unlinked output directory.";
                    }

                    if (!string.Equals(path, target, StringComparison.Ordinal) && (attributes & FileAttributes.Directory) == 0
                        && planned?.IsScheduledForDeletion(path) != true)
                    {
                        return "A template output directory ancestor is an existing file. Choose an output directory without file collisions.";
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

    internal async Task<TemplateInvocationResult?> ValidateSelectedConfigurationAsync(
        ITemplateInfo template, TemplateConfigurationSnapshot configuration, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        TemplateConfigurationSnapshot current;
        try
        {
            if (await _engine.ValidatePinnedPackageAsync(template, cancellationToken) is { } packageFailure)
            {
                return new(TemplateInvocationStatus.TemplateChanged, [], packageFailure);
            }
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
