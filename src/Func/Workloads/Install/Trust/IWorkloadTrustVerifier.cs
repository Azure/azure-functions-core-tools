// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Install.Trust;

/// <summary>
/// Gates workload installs on publisher trust. The verifier looks at
/// both the package id (must live under the Azure Functions namespace)
/// and the <c>.nupkg</c>'s primary signature (must chain to a trust
/// anchor in the bundled <see cref="ITrustAnchorStore"/>). The
/// <c>--allow-untrusted</c> flag bypasses the gate for local dev
/// workflows.
/// </summary>
internal interface IWorkloadTrustVerifier
{
    /// <summary>
    /// Verifies that the package at <paramref name="nupkgPath"/> with id
    /// <paramref name="packageId"/> is allowed to install. When
    /// <paramref name="allowUntrusted"/> is <c>true</c> the check is
    /// skipped and a trusted result is returned.
    /// </summary>
    public Task<TrustVerificationResult> VerifyAsync(
        string nupkgPath,
        string packageId,
        bool allowUntrusted,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Outcome of a trust check. <see cref="IsTrusted"/> is <c>true</c> when
/// the package is allowed to install (either it passed verification or
/// the caller opted into <c>--allow-untrusted</c>);
/// <see cref="Reason"/> is populated on failure and is safe to surface
/// to the user.
/// </summary>
internal sealed record TrustVerificationResult(bool IsTrusted, string? Reason = null)
{
    public static TrustVerificationResult Trusted { get; } = new(true);

    public static TrustVerificationResult Untrusted(string reason) => new(false, reason);
}
