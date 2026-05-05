// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Install;

/// <summary>
/// Parses the metadata block of a NuGet <c>.nuspec</c> XML file into a
/// <see cref="NuspecMetadata"/>. Behind an interface so the install pipeline
/// can be tested without producing real .nuspec files (and so a future
/// NuGet-package-aware reader can plug in).
/// </summary>
internal interface INuspecReader
{
    /// <summary>
    /// Reads <paramref name="nuspecPath"/> and projects its metadata.
    /// </summary>
    /// <exception cref="Common.GracefulException">
    /// The file is missing, malformed, or missing required metadata
    /// elements (<c>id</c>, <c>version</c>).
    /// </exception>
    public NuspecMetadata Read(string nuspecPath);
}
