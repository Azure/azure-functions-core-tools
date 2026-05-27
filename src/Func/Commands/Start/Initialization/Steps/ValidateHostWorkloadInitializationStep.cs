// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Commands.Start.Host;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Validates that the requested host workload is available.
/// </summary>
internal sealed class ValidateHostWorkloadInitializationStep(IHostWorkloadResolver resolver, IWorkloadInstaller installer, IWorkloadPaths workloadPaths)
    : DemoInitializationStep
{
    public const string StepId = "resolve_host_workload";
    public const string HostContentRootEnvironmentVariable = "FUNC_HOST_CONTENT_ROOT";

    private const string LocalHostPackageId = "Azure.Functions.Cli.Workloads.Host.local";
    private const string LocalHostVersion = "local-dev";
    private const string HostAlias = "host";

    private readonly IHostWorkloadResolver _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    private readonly IWorkloadInstaller _installer = installer ?? throw new ArgumentNullException(nameof(installer));
    private readonly IWorkloadPaths _workloadPaths = workloadPaths ?? throw new ArgumentNullException(nameof(workloadPaths));

    public override string Id => StepId;

    public override string Title => "Validate host version";

    public override async Task<StartInitializationStepResult> ExecuteAsync(
        StartInitializationStepContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        await SimulateWorkAsync(context, cancellationToken);

        string? localContentRoot = Environment.GetEnvironmentVariable(HostContentRootEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(localContentRoot))
        {
            ContentWorkloadInfo workload = CreateLocalContentWorkload(localContentRoot);
            context.State.HostVersion = workload.PackageVersion;
            context.State.HostWorkload = workload;
            return StartInitializationStepResult.Completed($"Using local host content root {workload.ContentRoot}");
        }

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
                context.State.HostWorkload = installed.Workload;
                string explicitText = installed.ExplicitlyRequested ? "requested " : string.Empty;
                return StartInitializationStepResult.Completed($"Using {explicitText}host {installed.HostVersion}");

            case HostWorkloadResolution.InstallRequired installRequired:
                if (context.Options.Offline)
                {
                    string message = $"{installRequired.Message}, and --offline prevents installing it.";
                    throw new GracefulException(message, isUserError: true);
                }

                var installStep = new InstallHostWorkloadInitializationStep(_installer, _workloadPaths, installRequired.PackageId, installRequired.HostVersion);
                context.AddNext(installStep);

                return StartInitializationStepResult.Completed(installRequired.Message);

            default:
                throw new InvalidOperationException($"Unknown host workload resolution: {resolution.GetType().Name}");
        }
    }

    private static ContentWorkloadInfo CreateLocalContentWorkload(string configuredContentRoot)
    {
        string contentRoot = Path.GetFullPath(configuredContentRoot);
        if (!Directory.Exists(contentRoot))
        {
            throw new GracefulException(
                $"{HostContentRootEnvironmentVariable} points to '{contentRoot}', but that directory does not exist.",
                isUserError: true);
        }

        string executablePath = HostProcessStartInfoFactory.ResolveExecutablePath(contentRoot);
        if (!File.Exists(executablePath))
        {
            throw new GracefulException(
                $"{HostContentRootEnvironmentVariable} points to '{contentRoot}', but the host executable was not found at '{executablePath}'.",
                isUserError: true);
        }

        return new ContentWorkloadInfo(
            LocalHostPackageId,
            LocalHostVersion,
            [HostAlias],
            contentRoot,
            contentRoot,
            "Azure Functions host (local)",
            $"Local host content root from {HostContentRootEnvironmentVariable}.");
    }
}
