// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Bundles;

/// <summary>
/// Reads extension bundle declarations from project host.json files.
/// </summary>
internal interface IHostJsonBundleSectionReader
{
    /// <summary>
    /// Reads the extension bundle declaration, or <c>null</c> when host.json does not declare one.
    /// </summary>
    /// <exception cref="ExtensionBundleConfigurationException">
    /// The host.json file could not be read or contains an invalid extension bundle declaration.
    /// </exception>
    public Task<HostJsonBundleSection?> ReadAsync(DirectoryInfo projectDirectory, CancellationToken cancellationToken);
}
