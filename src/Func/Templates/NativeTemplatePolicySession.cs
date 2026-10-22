// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Native;

namespace Azure.Functions.Cli.Templates;

internal sealed class NativeTemplatePolicyCandidate(
    BoundNativeTemplatePolicy admission, TemplateCatalogEntry entry, string configurationSha256, string configurationSourceIdentity,
    Func<string, CancellationToken, Task<ReadOnlyMemory<byte>>> read,
    Func<CancellationToken, Task<TemplateInvocationResult?>> revalidate, string configurationRelativePath, bool requiresBinding = false)
{
    private readonly Func<string, CancellationToken, Task<ReadOnlyMemory<byte>>> _read = read ?? throw new ArgumentNullException(nameof(read));
    private readonly Func<CancellationToken, Task<TemplateInvocationResult?>> _revalidate = revalidate ?? throw new ArgumentNullException(nameof(revalidate));

    public BoundNativeTemplatePolicy Admission { get; } = admission ?? throw new ArgumentNullException(nameof(admission));
    public TemplateCatalogEntry Entry { get; } = entry ?? throw new ArgumentNullException(nameof(entry));
    public string ConfigurationSha256 { get; } = configurationSha256 ?? throw new ArgumentNullException(nameof(configurationSha256));
    public string ConfigurationSourceIdentity { get; } = configurationSourceIdentity ?? throw new ArgumentNullException(nameof(configurationSourceIdentity));
    public bool RequiresBinding { get; } = requiresBinding;
    public string ConfigurationRelativePath { get; } = configurationRelativePath ?? throw new ArgumentNullException(nameof(configurationRelativePath));
    public NativeTemplateProjectAnchor? Project { get; private set; }
    public NativeTemplatePolicyStages Stages { get; private set; }

    public WorkloadNativeTemplateSource CreateSource(NativeTemplatePolicyLifetime lifetime)
        => new(Entry, ConfigurationSha256, lifetime, _read, _revalidate, Admission);

    public async Task<bool> QualifySourceAsync(bool requireProjectLayout, CancellationToken cancellationToken)
    {
        using NativeTemplatePolicyLifetime lifetime = new();
        WorkloadNativeTemplateSource source = CreateSource(lifetime);
        await source.RevalidateAsync(cancellationToken);
        NativeTemplatePolicy policy = Admission.Registration.Policy;
        if (!policy.Handles(source))
        {
            if (Admission.Required)
            {
                throw new InvalidDataException("The actual owning workload does not handle this required native content. Update the workload.");
            }
            return false;
        }
        Stages = policy.GetRequestedStages(source);
        const NativeTemplatePolicyStages known = NativeTemplatePolicyStages.Invocation | NativeTemplatePolicyStages.CapturedOutput | NativeTemplatePolicyStages.Effects;
        if ((Stages & ~known) != 0)
        {
            throw new InvalidDataException("The workload returned unknown native qualification stages.");
        }
        if (Entry.Type == TemplateType.Project)
        {
            IReadOnlyList<string> diagnostics = await policy.ValidateProjectAsync(source, cancellationToken);
            RequireDiagnostics(diagnostics);
            if (requireProjectLayout || Stages.HasFlag(NativeTemplatePolicyStages.CapturedOutput))
            {
                NativeTemplateProjectAnchor declaration = await policy.DescribeProjectLayoutAsync(source, cancellationToken)
                    ?? throw new InvalidDataException("The workload returned no required source project declaration.");
                Project = new(declaration.PrimaryOutputIndex, declaration.Stack, declaration.Language);
            }
        }
        await source.RevalidateAsync(cancellationToken);
        return true;
    }

    public async Task RevalidateDeclarationAsync(CancellationToken cancellationToken)
    {
        using NativeTemplatePolicyLifetime lifetime = new();
        WorkloadNativeTemplateSource source = CreateSource(lifetime);
        await source.RevalidateAsync(cancellationToken);
        NativeTemplatePolicy policy = Admission.Registration.Policy;
        if (!policy.Handles(source) || policy.GetRequestedStages(source) != Stages)
        {
            throw new InvalidDataException("The selected source policy or required stages changed. Rerun selection.");
        }
        if (Entry.Type == TemplateType.Project)
        {
            RequireDiagnostics(await policy.ValidateProjectAsync(source, cancellationToken));
            if (Project is { } expected)
            {
                NativeTemplateProjectAnchor current = await policy.DescribeProjectLayoutAsync(source, cancellationToken)
                    ?? throw new InvalidDataException("The required source declaration is no longer available.");
                if (current != expected)
                {
                    throw new InvalidDataException("The selected source project declaration changed. Rerun selection.");
                }
            }
        }
        await source.RevalidateAsync(cancellationToken);
    }

    internal static void RequireDiagnostics(IReadOnlyList<string>? diagnostics)
    {
        if (diagnostics is null)
        {
            throw new InvalidDataException("The workload returned no qualification diagnostics collection.");
        }
        if (diagnostics.Count > 0)
        {
            throw new InvalidDataException(string.Join(" ", diagnostics));
        }
    }
}

