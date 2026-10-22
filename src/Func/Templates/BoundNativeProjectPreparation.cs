// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Runtime.Loader;
using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Native;
using Microsoft.TemplateEngine.Abstractions.PhysicalFileSystem;

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Owns copied native output and final invocation facts independently of the disposed render image.
/// </summary>
internal sealed class NativeProjectPreparationCapture
{
    private readonly IPhysicalFileSystem _basis;
    private readonly TemplateInvocationRequest _request;
    private readonly NativeTemplateProjectBinding? _binding;
    private readonly string _projectDirectory;
    private byte[]? _configuration;

    internal NativeProjectPreparationCapture(BoundNativeProjectPreparation owner, TemplateInvocationRequest request,
        NativeTemplatePreparationSnapshot snapshot, NativeTemplateProjectBinding? binding, WorkingDirectory projectDirectory,
        TemplateProjectConfigurationPlan project, IPhysicalFileSystem basis)
    {
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _basis = basis ?? throw new ArgumentNullException(nameof(basis));
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(projectDirectory);
        ArgumentNullException.ThrowIfNull(project);
        _projectDirectory = projectDirectory.Info.FullName;
        _request = request with
        {
            Parameters = new ReadOnlyDictionary<string, string?>(new Dictionary<string, string?>(request.Parameters, StringComparer.Ordinal)),
            ExplicitParameterNames = request.ExplicitParameterNames.ToFrozenSet(StringComparer.Ordinal),
            OutputDirectory = Copy(request.OutputDirectory),
            CapturedSources = Array.AsReadOnly<TemplateEffectSource>([.. request.CapturedSources]),
            ArgumentSource = null,
        };
        _binding = binding is null ? null : new(binding.ProjectPath, binding.TargetFramework, binding.RootNamespace);
        Snapshot = new(snapshot.Source, snapshot.Effects, snapshot.ExpandedFiles)
        {
            Sources = Array.AsReadOnly<TemplateEffectSource>([.. snapshot.Sources]),
            OutputSources = Array.AsReadOnly<NativeTemplateOutputSource>([.. snapshot.OutputSources]),
            Directories = Array.AsReadOnly<string>([.. snapshot.Directories]),
        };
        Project = new(project.PrimaryOutput, project.Stack, project.Language);
    }

    public BoundNativeProjectPreparation Owner { get; }
    public NativeTemplatePreparationSnapshot Snapshot { get; }
    public TemplateProjectConfigurationPlan Project { get; }

    internal async Task CaptureConfigurationAsync(TemplateProjectConfigurationWriteReceipt written, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(written);
        if (_configuration is not null)
        {
            throw new InvalidOperationException("The mandatory configuration receipt is immutable once captured.");
        }
        if (!SamePath(written.ConfigurationPath, Project.ConfigurationPath))
        {
            throw new InvalidDataException("The writer's mandatory configuration receipt belongs to another project.");
        }
        RequireKind(Project.ConfigurationPath, directory: false);
        using Stream stream = _basis.OpenRead(Project.ConfigurationPath);
        using MemoryStream copied = new();
        await stream.CopyToAsync(copied, cancellationToken);
        byte[] bytes = copied.ToArray();
        if (!bytes.AsSpan().SequenceEqual(written.Content.Span))
        {
            throw new InvalidDataException("Mandatory configuration differs from the writer's committed bytes. Created output was retained.");
        }
        ValidateConfiguration(bytes);
        _configuration = bytes;
    }

    internal ReadOnlyMemory<byte> ReadConfiguration()
        => _configuration is { } bytes ? bytes.ToArray()
            : throw new InvalidOperationException("Mandatory created configuration has not been captured.");

    internal WorkloadNativeTemplateCapturedOutput CreateView(NativeTemplatePolicyLifetime lifetime)
        => new(new(Owner.Candidate.CreateSource(lifetime), lifetime, WorkingDirectory.FromExplicit(_projectDirectory),
            _request with { OutputDirectory = Copy(_request.OutputDirectory) }, _binding), lifetime, Snapshot);

    internal bool Matches(TemplateInvocationRequest request)
        => SamePath(_request.OutputDirectory.Info.FullName, request.OutputDirectory.Info.FullName)
        && _request.Name == request.Name && _request.Force == request.Force && _request.MsBuildBindings == request.MsBuildBindings
        && _request.ExplicitParameterNames.SetEquals(request.ExplicitParameterNames)
        && _request.Parameters.Count == request.Parameters.Count
        && _request.Parameters.All(pair => request.Parameters.TryGetValue(pair.Key, out string? value) && value == pair.Value);

