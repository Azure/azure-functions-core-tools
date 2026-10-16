// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Components;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Captures evaluated project properties and the exact selected source for controlled native binding.
/// </summary>
internal sealed record NativeMsBuildBindingSnapshot
{
    public NativeMsBuildBindingSnapshot(string projectFilePath, string templateIdentity, string sourceIdentity,
        string sourceFingerprint, string targetFramework, string rootNamespace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(templateIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFramework);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootNamespace);
        if (!Path.IsPathFullyQualified(projectFilePath) || Path.GetExtension(projectFilePath) is not (".csproj" or ".fsproj"))
        {
            throw new ArgumentException("Native MSBuild binding requires an exact absolute C# or F# project path.", nameof(projectFilePath));
        }

        if (sourceFingerprint.Length != 64 || !sourceFingerprint.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("Native MSBuild binding requires the selected source SHA-256.", nameof(sourceFingerprint));
        }

        ProjectFilePath = Path.GetFullPath(projectFilePath);
        TemplateIdentity = templateIdentity;
        SourceIdentity = sourceIdentity;
        SourceFingerprint = sourceFingerprint;
        TargetFramework = targetFramework;
        RootNamespace = rootNamespace;
    }

    public string ProjectFilePath { get; }

    public string TemplateIdentity { get; }

    public string SourceIdentity { get; }

    public string SourceFingerprint { get; }

    public string TargetFramework { get; }

    public string RootNamespace { get; }
}

/// <summary>
/// Binds only supplied immutable MSBuild results; it never evaluates or discovers a project.
/// </summary>
internal sealed class NativeMsBuildBindingSnapshotSource(NativeMsBuildBindingSnapshot snapshot) : IBindSymbolSource
{
    private readonly NativeMsBuildBindingSnapshot _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));

    public string DisplayName => "Selected MSBuild project snapshot";

    public string SourcePrefix => "msbuild";

    public int Priority => 200;

    public Guid Id => new("e534959b-4497-4a81-b9ef-430ee0a59237");

    public bool RequiresPrefixMatch => true;

    public Task<string?> GetBoundValueAsync(IEngineEnvironmentSettings settings, string bindname, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(bindname);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(bindname switch
        {
            "TargetFramework" => _snapshot.TargetFramework,
            "RootNamespace" => _snapshot.RootNamespace,
            _ => null,
        });
    }
}
