// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Hosting.Events;

namespace Azure.Functions.Cli.Commands.Start.Host;

internal sealed class DefaultHostProcessRunner(
    HostProcessStartInfoFactory startInfoFactory,
    IHostPortAvailability portAvailability,
    IHostProcessFactory processFactory,
    IHostProcessOutputParser outputParser,
    TimeProvider? timeProvider = null) : IHostProcessRunner
{
    private static readonly TimeSpan _shutdownTimeout = TimeSpan.FromSeconds(5);

    private readonly HostProcessStartInfoFactory _startInfoFactory = startInfoFactory
        ?? throw new ArgumentNullException(nameof(startInfoFactory));
    private readonly IHostPortAvailability _portAvailability = portAvailability
        ?? throw new ArgumentNullException(nameof(portAvailability));
    private readonly IHostProcessFactory _processFactory = processFactory
        ?? throw new ArgumentNullException(nameof(processFactory));
    private readonly IHostProcessOutputParser _outputParser = outputParser
        ?? throw new ArgumentNullException(nameof(outputParser));
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public Task<IHostEventStream> StartAsync(HostProcessStartContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        HostProcessLaunchInfo launchInfo = _startInfoFactory.Create(context);
        if (!_portAvailability.IsAvailable(launchInfo.Port))
        {
            throw new GracefulException(
                $"Port {launchInfo.Port} is unavailable. Close the process using that port, or specify another port using --port [-p].",
                isUserError: true);
        }

        IHostProcess process = _processFactory.Create(launchInfo.StartInfo);
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw CreateStartFailure(launchInfo, ex);
        }
        catch (InvalidOperationException ex)
        {
            throw CreateStartFailure(launchInfo, ex);
        }

        IHostEventStream stream = new HostProcessEventStream(process, _outputParser, launchInfo, _shutdownTimeout, _timeProvider);
        return Task.FromResult(stream);
    }

    private static GracefulException CreateStartFailure(HostProcessLaunchInfo launchInfo, Exception exception)
        => new(
            $"Failed to start host process '{launchInfo.StartInfo.FileName}': {exception.Message}",
            isUserError: true,
            verboseMessage: exception.ToString());
}
