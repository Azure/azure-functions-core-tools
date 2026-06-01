// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Threading.Channels;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Bridges synchronous project reports into the asynchronous initialization renderer.
/// </summary>
internal sealed class FunctionsProjectHostRunReporter : IFunctionsProjectHostRunReporter, IAsyncDisposable
{
    private readonly StartInitializationStepContext _context;
    private readonly CancellationToken _cancellationToken;
    private readonly Channel<Func<CancellationToken, Task>> _reports = Channel.CreateUnbounded<Func<CancellationToken, Task>>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false,
    });
    private readonly Task _reader;

    public FunctionsProjectHostRunReporter(StartInitializationStepContext context, CancellationToken cancellationToken)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _cancellationToken = cancellationToken;
        _reader = Task.Run(ProcessReportsAsync, CancellationToken.None);
    }

    public void ReportStatus(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        Enqueue(ct => _context.ReportStatusAsync(message, ct));
    }

    public void WriteLog(string line, FunctionsProjectReportSeverity severity = FunctionsProjectReportSeverity.Info)
    {
        if (line is null)
        {
            return;
        }

        Enqueue(ct => _context.ReportLogAsync(line, severity, ct));
    }

    public async Task CompleteAsync()
    {
        _reports.Writer.TryComplete();
        await _reader;
    }

    public async ValueTask DisposeAsync()
        => await CompleteAsync();

    private void Enqueue(Func<CancellationToken, Task> report)
    {
        if (!_reports.Writer.TryWrite(report))
        {
            return;
        }
    }

    private async Task ProcessReportsAsync()
    {
        await foreach (Func<CancellationToken, Task> report in _reports.Reader.ReadAllAsync(CancellationToken.None))
        {
            try
            {
                await report(_cancellationToken);
            }
            catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception)
            {
                // Project reporting is best-effort UI feedback; renderer failures must not escape into workload preparation.
            }
        }
    }
}
