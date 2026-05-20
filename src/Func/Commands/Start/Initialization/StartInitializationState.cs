// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Hosting.Dashboard;
using Azure.Functions.Cli.Hosting.Events;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Mutable state produced while startup initialization steps run.
/// </summary>
internal sealed class StartInitializationState
{
    public string ProfileName { get; set; } = "none";

    public string? HostVersion { get; set; }

    public IFunctionsProject? Project { get; set; }

    public string? BundleVersion { get; set; }

    public IHostEventStream? EventStream { get; set; }

    public StartInitializationResult ToResult(StartInitializationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string hostVersion = HostVersion ?? throw new InvalidOperationException("Host version was not resolved.");
        IFunctionsProject project = Project ?? throw new InvalidOperationException("Functions project was not resolved.");
        IHostEventStream eventStream = EventStream ?? throw new InvalidOperationException("Host event stream was not initialized.");

        var runInfo = new DashboardRunInfo(context.CliVersion, ProfileName, project.StackDisplayName);

        return new StartInitializationResult(
            runInfo,
            eventStream,
            hostVersion,
            project.SupportsExtensionBundles,
            BundleVersion);
    }
}
