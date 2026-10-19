// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates.Engine;

internal sealed record TemplateInvocationRequest(WorkingDirectory OutputDirectory, IReadOnlyDictionary<string, string?> Parameters, string? Name = null, bool DryRun = false, bool Force = false)
{
    public NativeTemplateInvocationEffects? EffectSupport { get; init; }

    public NativeMsBuildBindingSnapshot? MsBuildBindings { get; init; }

    public NativeTemplateSourceSnapshot? SourceSnapshot { get; init; }

    public bool PreserveExistingFiles { get; init; }

    public bool PreviewAfterDirectoryCleanup { get; init; }
}

internal sealed record NativeTemplateInvocationEffects(
    WorkingDirectory ProjectDirectory, string ExactProjectPath, INativeTemplateEffectCapability Capability, bool Confirmed)
{
    public TemplateFSharpCompileEffect? FSharpRepair { get; init; }
}

internal enum TemplateInvocationStatus
{
    Success,
    Failed,
    InvalidArguments,
    MissingRequiredParameters,
    FileConflict,
    TemplateChanged,
    TemplateUnavailable,
    InvalidTemplate,
    EngineFailure,
    ConfigurationFailure,
    EffectFailure,
}

internal enum TemplateFileChangeKind
{
    Create,
    Change,
    Overwrite,
    Delete,
    Append,
    Preserve,
}

internal sealed record TemplateFileChange(string Path, TemplateFileChangeKind Kind)
{
    public static TemplateFileChange FromNative(string path, ChangeKind kind) => new(path, kind switch
    {
        ChangeKind.Create => TemplateFileChangeKind.Create,
        ChangeKind.Change => TemplateFileChangeKind.Change,
        ChangeKind.Overwrite => TemplateFileChangeKind.Overwrite,
        ChangeKind.Delete => TemplateFileChangeKind.Delete,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The template engine returned an unsupported file-change kind."),
    });

    public string GetKindName() => Kind switch
    {
        TemplateFileChangeKind.Create => "Create",
        TemplateFileChangeKind.Change => "Change",
        TemplateFileChangeKind.Overwrite => "Overwrite",
        TemplateFileChangeKind.Delete => "Delete",
        TemplateFileChangeKind.Append => "Append",
        TemplateFileChangeKind.Preserve => "Preserve",
        _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "Unsupported internal template file-change kind."),
    };
}

internal sealed record TemplateInvocationResult(TemplateInvocationStatus Status, IReadOnlyList<TemplateFileChange> Changes, string? Diagnostic = null)
{
    public IReadOnlyList<TemplateProjectConfigurationPlan> ProjectConfigurations { get; init; } = [];

    public IReadOnlyList<string> ConfiguredProjects { get; init; } = [];

    public NativeTemplateEffectResult? Effects { get; init; }
}

internal sealed record TemplateConfigurationSnapshot(string Content, string Fingerprint, TemplateType? Type, IReadOnlyList<ITemplateParameter> Parameters)
{
    public NativeTemplateGenerationContract? NativeGeneration { get; init; }

    public string? DefaultName { get; init; }

    public string? HostConfigPlace { get; init; }

    public string? HostData { get; init; }
}