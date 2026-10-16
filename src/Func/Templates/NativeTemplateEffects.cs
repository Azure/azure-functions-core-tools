// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Templates;

internal sealed record TemplateEffectSource(string TemplateIdentity, string Identity, string Sha256);

internal abstract record TemplateEffect(TemplateEffectSource Source, string TargetPath, bool ContinueOnError = false);

internal sealed record TemplateAppendFileEffect(
    TemplateEffectSource Source, string TargetPath, string Content, string ExpectedTargetSha256) : TemplateEffect(Source, TargetPath);

internal sealed record TemplateCreateFileEffect(
    TemplateEffectSource Source, string TargetPath, string Content) : TemplateEffect(Source, TargetPath);

internal sealed record TemplatePackageReferenceEffect(TemplateEffectSource Source, string ProjectPath,
    string PackageId, string Version, string ProjectFileExtension, bool ContinueOnError)
    : TemplateEffect(Source, ProjectPath, ContinueOnError);

internal sealed record TemplateRestoreEffect(TemplateEffectSource Source, string ProjectPath, bool ContinueOnError)
    : TemplateEffect(Source, ProjectPath, ContinueOnError);

internal sealed record TemplateEditorNoOpEffect(TemplateEffectSource Source) : TemplateEffect(Source, string.Empty);

/// <summary>
/// Holds a copied, ordered internal effect plan; it grants no production execution permission.
/// </summary>
internal sealed class NativeTemplateEffectPlan(WorkingDirectory outputDirectory, IReadOnlyList<TemplateEffect> effects)
{
    public WorkingDirectory OutputDirectory { get; } = outputDirectory ?? throw new ArgumentNullException(nameof(outputDirectory));

    public IReadOnlyList<TemplateEffect> Effects { get; } =
        Array.AsReadOnly<TemplateEffect>([.. effects ?? throw new ArgumentNullException(nameof(effects))]);
}

internal sealed record NativeTemplateEffectPlanningResult(NativeTemplateEffectPlan? Plan, IReadOnlyList<string> Diagnostics);

internal sealed record TemplateEffectExecutionResult(
    bool Succeeded, IReadOnlyList<string> Diagnostics, bool TargetMayHaveChanged = false);

internal sealed record NativeTemplateEffectResult(
    string Kind,
    bool Succeeded,
    NativeTemplateEffectPlan Plan,
    IReadOnlyList<TemplateEffect> CompletedEffects,
    IReadOnlyList<TemplateEffect> FailedEffects,
    IReadOnlyList<string> Diagnostics);

/// <summary>
/// Supplies explicitly composed, workload-owned preflight and execution; no default implementation is registered.
/// </summary>
internal interface INativeTemplateEffectCapability
{
    /// <summary>
    /// Rejects every unsupported effect and validates selected source hashes, target snapshots, paths and occupancy without mutation.
    /// </summary>
    public Task<IReadOnlyList<string>> ValidateAsync(NativeTemplateEffectPlan plan, CancellationToken cancellationToken);

    /// <summary>
    /// Applies one validated effect, rechecking its source and target before mutation and reporting expected partial failures.
    /// </summary>
    public Task<TemplateEffectExecutionResult> ExecuteAsync(TemplateEffect effect, CancellationToken cancellationToken);
}
