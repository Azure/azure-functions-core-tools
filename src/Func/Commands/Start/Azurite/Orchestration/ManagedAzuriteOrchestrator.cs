// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using Azure.Functions.Cli.Commands.Start.Azurite.Launching;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Commands.Start.Azurite.Orchestration;

/// <inheritdoc cref="IManagedAzuriteOrchestrator"/>
internal sealed class ManagedAzuriteOrchestrator : IManagedAzuriteOrchestrator
{
    private const string AzuriteInstallUrl = "https://aka.ms/azfunc-azurite";
    private static readonly TimeSpan _pollInterval = TimeSpan.FromMilliseconds(250);

    private readonly IAzureWebJobsStorageClassifier _classifier;
    private readonly IAzuriteProbe _probe;
    private readonly IAzuriteExecutableLocator _executableLocator;
    private readonly IDockerAvailabilityProbe _dockerProbe;
    private readonly IAzuriteLauncher _launcher;
    private readonly IAzuriteManagedPathsProvider _pathsProvider;
    private readonly ILogger<ManagedAzuriteOrchestrator> _logger;

    public ManagedAzuriteOrchestrator(
        IAzureWebJobsStorageClassifier classifier,
        IAzuriteProbe probe,
        IAzuriteExecutableLocator executableLocator,
        IDockerAvailabilityProbe dockerProbe,
        IAzuriteLauncher launcher,
        IAzuriteManagedPathsProvider pathsProvider,
        ILogger<ManagedAzuriteOrchestrator> logger)
    {
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _executableLocator = executableLocator ?? throw new ArgumentNullException(nameof(executableLocator));
        _dockerProbe = dockerProbe ?? throw new ArgumentNullException(nameof(dockerProbe));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _pathsProvider = pathsProvider ?? throw new ArgumentNullException(nameof(pathsProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ManagedAzuriteResult> EnsureReadyAsync(
        ManagedAzuriteRequest request,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Disabled)
        {
            _logger.LogInformation("Managed Azurite disabled via --no-azurite.");
            return new ManagedAzuriteResult.Disabled("--no-azurite was specified.");
        }

        progress?.Report("checking AzureWebJobsStorage configuration");
        AzureWebJobsStorageReference classification = _classifier.Classify(request.StorageConnectionString);
        _logger.LogInformation(
            "AzureWebJobsStorage classified as {Classification}: {Reason}",
            classification.Classification,
            classification.Reason);

        switch (classification.Classification)
        {
            case AzureWebJobsStorageClassification.NotLocal:
                return new ManagedAzuriteResult.Disabled(classification.Reason);

            case AzureWebJobsStorageClassification.UserConfiguredAzurite:
                return await EnsureUserConfiguredAsync(classification, progress, cancellationToken);

            case AzureWebJobsStorageClassification.ManageableAzurite:
                return await EnsureManageableAsync(request, classification, progress, cancellationToken);

            default:
                return new ManagedAzuriteResult.Failed(
                    $"Unsupported AzureWebJobsStorage classification: {classification.Classification}.");
        }
    }

    private async Task<ManagedAzuriteResult> EnsureUserConfiguredAsync(
        AzureWebJobsStorageReference classification,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        AzuriteEndpointTuple endpoints = classification.Endpoints ?? DefaultEndpoints();
        progress?.Report("probing configured Azurite endpoints");
        AzuriteProbeResult probe = await _probe.ProbeAsync(endpoints, cancellationToken);

        if (probe.Status == AzuriteProbeStatus.Ready)
        {
            _logger.LogInformation("User-configured Azurite is already running.");
            return new ManagedAzuriteResult.UserManaged(endpoints, classification.Reason);
        }

        string message = BuildUserConfiguredFailureMessage(endpoints);
        return new ManagedAzuriteResult.Failed(message, probe.Reason);
    }

    private async Task<ManagedAzuriteResult> EnsureManageableAsync(
        ManagedAzuriteRequest request,
        AzureWebJobsStorageReference classification,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        AzuriteEndpointTuple endpoints = classification.Endpoints ?? DefaultEndpoints();

        progress?.Report("checking for an existing Azurite endpoint");
        AzuriteProbeResult probe = await _probe.ProbeAsync(endpoints, cancellationToken);
        switch (probe.Status)
        {
            case AzuriteProbeStatus.Ready:
                // §11 user-managed-reuse fallback: when MVP discovers something
                // already on the ports, treat it as user-managed and skip
                // launching our own.
                _logger.LogInformation("Existing Azurite endpoint detected; reusing it without launching a managed instance.");

                // This cannot render to the live display directly as it will introduce rendering issues.
                // TODO: If this information needs to be surfaced beyond initialization, we can introduce a new mechanism to display this data.
                //_interaction.WriteLine(l => l
                //    .Muted("Using existing Azurite endpoint at ")
                //    .Path(endpoints.BlobEndpoint.ToString())
                //    .Muted("."));
                //_interaction.WriteBlankLine();

                return new ManagedAzuriteResult.UserManaged(endpoints, "Endpoints already responded with storage-shaped replies.");

            case AzuriteProbeStatus.PortConflict:
            case AzuriteProbeStatus.Partial:
                return new ManagedAzuriteResult.Failed(
                    BuildPortConflictMessage(endpoints, probe),
                    probe.Reason);

            case AzuriteProbeStatus.NotListening:
                break;
        }

        progress?.Report("looking for a local Azurite installation");
        AzuriteExecutable? executable = await _executableLocator.FindAsync(request.ProjectRoot, cancellationToken);
        if (executable is not null)
        {
            progress?.Report("starting Azurite (native)");
            return await LaunchAsync(
                request,
                endpoints,
                AzuriteLaunchMode.Native,
                executable.FilePath,
                progress,
                cancellationToken);
        }

        progress?.Report("checking Docker availability");
        DockerAvailability docker = await _dockerProbe.ProbeAsync(cancellationToken);
        if (docker.Status != DockerAvailabilityStatus.Available)
        {
            return new ManagedAzuriteResult.Failed(
                BuildDockerUnavailableMessage(docker),
                docker.Reason);
        }

        progress?.Report("starting Azurite (Docker)");
        return await LaunchAsync(
            request,
            endpoints,
            AzuriteLaunchMode.Docker,
            executablePath: null,
            progress,
            cancellationToken);
    }

    private async Task<ManagedAzuriteResult> LaunchAsync(
        ManagedAzuriteRequest request,
        AzuriteEndpointTuple endpoints,
        AzuriteLaunchMode mode,
        string? executablePath,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        AzuriteManagedPaths paths = _pathsProvider.GetPaths();
        await _pathsProvider.EnsureCreatedAsync(paths, cancellationToken);

        AzuriteLaunchRequest launchRequest = new(
            mode: mode,
            blobPort: endpoints.BlobEndpoint.Port,
            queuePort: endpoints.QueueEndpoint.Port,
            tablePort: endpoints.TableEndpoint.Port,
            dataPath: paths.DataDirectory,
            logPath: paths.LogFilePath,
            executablePath: executablePath,
            dockerImage: mode == AzuriteLaunchMode.Docker ? AzuriteDockerImage.Default : null,
            containerName: mode == AzuriteLaunchMode.Docker ? "func-azurite" : null);

        IAzuriteProcess process;
        try
        {
            process = await _launcher.StartAsync(launchRequest, cancellationToken);
        }
        catch (AzuriteLaunchException ex)
        {
            return new ManagedAzuriteResult.Failed(
                $"Azurite could not be launched: {ex.Message}",
                ex.ToString());
        }

        PollOutcome poll = await PollReadyAsync(process, endpoints, request.StartupTimeout, progress, cancellationToken);
        switch (poll.Kind)
        {
            case PollOutcomeKind.Ready:
                progress?.Report($"Azurite ready ({poll.Elapsed.TotalSeconds:0.0}s)");
                return new ManagedAzuriteResult.Started(process, mode, endpoints);

            case PollOutcomeKind.ProcessExited:
                await SafeDisposeAsync(process);
                return new ManagedAzuriteResult.Failed(
                    BuildProcessExitedMessage(mode, paths, poll.StderrTail),
                    poll.StderrTail);

            case PollOutcomeKind.TimedOut:
            default:
                await StopAndDisposeAsync(process);
                return new ManagedAzuriteResult.Failed(
                    BuildTimeoutMessage(endpoints, paths, request.StartupTimeout),
                    poll.StderrTail);
        }
    }

    private async Task<PollOutcome> PollReadyAsync(
        IAzuriteProcess process,
        AzuriteEndpointTuple endpoints,
        TimeSpan timeout,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeoutCts = new(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutCts.Token);

        long startTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        Task<int> exitTask = process.WaitForExitAsync(linkedCts.Token);
        long lastHeartbeatTimestamp = startTimestamp;

        while (true)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            if (progress is not null)
            {
                TimeSpan sinceHeartbeat = System.Diagnostics.Stopwatch.GetElapsedTime(lastHeartbeatTimestamp);
                if (sinceHeartbeat >= TimeSpan.FromSeconds(2))
                {
                    TimeSpan totalElapsed = System.Diagnostics.Stopwatch.GetElapsedTime(startTimestamp);
                    progress.Report($"waiting for Azurite to be ready ({totalElapsed.TotalSeconds:0}s)");
                    lastHeartbeatTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
                }
            }

            if (exitTask.IsCompleted)
            {
                string stderr = await TryReadStderrTailAsync(process);
                return new PollOutcome(
                    PollOutcomeKind.ProcessExited,
                    System.Diagnostics.Stopwatch.GetElapsedTime(startTimestamp),
                    stderr);
            }

            AzuriteProbeResult probe;
            try
            {
                probe = await _probe.ProbeAsync(endpoints, linkedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                string stderr = await TryReadStderrTailAsync(process);
                return new PollOutcome(
                    PollOutcomeKind.TimedOut,
                    System.Diagnostics.Stopwatch.GetElapsedTime(startTimestamp),
                    stderr);
            }

            if (probe.Status == AzuriteProbeStatus.Ready)
            {
                return new PollOutcome(
                    PollOutcomeKind.Ready,
                    System.Diagnostics.Stopwatch.GetElapsedTime(startTimestamp),
                    StderrTail: null);
            }

            var delay = Task.Delay(_pollInterval, linkedCts.Token);
            Task completed = await Task.WhenAny(delay, exitTask);
            if (completed == exitTask)
            {
                string stderr = await TryReadStderrTailAsync(process);
                return new PollOutcome(
                    PollOutcomeKind.ProcessExited,
                    System.Diagnostics.Stopwatch.GetElapsedTime(startTimestamp),
                    stderr);
            }

            if (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                string stderr = await TryReadStderrTailAsync(process);
                return new PollOutcome(
                    PollOutcomeKind.TimedOut,
                    System.Diagnostics.Stopwatch.GetElapsedTime(startTimestamp),
                    stderr);
            }
        }
    }

    private static async Task<string> TryReadStderrTailAsync(IAzuriteProcess process)
    {
        // Drain stderr with a tight per-line budget so we never block readiness
        // diagnostics on a process that has stopped writing.
        using CancellationTokenSource readCts = new(TimeSpan.FromMilliseconds(500));
        Queue<string> lines = new(capacity: 10);
        try
        {
            await foreach (string line in process.ReadStderrLinesAsync(readCts.Token))
            {
                if (lines.Count == 10)
                {
                    lines.Dequeue();
                }

                lines.Enqueue(line);
            }
        }
        catch
        {
            // Best effort: stderr capture must not mask the launch failure.
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static async Task SafeDisposeAsync(IAzuriteProcess process)
    {
        try
        {
            await process.DisposeAsync();
        }
        catch
        {
            // Best effort.
        }
    }

    private static async Task StopAndDisposeAsync(IAzuriteProcess process)
    {
        using CancellationTokenSource stopCts = new(TimeSpan.FromSeconds(5));
        try
        {
            await process.StopAsync(stopCts.Token);
        }
        catch
        {
            // Best effort.
        }

        await SafeDisposeAsync(process);
    }

    private static AzuriteEndpointTuple DefaultEndpoints()
    {
        Uri Build(int port) => new UriBuilder("http", "127.0.0.1", port, $"/{AzureWebJobsStorageClassifier.DevelopmentStorageAccountName}").Uri;
        return new AzuriteEndpointTuple(
            BlobEndpoint: Build(AzureWebJobsStorageClassifier.DefaultBlobPort),
            QueueEndpoint: Build(AzureWebJobsStorageClassifier.DefaultQueuePort),
            TableEndpoint: Build(AzureWebJobsStorageClassifier.DefaultTablePort),
            AccountName: AzureWebJobsStorageClassifier.DevelopmentStorageAccountName);
    }

    private static string BuildUserConfiguredFailureMessage(AzuriteEndpointTuple endpoints)
    {
        StringBuilder sb = new();
        sb.AppendLine("AzureWebJobsStorage points to a local storage emulator, but the Azure Functions CLI cannot start this configuration automatically.");
        sb.AppendLine();
        sb.AppendLine("Start Azurite with these endpoints, then run 'func start' again:");
        sb.AppendLine($"  Blob:  {endpoints.BlobEndpoint}");
        sb.AppendLine($"  Queue: {endpoints.QueueEndpoint}");
        sb.Append($"  Table: {endpoints.TableEndpoint}");
        return sb.ToString();
    }

    private static string BuildPortConflictMessage(AzuriteEndpointTuple endpoints, AzuriteProbeResult probe)
    {
        StringBuilder sb = new();
        sb.AppendLine("AzureWebJobsStorage points to local storage, but another process is using the Azurite ports.");
        sb.AppendLine();
        sb.AppendLine("Stop whatever is on these ports, then run 'func start' again:");
        sb.AppendLine($"  Blob:  {endpoints.BlobEndpoint}");
        sb.AppendLine($"  Queue: {endpoints.QueueEndpoint}");
        sb.Append($"  Table: {endpoints.TableEndpoint}");
        if (!string.IsNullOrWhiteSpace(probe.Reason))
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.Append($"Detail: {probe.Reason}");
        }

        return sb.ToString();
    }

    private static string BuildDockerUnavailableMessage(DockerAvailability docker)
    {
        StringBuilder sb = new();
        sb.AppendLine("AzureWebJobsStorage points to local storage, but Azurite is not running.");
        sb.AppendLine();
        sb.AppendLine("The Azure Functions CLI could not find an Azurite executable and Docker is not available.");
        sb.AppendLine();
        sb.AppendLine("Install one of the following and run 'func start' again:");
        sb.AppendLine("  - Azurite:       npm install -g azurite");
        sb.AppendLine("  - Docker Desktop: https://docs.docker.com/desktop/");
        sb.AppendLine();
        sb.Append($"Learn more: {AzuriteInstallUrl}");
        if (docker.Status == DockerAvailabilityStatus.DaemonUnavailable)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.Append("Docker was detected but the daemon is not reachable. Start Docker Desktop and try again.");
        }

        return sb.ToString();
    }

    private static string BuildProcessExitedMessage(AzuriteLaunchMode mode, AzuriteManagedPaths paths, string? stderrTail)
    {
        StringBuilder sb = new();
        if (mode == AzuriteLaunchMode.Native)
        {
            sb.AppendLine("Azurite exited before it was ready.");
        }
        else
        {
            sb.AppendLine("The Azurite Docker container exited before it was ready.");
            sb.AppendLine($"Image: {AzuriteDockerImage.Default}");
            sb.AppendLine("Container: func-azurite (run 'docker logs func-azurite' for details)");
        }

        sb.AppendLine();
        sb.Append($"Log file: {paths.LogFilePath}");
        if (!string.IsNullOrWhiteSpace(stderrTail))
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("Last output:");
            sb.Append(stderrTail);
        }

        return sb.ToString();
    }

    private static string BuildTimeoutMessage(AzuriteEndpointTuple endpoints, AzuriteManagedPaths paths, TimeSpan timeout)
    {
        StringBuilder sb = new();
        sb.AppendLine($"Azurite did not become ready within {(int)timeout.TotalSeconds} seconds.");
        sb.AppendLine();
        sb.AppendLine("Expected endpoints:");
        sb.AppendLine($"  Blob:  {endpoints.BlobEndpoint}");
        sb.AppendLine($"  Queue: {endpoints.QueueEndpoint}");
        sb.AppendLine($"  Table: {endpoints.TableEndpoint}");
        sb.AppendLine();
        sb.Append($"Log file: {paths.LogFilePath}");
        return sb.ToString();
    }

    private enum PollOutcomeKind
    {
        Ready,
        ProcessExited,
        TimedOut,
    }

    private readonly record struct PollOutcome(PollOutcomeKind Kind, TimeSpan Elapsed, string? StderrTail);
}
