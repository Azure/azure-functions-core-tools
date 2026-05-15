// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Resolution;

/// <summary>
/// Inputs to <see cref="IWorkloadResolver.ResolveAsync"/>.
/// </summary>
/// <param name="Directory">The directory the command is operating on.</param>
/// <param name="StackSelector">Value of <c>--stack</c>, or <c>null</c> when the user did not supply one.</param>
/// <param name="SkipDirectoryDetection">
/// When <c>true</c>, the resolver does not invoke resolvers (used by
/// <c>func init</c>, where there is no project to inspect yet).
/// </param>
internal sealed record WorkloadResolutionContext(
    DirectoryInfo Directory,
    string? StackSelector,
    bool SkipDirectoryDetection = false);
