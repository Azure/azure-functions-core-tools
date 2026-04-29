// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Why the CLI is showing a workload-related hint to the user. Each kind
/// drives a distinct rendered message; commands describe the situation,
/// the renderer owns the wording.
/// </summary>
internal enum WorkloadHintKind
{
    /// <summary>No workloads are installed at all. The user needs to install one before they can proceed.</summary>
    NoWorkloadsInstalled,

    /// <summary>Workloads are installed, but none claim the user-supplied <c>--stack</c> value.</summary>
    NoMatchingStack,

    /// <summary>Multiple stacks are installed and the user did not specify <c>--stack</c>.</summary>
    AmbiguousStackChoice,
}

/// <summary>
/// Typed description of a workload-availability situation. Built by commands,
/// rendered by <see cref="IWorkloadHintRenderer"/>. Splitting the data from
/// the rendering keeps message wording consistent across commands and lets
/// tests assert on the situation without scraping captured output.
/// </summary>
/// <param name="Kind">Which situation is being described; controls layout.</param>
/// <param name="ActionDescription">What the user was trying to do (e.g. "initialize a project"). Used in the install prompt.</param>
/// <param name="RequestedStack">The <c>--stack</c> value the user passed, when applicable.</param>
/// <param name="InstalledStacks">Stack identifiers currently registered, when applicable.</param>
internal sealed record WorkloadHint(
    WorkloadHintKind Kind,
    string ActionDescription,
    string? RequestedStack,
    IReadOnlyList<string> InstalledStacks);
