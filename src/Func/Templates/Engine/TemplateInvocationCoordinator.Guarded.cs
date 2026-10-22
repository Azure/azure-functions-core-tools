// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text.Json;
using Azure.Functions.Cli.Common;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;

namespace Azure.Functions.Cli.Templates.Engine;

internal sealed partial class TemplateInvocationCoordinator
{
    private readonly Dictionary<SelectedSourceKey, NativeProjectLayoutQualification> _projectLayouts = [];
    private NativeMsBuildBindingSnapshot? _bindings;

    internal async Task<TemplateInvocationResult> InvokeSelectedAsync(ITemplateInfo template, TemplateConfigurationSnapshot configuration,
        TemplateCatalogEntry entry, TemplateInvocationRequest request, CancellationToken cancellationToken)
    {
        if (_policies is not null && request.PolicyCandidate is null)
        {
            try
            {
                NativeTemplatePolicyCandidate? candidate = await _policies.GetCandidateAsync(entry, configuration,
                    $"{template.MountPointUri}::{template.ConfigPlace}", template.MountPointUri,
                    (path, token) => ReadPackageFileAsync(template, path, token),
                    token => ValidateSelectedConfigurationAsync(template, configuration, token), requireLayout: false, cancellationToken, _engine);
                request = request with { PolicyCandidate = candidate };
            }
            catch (InvalidTemplateMetadataException exception)
            {
                return new(TemplateInvocationStatus.InvalidTemplate, [], exception.Message);
            }
        }
        return await InvokeAsync(template, configuration, request, cancellationToken);
    }

    internal Task<ReadOnlyMemory<byte>> ReadPackageFileAsync(ITemplateInfo template, string path, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return _engine.ReadPackageFileAsync(template, path, cancellationToken);
    }

    internal async Task QualifyProjectConfigurationAsync(ITemplateInfo template, TemplateCatalogEntry entry,
        TemplateConfigurationSnapshot configuration, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_projectSupport is null)
        {
            return;
        }
        NativeProjectTemplateSource source = new(entry, configuration, $"{template.MountPointUri}::{template.ConfigPlace}",
            (path, token) => ReadPackageFileAsync(template, path, token))
        {
            PackageMountPoint = template.MountPointUri,
            RevalidateAsync = token => ValidateSelectedConfigurationAsync(template, configuration, token),
            SessionIdentity = _engine,
        };
        bool requiresLayout = false;
        try
        {
            _projectSupport.Planner.Read(configuration.Content);
        }
        catch (InvalidTemplateMetadataException)
        {
            requiresLayout = true;
        }
        NativeTemplatePolicyCandidate? owned = _policies is null ? null
            : await _policies.QualifyProjectAsync(source, requiresLayout, cancellationToken);
        NativeProjectLayoutQualification? layout = owned?.Project is { } anchor
            ? new(entry.Identity, configuration.Fingerprint, anchor) { Policy = owned }
            : owned is null && _projectSupport.QualifyLayoutAsync is { } qualify ? await qualify(source, cancellationToken) : null;
        cancellationToken.ThrowIfCancellationRequested();
        if (layout is not null)
        {
            if (layout.TemplateIdentity != entry.Identity
                || !string.Equals(layout.ConfigurationFingerprint, configuration.Fingerprint, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidTemplateMetadataException("Project layout qualification does not match the exact mounted candidate.");
            }
            _projectSupport.Planner.ReadOwnedLayout(configuration.Content, layout);
            _projectLayouts[new(template.Identity, template.MountPointUri, template.ConfigPlace, configuration.Fingerprint,
                configuration.HostConfigPlace, configuration.HostData)] = layout;
        }
    }

    private TemplateProjectSupport? ProjectSupport(ITemplateInfo template, TemplateConfigurationSnapshot configuration) => _projectSupport is { } support
        && _projectLayouts.TryGetValue(new(template.Identity, template.MountPointUri, template.ConfigPlace, configuration.Fingerprint,
            configuration.HostConfigPlace, configuration.HostData), out NativeProjectLayoutQualification? layout)
        ? support with { OwnedLayout = layout } : _projectSupport;

    private sealed record SelectedSourceKey(string Identity, string Mount, string ConfigurationPath,
        string ConfigurationSha256, string? HostPath, string? HostData);

