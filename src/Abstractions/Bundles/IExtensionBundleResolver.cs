// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Bundles;

/// <summary>
/// Resolves a project's extension bundle from the workload registry. Pure: no network I/O.
/// </summary>
public interface IExtensionBundleResolver
{
    public Task<ExtensionBundleResolution> ResolveAsync(
        ExtensionBundleProjectContext context,
        CancellationToken cancellationToken = default);
}
