// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Native;

/// <summary>
/// Plans an owning workload's preparation without executing it.
/// </summary>
public sealed record NativeTemplateProjectPreparationPlan(bool Succeeded, bool Skipped, IReadOnlyList<string> Diagnostics)
{
    public IReadOnlyList<string> Diagnostics { get; } =
        Array.AsReadOnly<string>([.. Diagnostics ?? throw new ArgumentNullException(nameof(Diagnostics))]);
}

/// <summary>
/// Preserves the workload's returned preparation outcome independently of native output commits.
/// </summary>
public sealed record NativeTemplateProjectPreparationResult(
    bool Succeeded, IReadOnlyList<string> Diagnostics, bool TargetMayHaveChanged = false)
{
    public IReadOnlyList<string> Diagnostics { get; } =
        Array.AsReadOnly<string>([.. Diagnostics ?? throw new ArgumentNullException(nameof(Diagnostics))]);
}

/// <summary>
/// Exposes successful created output and copied mandatory configuration within the completion lifetime.
/// </summary>
public abstract class NativeTemplateCreatedProject
{
    public abstract NativeTemplateCapturedOutput Output { get; }

    public abstract string ConfigurationPath { get; }

    public abstract Task<ReadOnlyMemory<byte>> ReadConfigurationAsync(CancellationToken cancellationToken);

    public abstract Task RevalidateAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Plans and completes only the owning workload's project preparation.
/// </summary>
public interface INativeTemplateProjectPreparation
{
    public Task<NativeTemplateProjectPreparationPlan> PreviewAsync(NativeTemplateCapturedOutput captured, CancellationToken cancellationToken);

    public Task<NativeTemplateProjectPreparationResult> CompleteAsync(
        NativeTemplateCreatedProject created, NativeTemplateProjectPreparationPlan preview, CancellationToken cancellationToken);
}
