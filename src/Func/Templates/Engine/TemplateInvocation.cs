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

internal sealed record TemplateFileChange(string Path, string Kind);

internal sealed record TemplateInvocationResult(TemplateInvocationStatus Status, IReadOnlyList<TemplateFileChange> Changes, string? Diagnostic = null)
{
    public IReadOnlyList<TemplateProjectConfigurationPlan> ProjectConfigurations { get; init; } = [];

    public IReadOnlyList<string> ConfiguredProjects { get; init; } = [];

    public NativeTemplateEffectResult? Effects { get; init; }
}

internal sealed record TemplateConfigurationSnapshot(string Content, string Fingerprint, TemplateType? Type, IReadOnlyList<ITemplateParameter> Parameters)
{
    public string? DefaultName { get; init; }

    public string? HostConfigPlace { get; init; }

    public string? HostData { get; init; }
}