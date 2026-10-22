// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Templates;

internal enum NativeProjectPreparationOperation
{
    NodeNpmInstall,
    GoModTidy,
    PowerShellManagedDependencies,
}

internal sealed record NativeProjectPreparationStep(NativeProjectPreparationOperation Operation, bool Skipped);

internal sealed record NativeProjectPreparationContext(ResolvedTemplate SelectedTemplate, WorkingDirectory TargetDirectory,
    IReadOnlyDictionary<string, string?> Parameters, TemplateInvocationResult NativePreview);

internal sealed record NativeProjectPreparationPreview(bool Succeeded, IReadOnlyList<NativeProjectPreparationStep> Operations,
    IReadOnlyList<string> Diagnostics, NativeTemplateEffectPlan? FileEffects = null);

internal sealed record NativeProjectPreparationResult(
    bool Succeeded, IReadOnlyList<string> Diagnostics, NativeTemplateEffectResult? Effects = null);

internal sealed record NativeProjectPreparationOutcome(
    NativeProjectPreparationPreview Preview, NativeProjectPreparationResult? Completion = null);

/// <summary>
/// Plans and completes fixed, source-qualified stack preparation without template-authored executable commands.
/// </summary>
internal interface INativeProjectPreparation
{
    /// <summary>
    /// Reads owned source and plans known operations without modifying the live project.
    /// </summary>
    public Task<NativeProjectPreparationPreview> PreviewAsync(NativeProjectPreparationContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Runs owned helpers only after native creation and mandatory configuration complete, preserving skip and failure diagnostics.
    /// </summary>
    public Task<NativeProjectPreparationResult> CompleteAsync(
        NativeProjectPreparationContext context, NativeProjectPreparationPreview preview, CancellationToken cancellationToken);
}
