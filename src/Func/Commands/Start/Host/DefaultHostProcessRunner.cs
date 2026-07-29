// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using System.Text;
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

        // Open the JSON output file before launching so a bad path fails fast without a leaked process.
        TextWriter? rawStdoutWriter = CreateJsonOutputWriter(context.JsonOutputFilePath);

        IHostProcess process = _processFactory.Create(launchInfo.StartInfo);
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            rawStdoutWriter?.Dispose();
            throw CreateStartFailure(launchInfo, ex);
        }
        catch (InvalidOperationException ex)
        {
            rawStdoutWriter?.Dispose();
            throw CreateStartFailure(launchInfo, ex);
        }

        IHostEventStream stream = new HostProcessEventStream(process, _outputParser, launchInfo, _shutdownTimeout, _timeProvider, rawStdoutWriter);
        return Task.FromResult(stream);
    }

    private static TextWriter? CreateJsonOutputWriter(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            return new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            throw new GracefulException(
                $"Could not open JSON output file '{path}': {ex.Message}",
                isUserError: true,
                verboseMessage: ex.ToString());
        }
    }

    private static GracefulException CreateStartFailure(HostProcessLaunchInfo launchInfo, Exception exception)
        => new(
            $"Failed to start host process '{launchInfo.StartInfo.FileName}': {exception.Message}",
            isUserError: true,
            verboseMessage: exception.ToString());
}
