// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Resolves the Azure Functions host runtime to use.
/// </summary>
public interface IHostResolver
{
    /// <summary>
    /// Resolves the host DLL path. Returns null if no host is found.
    /// </summary>
    /// <param name="scriptRoot">The function app directory.</param>
    /// <param name="requestedVersion">Explicit version from --host-version, or null.</param>
    public HostResolution? Resolve(string scriptRoot, string? requestedVersion);

    /// <summary>
    /// Returns a list of installed host versions.
    /// </summary>
    public IReadOnlyList<string> GetInstalledVersions();
}

/// <summary>
/// The result of resolving a host runtime.
/// </summary>
/// <param name="HostPath">Full path to the host DLL.</param>
/// <param name="Version">The resolved host version, if known.</param>
/// <param name="Source">Human-readable description of where the host was found.</param>
public record HostResolution(string HostPath, string? Version, string Source);
