// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Host;
using Azure.Functions.Cli.Hosting.Events;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Tests.Commands.Start.Host;

public class WorkerFailureDetectingHostEventStreamTests
{
    private const string ImportError = "ImportError: DLL load failed while importing cygrpc: The filename or extension is too long.";

    [Fact]
    public async Task RetriesExhausted_RecordsRuntimeAndCause_AndPassesEntriesThrough()
    {
        var inner = new InMemoryHostEventStream();
        inner.Write(WorkerExited($"{ImportError},{ImportError},{ImportError}"));
        inner.Write(Entry(
            LogLevel.Error,
            "Exceeded language worker restart retry count for runtime:python. Shutting down and proactively recycling the Functions Host to recover"));
        inner.Complete();

        await using var stream = new WorkerFailureDetectingHostEventStream(inner);
        List<HostLogEntry> observed = await DrainAsync(stream);

        observed.Should().HaveCount(2);
        stream.Detected.Should().BeTrue();
        stream.Runtime.Should().Be("python");
        stream.Cause.Should().Be(ImportError);
    }

    [Fact]
    public async Task WorkerThatRecoversAfterARestart_IsNotDetected()
    {
        var inner = new InMemoryHostEventStream();
        inner.Write(WorkerExited(ImportError));
        inner.Write(Entry(LogLevel.Information, "Host started."));
        inner.Complete();

        await using var stream = new WorkerFailureDetectingHostEventStream(inner);
        await DrainAsync(stream);

        stream.Detected.Should().BeFalse();
    }

    private static async Task<List<HostLogEntry>> DrainAsync(IHostEventStream stream)
    {
        List<HostLogEntry> entries = [];
        await foreach (HostLogEntry entry in stream.ReadAsync(CancellationToken.None))
        {
            entries.Add(entry);
        }

        return entries;
    }

    private static HostLogEntry WorkerExited(string innermostMessage)
        => Entry(LogLevel.Error, "Language Worker Process exited. Pid=1234.") with
        {
            ExceptionDetails = new HostLogExceptionDetails(
                "Microsoft.Azure.WebJobs.Script.Workers.WorkerProcessExitException",
                "python.exe exited with code 1 (0x1)",
                InnerException: new HostLogExceptionDetails("System.Exception", innermostMessage)),
        };

    private static HostLogEntry Entry(LogLevel level, string message)
        => new(
            DateTimeOffset.UtcNow,
            "Host.Process",
            level,
            default,
            message,
            Exception: null,
            HostLogEntry.EmptyAttributes);
}
