// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Templates.Native;

/// <summary>
/// Carries one host-planned, source-bound append without granting other effect permissions.
/// </summary>
public sealed record NativeTemplateAppendRequest(
    WorkingDirectory PlanOutputDirectory,
    NativeTemplateSourceStamp Source,
    string TargetPath,
    string GeneratedSnippet,
    string ExpectedTargetSha256);

/// <summary>
/// Reports append completion and whether a failed operation may have modified its target.
/// </summary>
public sealed record NativeTemplateAppendResult(bool Succeeded, IReadOnlyList<string> Diagnostics, bool TargetMayHaveChanged = false);

/// <summary>
/// Validates and applies one append through the owning workload, preserving cancellation and partial failures.
/// </summary>
public interface INativeTemplateAppendCapability
{
    public Task<IReadOnlyList<string>> ValidateAsync(NativeTemplateAppendRequest request, CancellationToken cancellationToken);

    public Task<NativeTemplateAppendResult> ExecuteAsync(NativeTemplateAppendRequest request, CancellationToken cancellationToken);
}