    private async Task<TemplateInvocationResult?> ValidateGuardedSourcesAsync(ITemplateInfo template,
        TemplateConfigurationSnapshot configuration, TemplateInvocationRequest request, CancellationToken cancellationToken)
    {
        TemplateInvocationResult? failure = await ValidateSelectedConfigurationAsync(template, configuration, cancellationToken);
        if (failure is not null)
        {
            return failure;
        }
        if (request.PolicyCandidate is { } policy)
        {
            try
            {
                await policy.RevalidateDeclarationAsync(cancellationToken);
            }
            catch (IOException exception)
            {
                return new(TemplateInvocationStatus.TemplateChanged, [], exception.Message);
            }
            catch (NotSupportedException exception)
            {
                return new(TemplateInvocationStatus.InvalidTemplate, [], exception.Message);
            }
            catch (UnauthorizedAccessException exception)
            {
                return new(TemplateInvocationStatus.TemplateChanged, [], exception.Message);
            }
        }
        if (request.SourceSnapshot is { } snapshot && (configuration.Type != TemplateType.Item || snapshot.TemplateIdentity != template.Identity
            || !string.Equals(snapshot.ConfigurationSha256, configuration.Fingerprint, StringComparison.OrdinalIgnoreCase)))
        {
            return new(TemplateInvocationStatus.InvalidTemplate, [], "Source qualification is not bound to the exact selected item configuration.");
        }

        foreach (TemplateEffectSource source in (request.SourceSnapshot?.Sources ?? [])
            .Concat(request.ExpectedPreparation?.Sources ?? []).Concat(request.CapturedSources).Distinct())
        {
            ReadOnlyMemory<byte> bytes;
            try
            {
                bytes = await ReadPackageFileAsync(template, source.Identity, cancellationToken);
            }
            catch (FileNotFoundException exception)
            {
                return new(TemplateInvocationStatus.TemplateChanged, [], exception.Message);
            }
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes.Span)), source.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return new(TemplateInvocationStatus.TemplateChanged, [], "A selected package source changed. Rerun selection.");
            }
        }
        return null;
    }

    private string? QualifyBindings(ITemplateInfo template, TemplateConfigurationSnapshot configuration, TemplateInvocationRequest request)
    {
        if (request.MsBuildBindings is not { } snapshot)
        {
            if (_bindings is not null)
            {
                return "This controlled session requires its exact explicit MSBuild snapshot on every invocation.";
            }
            if (request.EffectSupport is not null && configuration.Type == TemplateType.Item
                && configuration.Root.TryGetProperty("symbols", out JsonElement symbols)
                && symbols.EnumerateObject().Any(symbol => symbol.Value.ValueKind == JsonValueKind.Object
                    && symbol.Value.TryGetProperty("binding", out JsonElement binding) && binding.ValueKind == JsonValueKind.String
                    && binding.GetString()!.StartsWith("msbuild:", StringComparison.OrdinalIgnoreCase)))
            {
                return "Controlled item effects require an explicit evaluated MSBuild snapshot; template framework defaults are not a project binding.";
            }
            return null;
        }
        NativeTemplateInvocationEffects? effects = request.EffectSupport;
        string? projectRoot = effects?.ProjectDirectory.Info.FullName ?? _engine.Context.Project?.RootDirectory.Info.FullName;
        if (configuration.Type != TemplateType.Item || (effects is null && request.PolicyCandidate is null)
            || projectRoot is null || _engine.Context.Project is null
            || snapshot.TemplateIdentity != template.Identity || snapshot.SourceIdentity != $"{template.MountPointUri}::{template.ConfigPlace}"
            || !string.Equals(snapshot.SourceFingerprint, configuration.Fingerprint, StringComparison.OrdinalIgnoreCase)
            || (effects is not null && (!Path.IsPathFullyQualified(effects.ExactProjectPath)
                || !SamePath(snapshot.ProjectFilePath, effects.ExactProjectPath)))
            || !SamePath(_engine.Context.Project.RootDirectory.Info.FullName, projectRoot)
            || (request.PolicyCandidate is { } selectedPolicy
                && !string.Equals(_engine.Context.Project.Stack, selectedPolicy.Admission.Registration.Policy.Stack, StringComparison.OrdinalIgnoreCase)))
        {
            return "The explicit MSBuild snapshot does not match the selected item, source or controlled project boundary.";
        }
        string relative = Path.GetRelativePath(_engine.Context.Project.RootDirectory.Info.FullName, snapshot.ProjectFilePath);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return "The evaluated MSBuild project is outside the selected Functions project.";
        }
        if (_bindings is not null && _bindings != snapshot)
        {
            return "This controlled session cannot replace its accepted immutable MSBuild snapshot.";
        }
        if (_bindings is null)
        {
            _engine.ApplyBindings(snapshot);
            _bindings = snapshot;
        }
        return null;
    }

    private static bool SamePath(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
        Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool SameSourcePin(TemplateEffectSource left, TemplateEffectSource right)
        => left.TemplateIdentity == right.TemplateIdentity && left.Identity == right.Identity
        && string.Equals(left.Sha256, right.Sha256, StringComparison.OrdinalIgnoreCase);

    private string? GetFSharpContextDiagnostic(ITemplateInfo template, TemplateConfigurationSnapshot configuration, TemplateInvocationRequest request)
        => request.EffectSupport?.FSharpRepair is not null
            && (configuration.Type != TemplateType.Item || request.MsBuildBindings is null
                || !SamePath(request.MsBuildBindings.ProjectFilePath, request.EffectSupport.ExactProjectPath)
                || Path.GetExtension(request.EffectSupport.ExactProjectPath) != ".fsproj")
            ? "Compile repair requires the selected item, its exact evaluated project snapshot and controlled capability." : null;

    private string? ValidateCapturedAuthority(ITemplateInfo template, TemplateConfigurationSnapshot configuration,
        TemplateInvocationRequest request, TemplateOutputImage image, ICreationEffects effects)
    {
        if (request.SourceSnapshot is null && request.EffectSupport is null && request.ExpectedPreparation is null && !request.RequireCapturedSources)
        {
            return null;
        }
        IFileChange[] files = [.. effects.FileChanges.Where(change => image.HasFile(image.GetTarget(change.TargetRelativePath)))];
        string[] mappedDirectories = [.. GetDirectoryOutputs(configuration, effects).Select(image.GetTarget)];
        if (files.Length != image.Files.Count || files.Any(change => change is not IFileChange2
                || change.ChangeKind is not (ChangeKind.Create or ChangeKind.Change or ChangeKind.Overwrite))
            || files.Select(change => Path.GetFullPath(image.GetTarget(change.TargetRelativePath))).Distinct(PathComparer).Count() != files.Length
            || files.OfType<IFileChange2>().Any(change => string.IsNullOrWhiteSpace(change.SourceRelativePath))
            || files.OfType<IFileChange2>().Select(change => NativeTemplateSourceSnapshot.Normalize(change.SourceRelativePath)).Distinct(StringComparer.Ordinal).Count() != files.Length
            || image.Files.Any(file => files.Count(change => SamePath(image.GetTarget(file), image.GetTarget(change.TargetRelativePath))) != 1)
            || image.Directories.Any(directory => directory != "." && !image.Files.Any(file =>
                image.GetTarget(file).StartsWith(image.GetTarget(directory) + Path.DirectorySeparatorChar,
                    Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                && !mappedDirectories.Any(mapped => SamePath(mapped, image.GetTarget(directory))
                    || mapped.StartsWith(image.GetTarget(directory) + Path.DirectorySeparatorChar,
                        Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))))
        {
            return "Qualified native capture requires an exact one-to-one regular-file source mapping for every actual output, not placeholder directories.";
        }
        if (request.SourceSnapshot is { } snapshot
            && (files.Length != snapshot.Outputs.Count
                || effects.FileChanges.Count != files.Length
                || files.OfType<IFileChange2>().Any(change => snapshot.Outputs.Count(source =>
                    NativeTemplateSourceSnapshot.Normalize(source.SourceRelativePath) == NativeTemplateSourceSnapshot.Normalize(change.SourceRelativePath)) != 1)
                || image.Directories.Any(directory => directory != "." && !image.Files.Any(file =>
                    Path.GetRelativePath(image.GetTarget(directory), image.GetTarget(file)) is { } relative
                    && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)))))
        {
            return "The actual captured files or placeholder directories do not match the qualified regular source inventory.";
        }
        if (request.EffectSupport?.FSharpRepair is { } repair)
        {
            if (repair.GeneratedFiles.Any(file => !image.HasFile(file.Path)
                    || !string.Equals(HashCaptured(image, file.Path), file.Sha256, StringComparison.OrdinalIgnoreCase)))
            {
                return "Compile repair does not match the actual captured regular-file bytes and evaluated project binding.";
            }
        }
        return null;

    }

    private static StringComparer PathComparer => Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static string HashCaptured(TemplateOutputImage image, string path)
    {
        using Stream stream = image.OpenCaptured(image.GetRelative(Path.GetFullPath(path))!);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private async Task<NativeTemplatePreparationSnapshot> CreateSnapshotAsync(ITemplateInfo template, TemplateConfigurationSnapshot configuration,
        TemplateOutputImage image, ICreationEffects effects, ICreationResult creation, bool qualifySources, CancellationToken cancellationToken)
    {
        Dictionary<string, ReadOnlyMemory<byte>> files = new(StringComparer.Ordinal);
        foreach (string file in image.Files)
        {
            using Stream stream = image.OpenCaptured(file);
            using MemoryStream bytes = new();
            stream.CopyTo(bytes);
            files.Add(file, bytes.ToArray());
        }
        List<TemplateEffectSource> sources = [];
        List<NativeTemplateOutputSource> mappings = [];
        if (qualifySources)
        {
            foreach (IFileChange2 change in effects.FileChanges.OfType<IFileChange2>().Where(change => image.HasFile(image.GetTarget(change.TargetRelativePath))))
            {
                TemplateEffectSource pin = await _engine.CaptureMappedSourceAsync(template, change.SourceRelativePath, cancellationToken);
                sources.Add(pin);
                mappings.Add(new(NativeTemplateSourceSnapshot.Normalize(change.SourceRelativePath), pin));
            }
        }
        return new(new(template.Identity, $"{template.MountPointUri}::{template.ConfigPlace}", configuration.Fingerprint), effects, files, creation)
        {
            Sources = Array.AsReadOnly<TemplateEffectSource>([.. sources]),
            OutputSources = Array.AsReadOnly<NativeTemplateOutputSource>([.. mappings]),
            Directories = Array.AsReadOnly<string>([.. image.Directories]),
        };
    }

    private static bool SameCapture(NativeTemplatePreparationSnapshot expected, NativeTemplatePreparationSnapshot actual)
        => expected.Source == actual.Source
        && expected.ExpandedFiles.Count == actual.ExpandedFiles.Count
        && expected.ExpandedFiles.All(pair => actual.ExpandedFiles.TryGetValue(pair.Key, out ReadOnlyMemory<byte> bytes)
            && pair.Value.Span.SequenceEqual(bytes.Span))
        && expected.Directories.SequenceEqual(actual.Directories)
        && expected.Sources.SequenceEqual(actual.Sources)
        && expected.OutputSources.SequenceEqual(actual.OutputSources)
        && expected.Effects.FileChanges.OfType<IFileChange2>().Select(change => (change.SourceRelativePath, change.TargetRelativePath, change.ChangeKind))
            .SequenceEqual(actual.Effects.FileChanges.OfType<IFileChange2>().Select(change => (change.SourceRelativePath, change.TargetRelativePath, change.ChangeKind)))
        && DescribeActions(expected.Effects.CreationResult) == DescribeActions(actual.Effects.CreationResult)
        && expected.Effects.CreationResult.PrimaryOutputs.Select(path => path.Path).SequenceEqual(actual.Effects.CreationResult.PrimaryOutputs.Select(path => path.Path));

    private static string DescribeActions(ICreationResult creation) => JsonSerializer.Serialize(creation.PostActions.Select(action =>
        new { action.ActionId, action.ContinueOnError, Args = action.Args.OrderBy(pair => pair.Key, StringComparer.Ordinal) }));

    private async Task<NativeTemplateEffectResult?> PlanEffectsAsync(ITemplateInfo template, TemplateConfigurationSnapshot configuration,
        TemplateInvocationRequest request, TemplateOutputImage image, ICreationEffects effects,
        NativeTemplatePreparationSnapshot capture, IReadOnlyList<TemplateProjectConfigurationPlan> plans, bool qualifiedProject,
        CancellationToken cancellationToken, NativeTemplatePolicySession? policy = null)
    {
        NativeTemplateInvocationEffects? support = request.EffectSupport;
        TemplateFSharpCompileEffect? repair = request.PolicyCompileRepair ?? support?.FSharpRepair;
        if (policy is not null && !qualifiedProject && configuration.Type == TemplateType.Project && support is null
            && repair is null && effects.CreationResult.PostActions.All(action => action.ActionId == Guid.Parse(TemplateProjectConfigurationPlanner.ConfigurationActionId)))
        {
            return null;
        }
        if (policy is not null && effects.CreationResult.PostActions.Count == 0 && repair is null)
        {
            return null;
        }
        if (policy is not null && repair is null && effects.CreationResult.PostActions.All(action =>
            action.ActionId == NativeTemplateEffectPlanner.EditorActionId && action.ContinueOnError
            && action.Args.Count == 1 && action.Args.TryGetValue("files", out string? files) && files == "0"))
        {
            TemplateEffectSource editorSource = new(template.Identity, $"{template.MountPointUri}::{template.ConfigPlace}", configuration.Fingerprint);
            NativeTemplateEffectPlan editorPlan = new(policy.ProjectDirectory,
                [.. effects.CreationResult.PostActions.Select(_ => new TemplateEditorNoOpEffect(editorSource))], capture);
            await policy.QualifyEffectsAsync(editorPlan, cancellationToken);
            return new("previewed", true, editorPlan, [], [], []);
        }
        if (support is null && policy is null)
        {
            if (qualifiedProject)
            {
                throw new InvalidTemplateMetadataException("Qualified project creation requires an explicit controlled dependency capability.");
            }
            return null;
        }
        if (configuration.Type != TemplateType.Item && !qualifiedProject)
        {
            throw new InvalidTemplateMetadataException("Controlled dependency effects require a qualified item or project layout.");
        }
        WorkingDirectory projectDirectory = support?.ProjectDirectory ?? policy!.ProjectDirectory;
        string? projectPath = support?.ExactProjectPath ?? policy?.ProjectBinding?.ProjectPath
            ?? (plans.Count == 1 ? plans[0].PrimaryOutput : null);
        if (projectPath is null)
        {
            throw new InvalidTemplateMetadataException("Dependency qualification requires an exact source-bound project path.");
        }
        if (qualifiedProject && (plans.Count != 1 || !SamePath(projectPath, plans[0].PrimaryOutput)
            || !SamePath(projectDirectory.Info.FullName, image.Output)))
        {
            throw new InvalidTemplateMetadataException("Qualified effects must target the captured project in the exact invocation root.");
        }
        TemplateEffectSource source = new(template.Identity, $"{template.MountPointUri}::{template.ConfigPlace}", configuration.Fingerprint);
        NativeTemplateEffectPlanner planner = new();
        NativeTemplateEffectPlanningResult plan = repair is not null
            ? planner.PlanFSharpItemDependencies(projectDirectory, source, effects, request.OutputDirectory, projectPath, repair)
            : planner.PlanDependencies(projectDirectory, source, effects.CreationResult, projectPath);
        if (plan.Plan is null)
        {
            return new("invalidPlan", false, new(projectDirectory, []), [], [], plan.Diagnostics);
        }
        NativeTemplateEffectPlan capturedPlan = new(plan.Plan.OutputDirectory, plan.Plan.Effects, capture);
        if (policy is not null)
        {
            await policy.QualifyEffectsAsync(capturedPlan, cancellationToken);
            if (capturedPlan.Effects.All(effect => effect is TemplateEditorNoOpEffect))
            {
                return new("previewed", true, capturedPlan, [], [], []);
            }
            policy.BindExistingExecutor(request, projectDirectory, projectPath);
            support = policy.ExecutionSupport;
        }
        if (support is null)
        {
            throw new InvalidTemplateMetadataException("No exact controlled executor is available for the requested role.");
        }
        NativeTemplateEffectResult preview = await new NativeTemplateEffectWorkflow().ExecuteAsync(
            capturedPlan, support.Capability, dryRun: true, confirmed: false, cancellationToken);
        if (!request.DryRun && !support.Confirmed && preview.Succeeded)
        {
            return preview with { Kind = "declined", Succeeded = false, Diagnostics = ["The controlled effect plan was not confirmed; nothing was changed."] };
        }
        return preview;
    }

    internal async Task<NativeTemplatePreparationResult> CaptureAsync(ITemplateInfo template,
        TemplateConfigurationSnapshot configuration, TemplateInvocationRequest request, bool expandFiles, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        request = request with { RequireCapturedSources = true };
        TemplateInvocationResult? invalid = await ValidateGuardedSourcesAsync(template, configuration, request, cancellationToken);
        if (invalid is not null)
        {
            return new(null, invalid);
        }
        string? binding = QualifyBindings(template, configuration, request) ?? GetFSharpContextDiagnostic(template, configuration, request);
        if (binding is not null)
        {
            return new(null, new(TemplateInvocationStatus.InvalidArguments, [], binding));
        }
        if (expandFiles && string.IsNullOrWhiteSpace(request.Name))
        {
            return new(null, new(TemplateInvocationStatus.InvalidArguments, [], "Private native expansion requires an effective name."));
        }
        using TemplateOutputImage image = new(_engine.FileSystem, request.OutputDirectory.Info.FullName, cancellationToken);
        ITemplateCreationResult result;
        try
        {
            result = await _engine.RenderImageAsync(template, request,
                CreateInputData(configuration.Parameters, request.Parameters), image, cancellationToken);
        }
        catch (IOException exception)
        {
            return new(null, new(TemplateInvocationStatus.InvalidTemplate, [], exception.Message));
        }
        catch (InvalidTemplateMetadataException exception)
        {
            return new(null, new(TemplateInvocationStatus.InvalidTemplate, [], exception.Message));
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (result.Status != CreationResultStatus.Success)
        {
            return new(null, new(MapCreationStatus(result.Status, cancellationToken), [], result.ErrorMessage));
        }
        if (result.CreationEffects is null || result.CreationResult is null)
        {
            return new(null, new(TemplateInvocationStatus.EngineFailure, [], "The native capture did not report its result and source mappings."));
        }
        NativeTemplatePreparationSnapshot metadata = new(new(template.Identity, $"{template.MountPointUri}::{template.ConfigPlace}", configuration.Fingerprint),
            result.CreationEffects, new Dictionary<string, ReadOnlyMemory<byte>>(), result.CreationResult);
        invalid = await ValidateGuardedSourcesAsync(template, configuration, request, cancellationToken);
        string? diagnostic = image.Diagnostic ?? image.ConflictDiagnostic
            ?? ValidateCapturedAuthority(template, configuration, request, image, metadata.Effects);
        if (invalid is not null || diagnostic is not null)
        {
            return new(null, invalid ?? new(TemplateInvocationStatus.InvalidTemplate, [], diagnostic));
        }
        NativeTemplatePreparationSnapshot snapshot;
        try
        {
            snapshot = await CreateSnapshotAsync(template, configuration, image, metadata.Effects,
                metadata.Effects.CreationResult, qualifySources: true, cancellationToken);
        }
        catch (FileNotFoundException exception)
        {
            return new(null, new(TemplateInvocationStatus.InvalidTemplate, [], exception.Message));
        }
        catch (InvalidTemplateMetadataException exception)
        {
            return new(null, new(TemplateInvocationStatus.InvalidTemplate, [], exception.Message));
        }
        if (request.SourceSnapshot is { } pinned && pinned.Outputs.Any(expected =>
            !snapshot.OutputSources.Any(actual => actual.SourceRelativePath == NativeTemplateSourceSnapshot.Normalize(expected.SourceRelativePath)
                && SameSourcePin(actual.Source, expected.Source))))
        {
            return new(null, new(TemplateInvocationStatus.InvalidTemplate, [], "The captured native source map does not match its selected package pins."));
        }
        if (snapshot.Effects.FileChanges.Count != image.Files.Count || image.Files.Any(file =>
            _engine.FileSystem.FileExists(image.GetTarget(file)) || _engine.FileSystem.DirectoryExists(image.GetTarget(file)))
            || image.Directories.Any(directory => directory != "." && !image.Files.Any(file => image.GetTarget(file)
                .StartsWith(image.GetTarget(directory) + Path.DirectorySeparatorChar, PathComparer == StringComparer.OrdinalIgnoreCase
                    ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))))
        {
            return new(null, new(TemplateInvocationStatus.InvalidTemplate, [], "Detached capture requires unoccupied regular outputs without placeholder directories."));
        }
        return new(expandFiles ? snapshot : new(snapshot.Source, snapshot.Effects, new Dictionary<string, ReadOnlyMemory<byte>>())
        {
            Sources = snapshot.Sources,
            OutputSources = snapshot.OutputSources,
            Directories = snapshot.Directories,
        }, null);
    }
}
