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
    private readonly Dictionary<string, DotNetProjectLayoutQualification> _projectLayouts = new(StringComparer.Ordinal);
    private NativeMsBuildBindingSnapshot? _bindings;

    internal Task<ReadOnlyMemory<byte>> ReadPackageFileAsync(ITemplateInfo template, string path, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return _engine.ReadPackageFileAsync(template, path, cancellationToken);
    }

    internal async Task QualifyProjectConfigurationAsync(ITemplateInfo template, TemplateCatalogEntry entry,
        TemplateConfigurationSnapshot configuration, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_projectSupport?.QualifyLayoutAsync is not { } qualify)
        {
            return;
        }

        DotNetProjectLayoutQualification? layout = await qualify(new(entry, configuration,
            $"{template.MountPointUri}::{template.ConfigPlace}", (path, token) => ReadPackageFileAsync(template, path, token)), cancellationToken);
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

    private TemplateProjectSupport? ProjectSupport(string content) => _projectSupport is { } support
        && _projectLayouts.TryGetValue(content, out DotNetProjectLayoutQualification? layout)
        ? support with { DotNetLayout = layout } : _projectSupport;

    private async Task<TemplateInvocationResult?> ValidateGuardedSourcesAsync(ITemplateInfo template,
        TemplateConfigurationSnapshot configuration, TemplateInvocationRequest request, CancellationToken cancellationToken)
    {
        TemplateInvocationResult? failure = await ValidateSelectedConfigurationAsync(template, configuration, cancellationToken);
        if (failure is not null)
        {
            return failure;
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
        if (configuration.Type != TemplateType.Item || effects is null
            || !string.Equals(_engine.Context.Project?.Stack, "dotnet", StringComparison.OrdinalIgnoreCase)
            || snapshot.TemplateIdentity != template.Identity || snapshot.SourceIdentity != $"{template.MountPointUri}::{template.ConfigPlace}"
            || !string.Equals(snapshot.SourceFingerprint, configuration.Fingerprint, StringComparison.OrdinalIgnoreCase)
            || !Path.IsPathFullyQualified(effects.ExactProjectPath)
            || !SamePath(snapshot.ProjectFilePath, effects.ExactProjectPath)
            || !SamePath(_engine.Context.Project!.RootDirectory.Info.FullName, effects.ProjectDirectory.Info.FullName))
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
            && (configuration.Type != TemplateType.Item || !template.TagsCollection.TryGetValue("language", out string? language) || language != "F#"
                || !string.Equals(_engine.Context.Project?.Stack, "dotnet", StringComparison.OrdinalIgnoreCase) || _engine.Context.Project?.Language != "F#"
                || request.MsBuildBindings?.TargetFramework != "net10.0" || Path.GetExtension(request.EffectSupport.ExactProjectPath) != ".fsproj")
            ? "F# compile repair requires the selected F# item, an exact evaluated net10.0 .fsproj snapshot and its controlled dependency capability." : null;

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
                return "F# compile repair does not match the actual captured regular-file bytes and explicit net10.0 project binding.";
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
        NativeTemplatePreparationSnapshot capture, IReadOnlyList<TemplateProjectConfigurationPlan> plans, bool qualifiedProject, CancellationToken cancellationToken)
    {
        if (request.EffectSupport is not { } support)
        {
            if (qualifiedProject)
            {
                throw new InvalidTemplateMetadataException("Qualified .NET project creation requires an explicit controlled dependency capability.");
            }
            return null;
        }
        if (configuration.Type != TemplateType.Item && !qualifiedProject)
        {
            throw new InvalidTemplateMetadataException("Controlled dependency effects require a qualified item or project layout.");
        }
        ArgumentNullException.ThrowIfNull(support.Capability);
        ArgumentNullException.ThrowIfNull(support.ProjectDirectory);
        if (qualifiedProject && (plans.Count != 1 || !SamePath(support.ExactProjectPath, plans[0].PrimaryOutput)
            || !SamePath(support.ProjectDirectory.Info.FullName, image.Output)))
        {
            throw new InvalidTemplateMetadataException("Qualified .NET effects must target the captured project in the exact invocation root.");
        }
        TemplateEffectSource source = new(template.Identity, $"{template.MountPointUri}::{template.ConfigPlace}", configuration.Fingerprint);
        NativeTemplateEffectPlanner planner = new();
        NativeTemplateEffectPlanningResult plan = support.FSharpRepair is { } repair
            ? planner.PlanFSharpItemDependencies(support.ProjectDirectory, source, effects, request.OutputDirectory, support.ExactProjectPath, repair)
            : planner.PlanDependencies(support.ProjectDirectory, source, effects.CreationResult, support.ExactProjectPath);
        if (plan.Plan is null)
        {
            return new("invalidPlan", false, new(support.ProjectDirectory, []), [], [], plan.Diagnostics);
        }
        NativeTemplateEffectPlan capturedPlan = new(plan.Plan.OutputDirectory, plan.Plan.Effects, capture);
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
