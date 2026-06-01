// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography.X509Certificates;

namespace Azure.Functions.Cli.Workloads.Install.Trust;

/// <summary>
/// Source of trust anchors (typically root CAs) used to validate
/// workload package signatures. A package is considered trusted when
/// its signing certificate chains to one of these anchors under
/// <see cref="X509ChainTrustMode.CustomRootTrust"/>.
/// </summary>
internal interface ITrustAnchorStore
{
    /// <summary>
    /// Returns the trust anchors shipped with the CLI. The collection is
    /// loaded once and cached for the process lifetime; callers must not
    /// mutate it.
    /// </summary>
    public X509Certificate2Collection GetTrustAnchors();
}
