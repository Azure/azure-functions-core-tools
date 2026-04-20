// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Manages host runtime installations — install, remove, list, and query available versions.
/// </summary>
public interface IHostManager
{
    /// <summary>
    /// Returns a list of host versions available for download from NuGet.
    /// </summary>
    public Task<IReadOnlyList<string>> GetAvailableVersionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a list of locally installed host versions with metadata.
    /// </summary>
    public IReadOnlyList<InstalledHostVersion> GetInstalledVersions();

    /// <summary>
    /// Downloads and installs a host version from NuGet.
    /// Returns the installation path, or null if installation failed.
    /// </summary>
    public Task<string?> InstallAsync(string version, IProgress<HostInstallProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes an installed host version.
    /// </summary>
    public bool Remove(string version);

    /// <summary>
    /// Gets the currently configured default host version, or null.
    /// </summary>
    public string? GetDefaultVersion();

    /// <summary>
    /// Sets the default host version.
    /// </summary>
    public void SetDefaultVersion(string version);
}

/// <summary>
/// Information about a locally installed host version.
/// </summary>
/// <param name="Version">The version string.</param>
/// <param name="Path">Full path to the host DLL.</param>
/// <param name="IsDefault">Whether this is the configured default version.</param>
/// <param name="IsKnownGood">Whether this version is in the CLI's tested versions list.</param>
public record InstalledHostVersion(string Version, string Path, bool IsDefault, bool IsKnownGood);

/// <summary>
/// Progress information for host installation.
/// </summary>
/// <param name="Phase">Current phase (downloading, extracting, verifying).</param>
/// <param name="Percentage">Progress percentage (0-100), or null if indeterminate.</param>
public record HostInstallProgress(string Phase, int? Percentage);