    internal async Task ValidateInstalledAsync(IFileSystem files, CancellationToken cancellationToken)
    {
        foreach (string directory in Snapshot.Directories)
        {
            RequireKind(Path.Combine(Project.Root, directory), directory: true);
        }
        foreach ((string relative, ReadOnlyMemory<byte> expected) in Snapshot.ExpandedFiles)
        {
            string path = Path.Combine(Project.Root, relative);
            RequireKind(path, directory: false);
            string actual = await files.ComputeSha256Async(path, cancellationToken);
            if (!actual.Equals(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(expected.Span)), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Created project bytes changed before workload preparation. Generated output was retained.");
            }
        }
        RequireKind(Project.ConfigurationPath, directory: false);
        byte[] expectedConfiguration = _configuration
            ?? throw new InvalidDataException("Preparation has no successful mandatory configuration receipt.");
        using Stream stream = _basis.OpenRead(Project.ConfigurationPath);
        using MemoryStream actualConfiguration = new();
        await stream.CopyToAsync(actualConfiguration, cancellationToken);
        if (!actualConfiguration.ToArray().AsSpan().SequenceEqual(expectedConfiguration))
        {
            throw new InvalidDataException("Mandatory CLI configuration changed after its successful creation receipt.");
        }
        ValidateConfiguration(expectedConfiguration);
    }

    private void ValidateConfiguration(ReadOnlyMemory<byte> bytes)
    {
        using var configuration = JsonDocument.Parse(bytes);
        if (!configuration.RootElement.TryGetProperty("stack", out JsonElement stack)
            || !stack.TryGetProperty("runtime", out JsonElement runtime) || runtime.ValueKind != JsonValueKind.String
            || !stack.TryGetProperty("language", out JsonElement language) || language.ValueKind != JsonValueKind.String
            || runtime.GetString() != Project.Stack || language.GetString() != Project.Language)
        {
            throw new InvalidDataException("Mandatory CLI configuration no longer matches the admitted project.");
        }
    }

    private void RequireKind(string path, bool directory)
    {
        FileAttributes attributes = _basis.GetFileAttributes(path);
        if (((attributes & FileAttributes.Directory) != 0) != directory)
        {
            throw new InvalidDataException("A created preparation input no longer has its captured file or directory kind.");
        }
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            if ((_basis.GetFileAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("Preparation requires an unlinked created project.");
            }
        }
    }

    internal static bool SamePath(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
        Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static WorkingDirectory Copy(WorkingDirectory directory)
        => new(new DirectoryInfo(directory.Info.FullName), directory.WasExplicit, directory.OriginalPath);
}

/// <summary>
/// Authorizes only one declared preparation journey and its exact loaded workload capability.
/// </summary>
internal sealed class BoundNativeProjectPreparation
{
    private readonly INativeTemplateProjectPreparation _capability;
    private readonly TemplateInvocationRequest _initial;
    private NativeProjectPreparationCapture? _preview;
    private NativeProjectPreparationCapture? _created;
    private NativeTemplateProjectPreparationPlan? _plan;
    private int _state;
    private int _executed;

    internal BoundNativeProjectPreparation(NativeTemplatePolicyCandidate candidate,
        INativeTemplateProjectPreparation capability, TemplateInvocationRequest request)
    {
        Candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
        _capability = capability ?? throw new ArgumentNullException(nameof(capability));
        ArgumentNullException.ThrowIfNull(request);
        _initial = request with
        {
            Parameters = new Dictionary<string, string?>(request.Parameters, StringComparer.Ordinal),
            ExplicitParameterNames = request.ExplicitParameterNames.ToFrozenSet(StringComparer.Ordinal),
        };
        TargetPath = request.OutputDirectory.Info.FullName;
        if (!candidate.RequiresPreparation
            || !ReferenceEquals(AssemblyLoadContext.GetLoadContext(capability.GetType().Assembly), candidate.Admission.Registration.Owner.LoadContext))
        {
            throw new InvalidDataException("Project preparation must be supplied by the actual loaded owning workload.");
        }
    }

    public NativeTemplatePolicyCandidate Candidate { get; }
    private string TargetPath { get; }
    public WorkingDirectory Target => WorkingDirectory.FromExplicit(TargetPath);
    public NativeTemplatePreparationSnapshot ExpectedCapture => _preview?.Snapshot
        ?? throw new InvalidOperationException("Workload preparation has no qualified native preview.");

    internal INativeTemplateProjectPreparation Capability => _capability;

    internal void ValidateRequest(NativeTemplatePolicyCandidate candidate, TemplateInvocationRequest request)
    {
        if (!ReferenceEquals(candidate, Candidate) || !NativeProjectPreparationCapture.SamePath(request.OutputDirectory.Info.FullName, TargetPath)
            || request.Name != _initial.Name || request.Force != _initial.Force
            || !_initial.ExplicitParameterNames.SetEquals(request.ExplicitParameterNames)
            || _initial.ExplicitParameterNames.Any(key => !_initial.Parameters.TryGetValue(key, out string? initial)
                || !request.Parameters.TryGetValue(key, out string? current) || current != initial)
            || (!request.DryRun && (_state != 1 || _preview is null || !_preview.Matches(request)
                || !ReferenceEquals(request.ExpectedPreparation, _preview.Snapshot)))
            || (request.DryRun && (_state != 0 || _preview is not null)))
        {
            throw new InvalidDataException("Required preparation does not match this exact owner, invocation or frozen preview.");
        }
    }