internal sealed class NativeTemplatePolicySession(
    NativeTemplatePolicyCandidate candidate, WorkingDirectory projectDirectory, ITemplateArgumentParser arguments) : IDisposable
{
    private readonly NativeTemplatePolicyCandidate _candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
    private readonly WorkingDirectory _projectDirectory = projectDirectory ?? throw new ArgumentNullException(nameof(projectDirectory));
    private readonly ITemplateArgumentParser _arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
    private readonly NativeTemplatePolicyLifetime _lifetime = new();
    private WorkloadNativeTemplateInvocation? _invocation;
    private WorkloadNativeTemplateCapturedOutput? _captured;

    public NativeTemplateProjectAnchor? Project => _candidate.Project;
    public NativeTemplateCompileRepair? CompileRepair { get; private set; }
    internal BoundNativeTemplatePolicy Admission => _candidate.Admission;
    internal NativeTemplateProjectBinding? ProjectBinding => _invocation?.ProjectBinding;
    internal WorkingDirectory ProjectDirectory => _projectDirectory;
    internal NativeTemplateInvocationEffects? ExecutionSupport { get; private set; }

    internal void BindExistingExecutor(TemplateInvocationRequest request, WorkingDirectory projectDirectory, string projectPath)
    {
        BoundNativeTemplateExecutor? executor = request.OwnerExecutor;
        if (executor is null)
        {
            throw new InvalidDataException("This loaded policy has no bound executor for the requested non-append role. No output was installed.");
        }
        if (!ReferenceEquals(executor.Admission, Admission)
            || (request.EffectSupport is { } supplied && !ReferenceEquals(supplied.Capability, executor.Capability)))
        {
            throw new InvalidDataException("The supplied executor is not the exact instance bound to this runtime-owner admission.");
        }
        executor.BindInvocation(this);
        ExecutionSupport = new(projectDirectory, projectPath, executor.Capability, executor.Confirmed);
    }

    public async Task<TemplateInvocationRequest> PrepareAsync(TemplateInvocationRequest request, CancellationToken cancellationToken)
    {
        _lifetime.ThrowIfDisposed();
        await _candidate.RevalidateDeclarationAsync(cancellationToken);
        WorkloadNativeTemplateSource source = _candidate.CreateSource(_lifetime);
        WorkloadNativeTemplateInvocation initial = new(source, _lifetime, _projectDirectory, request);
        bool needsBinding = request.MsBuildBindings is not null || _candidate.RequiresBinding;
        NativeTemplatePolicyStages stages = _candidate.Stages
            | (needsBinding || Project is not null || request.OwnerExecutor is not null || request.EffectSupport is not null
                ? NativeTemplatePolicyStages.Invocation : NativeTemplatePolicyStages.None);
        NativeTemplateProjectBinding? binding = null;
        if (stages.HasFlag(NativeTemplatePolicyStages.Invocation))
        {
            NativeTemplateInvocationQualification result = await _candidate.Admission.Registration.Policy
                .QualifyInvocationAsync(initial, cancellationToken)
                ?? throw new InvalidDataException("The workload returned no required invocation qualification.");
            NativeTemplatePolicyCandidate.RequireDiagnostics(result.Diagnostics);
            if (!result.Succeeded)
            {
                throw new InvalidDataException("The owning workload refused this invocation.");
            }
            Dictionary<string, string?> values = new(request.Parameters, StringComparer.Ordinal);
            foreach ((string key, string? value) in result.CanonicalDefaults)
            {
                if (request.ExplicitParameterNames.Contains(key) || !_candidate.Entry.Parameters.Any(parameter => parameter.CanonicalName == key))
                {
                    throw new InvalidDataException("A workload default changed an explicit or undeclared canonical parameter.");
                }
                values[key] = value;
            }
            if (result.CanonicalDefaults.Count > 0)
            {
                TemplateArgumentParseRequest input = request.ArgumentSource
                    ?? throw new InvalidDataException("Workload defaults require the original host-captured argument provenance.");
                Dictionary<string, string?> completed = new(input.CompletedValuesByCanonicalName
                    ?? new Dictionary<string, string?>(), StringComparer.Ordinal);
                foreach ((string key, string? value) in values)
                {
                    if (!request.ExplicitParameterNames.Contains(key))
                    {
                        completed[key] = value;
                    }
                }
                TemplateCandidateParseResult parsed = _arguments.Parse(input with { CompletedValuesByCanonicalName = completed }, cancellationToken);
                if (!parsed.IsComplete || !parsed.IsArgumentCompatible)
                {
                    throw new InvalidDataException("Workload defaults do not satisfy the selected native parameter schema.");
                }
                values = new(parsed.ValuesByCanonicalName, StringComparer.Ordinal);
            }
            foreach (NativeTemplateSourceStamp pin in result.RequiredSources)
            {
                if (pin.TemplateIdentity != source.Identity || pin.Sha256 is not { Length: 64 } || !pin.Sha256.All(Uri.IsHexDigit))
                {
                    throw new InvalidDataException("Workload source qualification is not bound to the selected template.");
                }
            }
            request = request with
            {
                Parameters = values,
                CapturedSources = Array.AsReadOnly<TemplateEffectSource>([.. request.CapturedSources,
                    .. result.RequiredSources.Select(pin => new TemplateEffectSource(pin.TemplateIdentity, pin.RelativePath, pin.Sha256))]),
            };
            if (result.ProjectBinding is { } proposed)
            {
                binding = new(proposed.ProjectPath, proposed.TargetFramework, proposed.RootNamespace);
                NativeMsBuildBindingSnapshot snapshot = new(binding.ProjectPath, source.Identity,
                    _candidate.ConfigurationSourceIdentity, source.ConfigurationSha256, binding.TargetFramework, binding.RootNamespace);
                if (request.MsBuildBindings is { } existing && existing != snapshot)
                {
                    throw new InvalidDataException("The workload attempted to replace an existing immutable native project binding.");
                }
                request = request with { MsBuildBindings = snapshot };
            }
            else if (needsBinding)
            {
                throw new InvalidDataException("The workload omitted the required evaluated project binding.");
            }
        }
        _invocation = new(source, _lifetime, _projectDirectory, request, binding);
        await _invocation.RevalidateAsync(cancellationToken);
        return request;
    }

    public async Task QualifyCaptureAsync(NativeTemplatePreparationSnapshot snapshot, CancellationToken cancellationToken)
    {
        _lifetime.ThrowIfDisposed();
        WorkloadNativeTemplateInvocation invocation = _invocation ?? throw new InvalidOperationException("Qualify the invocation before its capture.");
        _captured = new(invocation, _lifetime, snapshot);
        bool requiresCompile = invocation.ProjectBinding is { } binding && Path.GetExtension(binding.ProjectPath) == ".fsproj"
            && _captured.Files.Any(file => Path.GetExtension(file.TargetRelativePath) == ".fs");
        if (_candidate.Stages.HasFlag(NativeTemplatePolicyStages.CapturedOutput) || Project is not null || requiresCompile)
        {
            NativeTemplateCaptureQualification result = await _candidate.Admission.Registration.Policy
                .QualifyCapturedOutputAsync(_captured, cancellationToken)
                ?? throw new InvalidDataException("The workload returned no required capture qualification.");
            NativeTemplatePolicyCandidate.RequireDiagnostics(result.Diagnostics);
            if (!result.Succeeded)
            {
                throw new InvalidDataException("The owning workload refused the captured native output.");
            }
            CompileRepair = result.CompileRepair is null ? null : new(result.CompileRepair.ProjectPath,
                result.CompileRepair.ExpectedProjectSha256, result.CompileRepair.EvaluatedCompileOrder, result.CompileRepair.GeneratedFiles);
            if (requiresCompile && CompileRepair is null)
            {
                throw new InvalidDataException("The required compile-order qualification is missing.");
            }
        }
        await _captured.RevalidateAsync(cancellationToken);
    }

    internal TemplateFSharpCompileEffect CreateCompileRepair()
    {
        _lifetime.ThrowIfDisposed();
        NativeTemplateCompileRepair repair = CompileRepair ?? throw new InvalidOperationException("No compile repair was qualified.");
        WorkloadNativeTemplateCapturedOutput captured = _captured ?? throw new InvalidOperationException("No output was captured.");
        NativeTemplateProjectBinding binding = ProjectBinding ?? throw new InvalidDataException("Compile repair requires its evaluated project binding.");
        if (!SamePath(binding.ProjectPath, repair.ProjectPath))
        {
            throw new InvalidDataException("Compile qualification targets another evaluated project.");
        }
        List<TemplateGeneratedFSharpFile> generated = [];
        foreach (NativeTemplateCapturedFile file in repair.GeneratedFiles)
        {
            NativeTemplateCapturedFile? actual = captured.Files.SingleOrDefault(candidate =>
                Normalize(candidate.TargetRelativePath) == Normalize(file.TargetRelativePath));
            if (actual is null || Normalize(actual.SourceRelativePath) != Normalize(file.SourceRelativePath)
                || actual.Source.TemplateIdentity != file.Source.TemplateIdentity || actual.Source.RelativePath != file.Source.RelativePath
                || !string.Equals(actual.Source.Sha256, file.Source.Sha256, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(actual.Sha256, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Compile qualification does not match the actual generated bytes and original source.");
            }
            generated.Add(new(Path.GetFullPath(Path.Combine(captured.Invocation.OutputDirectory.Info.FullName, actual.TargetRelativePath)), actual.Sha256));
        }
        return new(new(_candidate.Entry.Identity, _candidate.ConfigurationSourceIdentity, _candidate.ConfigurationSha256),
            repair.ProjectPath, repair.ExpectedProjectSha256, repair.EvaluatedCompileOrder, generated);
    }

    public async Task QualifyEffectsAsync(NativeTemplateEffectPlan plan, CancellationToken cancellationToken)
    {
        _lifetime.ThrowIfDisposed();
        WorkloadNativeTemplateCapturedOutput captured = _captured ?? throw new InvalidOperationException("Qualify captured output before its effects.");
        NativeTemplatePolicy policy = _candidate.Admission.Registration.Policy;
        foreach (TemplateEffect effect in plan.Effects)
        {
            string relative = effect.Source.TemplateIdentity == _candidate.Entry.Identity
                && effect.Source.Identity == _candidate.ConfigurationSourceIdentity ? _candidate.ConfigurationRelativePath : effect.Source.Identity;
            NativeTemplateSourceStamp source = new(effect.Source.TemplateIdentity, relative, effect.Source.Sha256);
            IReadOnlyList<string> diagnostics = effect switch
            {
                TemplateEditorNoOpEffect => [],
                TemplatePackageReferenceEffect package => await policy.QualifyPackageReferenceAsync(captured, source,
                    package.ProjectPath, package.PackageId, package.Version, package.ProjectFileExtension, package.ContinueOnError, cancellationToken),
                TemplateRestoreEffect restore => await policy.QualifyRestoreAsync(captured, source, restore.ProjectPath, restore.ContinueOnError, cancellationToken),
                TemplateCreateFileEffect create => await policy.QualifyCreateFileAsync(captured, source, create.TargetPath, create.Content, cancellationToken),
                TemplateFSharpCompileEffect => await policy.QualifyCompileRepairAsync(captured, source,
                    CompileRepair ?? throw new InvalidDataException("The workload omitted required compile qualification."), cancellationToken),
                _ => throw new InvalidDataException("This policy boundary does not authorize the requested effect role."),
            };
            NativeTemplatePolicyCandidate.RequireDiagnostics(diagnostics);
        }
        await captured.RevalidateAsync(cancellationToken);
    }

    public void Dispose() => _lifetime.Dispose();

    private static bool SamePath(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
        Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string Normalize(string path) => NativeTemplateSourceSnapshot.Normalize(path.Replace('\\', '/'));
}
