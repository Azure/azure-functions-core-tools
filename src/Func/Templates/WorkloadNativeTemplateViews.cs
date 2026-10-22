// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Native;
using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates;

internal sealed class NativeTemplatePolicyLifetime : IDisposable
{
    private bool _disposed;

    public void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose() => _disposed = true;
}

internal sealed class WorkloadNativeTemplateSource : NativeTemplateSource
{
    private readonly NativeTemplatePolicyLifetime _lifetime;
    private readonly Func<string, CancellationToken, Task<ReadOnlyMemory<byte>>> _read;
    private readonly Func<CancellationToken, Task<TemplateInvocationResult?>> _revalidate;
    private readonly BoundNativeTemplatePolicy _admission;

    public WorkloadNativeTemplateSource(TemplateCatalogEntry entry, string configurationSha256,
        NativeTemplatePolicyLifetime lifetime, Func<string, CancellationToken, Task<ReadOnlyMemory<byte>>> read,
        Func<CancellationToken, Task<TemplateInvocationResult?>> revalidate, BoundNativeTemplatePolicy admission)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationSha256);
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        _read = read ?? throw new ArgumentNullException(nameof(read));
        _revalidate = revalidate ?? throw new ArgumentNullException(nameof(revalidate));
        _admission = admission ?? throw new ArgumentNullException(nameof(admission));
        Identity = entry.Identity;
        DisplayName = entry.Name;
        IsProject = entry.Type == TemplateType.Project;
        Language = entry.Language;
        ShortNames = Array.AsReadOnly<string>([.. entry.ShortNames]);
        IsHidden = entry.IsHidden;
        InternalRole = entry.DeclaredInternalRole;
        DefaultName = entry.DefaultName;
        PackageId = entry.Package?.Identifier ?? throw new InvalidDataException("The selected source has no package identity.");
        PackageVersion = entry.Package.Version ?? throw new InvalidDataException("The selected source has no package version.");
        ConfigurationSha256 = configurationSha256;
        CanonicalParameters = Array.AsReadOnly<TemplateUserPrompt>([.. entry.Parameters.Select(parameter => new TemplateUserPrompt(
            parameter.CanonicalName, parameter.Description, parameter.DataType, parameter.DefaultValue,
            Array.AsReadOnly<string>([.. parameter.Choices.Select(choice => choice.Value)]), parameter.IsRequired,
            null, parameter.ShortName, parameter.LongName))]);
    }

    public override string Identity { get; }
    public override string DisplayName { get; }
    public override bool IsProject { get; }
    public override string? Language { get; }
    public override IReadOnlyList<string> ShortNames { get; }
    public override bool IsHidden { get; }
    public override string? InternalRole { get; }
    public override string? DefaultName { get; }
    public override string PackageId { get; }
    public override string PackageVersion { get; }
    public override string ConfigurationSha256 { get; }
    public override IReadOnlyList<TemplateUserPrompt> CanonicalParameters { get; }

    public override async Task<ReadOnlyMemory<byte>> ReadPackageFileAsync(string relativePath, CancellationToken cancellationToken)
    {
        _lifetime.ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathRooted(relativePath) || relativePath.Contains('\\')
            || relativePath.Split('/').Any(part => part is "" or "." or ".."))
        {
            throw new ArgumentException("Supply an exact package-relative source path without traversal.", nameof(relativePath));
        }
        cancellationToken.ThrowIfCancellationRequested();
        ReadOnlyMemory<byte> bytes = await _read(relativePath, cancellationToken);
        _lifetime.ThrowIfDisposed();
        return bytes.ToArray();
    }

    public override async Task RevalidateAsync(CancellationToken cancellationToken)
    {
        _lifetime.ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        TemplateInvocationResult? changed = await _revalidate(cancellationToken);
        if (changed is not null)
        {
            throw new InvalidDataException(changed.Diagnostic ?? "The selected native configuration changed. Rerun selection.");
        }
        await _admission.RevalidateAsync(cancellationToken);
        _lifetime.ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
    }
}