    internal NativeProjectPreparationCapture Capture(TemplateInvocationRequest request, NativeTemplatePreparationSnapshot snapshot,
        NativeTemplateProjectBinding? binding, WorkingDirectory projectDirectory,
        IReadOnlyList<TemplateProjectConfigurationPlan> projects, IPhysicalFileSystem basis)
    {
        if (projects.Count != 1 || !NativeProjectPreparationCapture.SamePath(projects[0].Root, TargetPath)
            || !string.Equals(projects[0].Stack, Candidate.Admission.Registration.Policy.Stack, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Workload preparation requires the actual owning canonical project in its exact invocation root.");
        }
        ValidateRequest(Candidate, request);
        NativeProjectPreparationCapture capture = new(this, request, snapshot, binding, projectDirectory, projects[0], basis);
        if (request.DryRun)
        {
            _preview = capture;
        }
        else
        {
            if (Interlocked.CompareExchange(ref _state, 2, 1) != 1)
            {
                throw new InvalidDataException("This preparation journey already produced a creation receipt.");
            }
            _created = capture;
        }
        return capture;
    }

    internal NativeTemplateProjectPreparationPlan Freeze(NativeProjectPreparationCapture capture, NativeTemplateProjectPreparationPlan plan)
    {
        if (!ReferenceEquals(capture, _preview) || Interlocked.CompareExchange(ref _state, 1, 0) != 0)
        {
            throw new InvalidDataException("Preparation preview does not belong to this invocation.");
        }
        _plan = new(plan.Succeeded, plan.Skipped, plan.Diagnostics);
        return _plan;
    }

    internal NativeTemplateProjectPreparationPlan BeginCompletion(NativeProjectPreparationContext context, NativeTemplateProjectPreparationPlan plan)
    {
        if (_plan is null || !ReferenceEquals(plan, _plan)
            || context.Created is not { Status: TemplateInvocationStatus.Success, HasCommittedOutput: true } created
            || !ReferenceEquals(created.ProjectPreparationCapture, _created) || _created is null
            || !ReferenceEquals(context.NativePreview.ProjectPreparationCapture, _preview)
            || created.ConfiguredProjects.Count != 1 || !NativeProjectPreparationCapture.SamePath(created.ConfiguredProjects[0], TargetPath)
            || !NativeProjectPreparationCapture.SamePath(context.TargetDirectory.Info.FullName, TargetPath)
            || Interlocked.CompareExchange(ref _state, 3, 2) != 2)
        {
            throw new InvalidDataException("Workload preparation requires its one successful native and mandatory configuration creation.");
        }
        return _plan;
    }

    internal Task<NativeTemplateProjectPreparationResult> ExecuteAsync(
        NativeTemplateCreatedProject created, NativeTemplateProjectPreparationPlan plan, CancellationToken cancellationToken)
    {
        if (_state != 3 || !ReferenceEquals(plan, _plan) || _plan is not { Succeeded: true }
            || Interlocked.CompareExchange(ref _executed, 1, 0) != 0)
        {
            throw new InvalidOperationException("The workload capability has no one-use completion admission.");
        }
        return _capability.CompleteAsync(created, plan, cancellationToken);
    }
}

internal sealed class WorkloadNativeCreatedProject(
    NativeProjectPreparationCapture capture, NativeTemplatePolicyLifetime lifetime, IFileSystem files) : NativeTemplateCreatedProject
{
    private readonly NativeProjectPreparationCapture _capture = capture ?? throw new ArgumentNullException(nameof(capture));
    private readonly NativeTemplatePolicyLifetime _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
    private readonly IFileSystem _files = files ?? throw new ArgumentNullException(nameof(files));

    public override NativeTemplateCapturedOutput Output { get; } = capture.CreateView(lifetime);
    public override string ConfigurationPath => _capture.Project.ConfigurationPath;

    public override Task<ReadOnlyMemory<byte>> ReadConfigurationAsync(CancellationToken cancellationToken)
    {
        _lifetime.ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_capture.ReadConfiguration());
    }

    public override async Task RevalidateAsync(CancellationToken cancellationToken)
    {
        _lifetime.ThrowIfDisposed();
        await Output.RevalidateAsync(cancellationToken);
        await _capture.ValidateInstalledAsync(_files, cancellationToken);
        _lifetime.ThrowIfDisposed();
    }
}
