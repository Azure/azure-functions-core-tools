// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Common;

/// <summary>
/// Exposes the running CLI's version metadata. Behind an interface so
/// commands that surface the version (e.g. <c>version</c>, <c>help</c>) can
/// be tested without depending on the executing assembly's attributes.
/// </summary>
internal interface ICliVersionProvider
{
    /// <summary>
    /// Public-facing semantic version (e.g. <c>"5.0.0"</c>), with any build
    /// metadata stripped.
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// Full informational version including build metadata
    /// (e.g. <c>"5.0.0+abc1234"</c>).
    /// </summary>
    public string InformationalVersion { get; }

    /// <summary>
    /// Indicates if the version is a prerelease.
    /// </summary>
    public bool IsPrerelease { get; }
}