internal sealed class WorkloadNativeTemplateInvocation(
    WorkloadNativeTemplateSource source, NativeTemplatePolicyLifetime lifetime, WorkingDirectory projectDirectory,
    TemplateInvocationRequest request, NativeTemplateProjectBinding? binding = null) : NativeTemplateInvocationContext
{
    private readonly NativeTemplatePolicyLifetime _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));

    public override NativeTemplateSource Source { get; } = source ?? throw new ArgumentNullException(nameof(source));
    public override WorkingDirectory ProjectDirectory { get; } = projectDirectory ?? throw new ArgumentNullException(nameof(projectDirectory));
    public override WorkingDirectory OutputDirectory { get; } = (request ?? throw new ArgumentNullException(nameof(request))).OutputDirectory;
    public override string? Name { get; } = request.Name;
    public override bool DryRun { get; } = request.DryRun;
    public override bool Force { get; } = request.Force;
    public override IReadOnlyDictionary<string, string?> CanonicalValues { get; } =
        new ReadOnlyDictionary<string, string?>(new Dictionary<string, string?>(request.Parameters, StringComparer.Ordinal));
    public override IReadOnlySet<string> ExplicitParameterNames { get; } = request.ExplicitParameterNames.ToFrozenSet(StringComparer.Ordinal);
    public override NativeTemplateProjectBinding? ProjectBinding { get; } = binding is null
        ? null : new(binding.ProjectPath, binding.TargetFramework, binding.RootNamespace);

    public override Task RevalidateAsync(CancellationToken cancellationToken)
    {
        _lifetime.ThrowIfDisposed();
        return Source.RevalidateAsync(cancellationToken);
    }
}

internal sealed class WorkloadNativeTemplateCapturedOutput : NativeTemplateCapturedOutput
{
    private readonly NativeTemplatePolicyLifetime _lifetime;
    private readonly NativeTemplatePreparationSnapshot _snapshot;

    public WorkloadNativeTemplateCapturedOutput(WorkloadNativeTemplateInvocation invocation,
        NativeTemplatePolicyLifetime lifetime, NativeTemplatePreparationSnapshot snapshot)
    {
        Invocation = invocation ?? throw new ArgumentNullException(nameof(invocation));
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> bytes = snapshot.ExpandedFiles;
        Files = Array.AsReadOnly<NativeTemplateCapturedFile>([.. bytes.Select(file =>
        {
            IFileChange2 mapping = snapshot.Effects.FileChanges.OfType<IFileChange2>()
                .Single(change => Normalize(change.TargetRelativePath) == Normalize(file.Key));
            NativeTemplateOutputSource source = snapshot.OutputSources.Single(source =>
                source.SourceRelativePath == NativeTemplateSourceSnapshot.Normalize(mapping.SourceRelativePath));
            return new NativeTemplateCapturedFile(file.Key, mapping.SourceRelativePath,
                new(source.Source.TemplateIdentity, source.Source.Identity, source.Source.Sha256),
                Convert.ToHexString(SHA256.HashData(file.Value.Span)));
        })]);
        Directories = Array.AsReadOnly<string>([.. snapshot.Directories]);
        PrimaryOutputs = Array.AsReadOnly<string>([.. snapshot.Effects.CreationResult.PrimaryOutputs.Select(output => output.Path)]);
    }

    public override NativeTemplateInvocationContext Invocation { get; }
    public override IReadOnlyList<NativeTemplateCapturedFile> Files { get; }
    public override IReadOnlyList<string> Directories { get; }
    public override IReadOnlyList<string> PrimaryOutputs { get; }

    public override Task<ReadOnlyMemory<byte>> ReadFileAsync(string targetRelativePath, CancellationToken cancellationToken)
    {
        _lifetime.ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRelativePath);
        if (Path.IsPathRooted(targetRelativePath) || targetRelativePath.Split('/', '\\').Any(part => part is "" or "." or ".."))
        {
            throw new ArgumentException("Supply an exact captured relative file path.", nameof(targetRelativePath));
        }
        if (!_snapshot.ExpandedFiles.TryGetValue(targetRelativePath, out ReadOnlyMemory<byte> bytes))
        {
            throw new FileNotFoundException("The current native capture has no such regular file.", targetRelativePath);
        }
        return Task.FromResult<ReadOnlyMemory<byte>>(bytes.ToArray());
    }

    public override Task RevalidateAsync(CancellationToken cancellationToken)
    {
        _lifetime.ThrowIfDisposed();
        return Invocation.RevalidateAsync(cancellationToken);
    }

    private static string Normalize(string path) => NativeTemplateSourceSnapshot.Normalize(path.Replace('\\', '/'));
}
