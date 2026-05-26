// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads.Install;

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Validates that the requested host workload is available.
/// </summary>
internal sealed class ValidateHostWorkloadInitializationStep(IHostWorkloadResolver resolver, IWorkloadInstaller installer)
    : DemoInitializationStep
{
    public const string StepId = "resolve_host_workload";

    private readonly IHostWorkloadResolver _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    private readonly IWorkloadInstaller _installer = installer ?? throw new ArgumentNullException(nameof(installer));

    public override string Id => StepId;

    public override string Title => "Validate host version";

    public override async Task<StartInitializationStepResult> ExecuteAsync(
        StartInitializationStepContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        await SimulateWorkAsync(context, cancellationToken);

        HostWorkloadResolution resolution;
        try
        {
            var hostWorkloadResolutionContext = new HostWorkloadResolutionContext(
                context.Options.RequestedHostVersion,
                context.State.ResolvedProfile?.HostVersionRange,
                context.Options.Offline);

            resolution = await _resolver.ResolveAsync(hostWorkloadResolutionContext, cancellationToken);
        }
        catch (HostWorkloadResolutionException ex)
        {
            throw new GracefulException(ex.Message, isUserError: true, verboseMessage: ex.ToString());
        }

        context.State.HostVersion = resolution.HostVersion;

        switch (resolution)
        {
            case HostWorkloadResolution.Installed installed:
                string explicitText = installed.ExplicitlyRequested ? "requested " : string.Empty;
                return StartInitializationStepResult.Completed($"Using {explicitText}host {installed.HostVersion}");

            case HostWorkloadResolution.InstallRequired installRequired:
                if (context.Options.Offline)
                {
                    string message = $"{installRequired.Message}, and --offline prevents installing it.";
                    throw new GracefulException(message, isUserError: true);
                }

                var installStep = new InstallHostWorkloadInitializationStep(_installer, installRequired.HostVersion);
                context.AddNext(installStep);
                return StartInitializationStepResult.Completed(installRequired.Message);

            default:
                throw new InvalidOperationException($"Unknown host workload resolution: {resolution.GetType().Name}");
        }
    }
}
