// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Hosting.Dashboard;
using Azure.Functions.Cli.Hosting.Events;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Resolved initialization output used to start the dashboard pipeline.
/// </summary>
internal sealed record StartInitializationResult(
    DashboardRunInfo RunInfo,
    IHostEventStream EventStream,
    string HostVersion,
    bool BundleRequired,
    string? BundleVersion,
    FunctionsProject Project,
    FunctionsProjectHostRunContext HostRunContext,
    StartInitializationProfileInfo? Profile = null);
