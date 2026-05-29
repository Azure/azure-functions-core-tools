// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Hosting.FirstRun;

/// <summary>
/// Lifecycle states for the first-run experience. Computed from the marker
/// file plus the installed workload registry.
/// </summary>
internal enum FirstRunState
{
    /// <summary>
    /// No marker on disk and no installed workloads. The user should see
    /// the first-run prompt.
    /// </summary>
    NeverPrompted = 0,

    /// <summary>
    /// Marker is present but no workloads are installed. Either the user
    /// declined first-run setup, or they ran setup with an empty selection.
    /// The user should see the muted breadcrumb hint on each command.
    /// </summary>
    MarkerWithoutWorkloads = 1,

    /// <summary>
    /// At least one workload is installed. The CLI is set up; no first-run
    /// affordance is needed.
    /// </summary>
    WorkloadsInstalled = 2,
}
