// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Inputs to project resolution.
/// </summary>
/// <param name="WorkingDirectory">The directory the command is operating on.</param>
/// <param name="WorkerVersionRanges">Profile worker version ranges keyed by Functions runtime name.</param>
internal sealed record ProjectResolutionContext(
    WorkingDirectory WorkingDirectory,
    IReadOnlyDictionary<string, VersionRange> WorkerVersionRanges)
{
    public ProjectResolutionContext(WorkingDirectory workingDirectory)
        : this(workingDirectory, new Dictionary<string, VersionRange>(StringComparer.OrdinalIgnoreCase))
    {
    }
}
