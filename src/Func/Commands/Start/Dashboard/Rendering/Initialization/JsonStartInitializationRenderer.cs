// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Buffers;
using System.Globalization;
using System.Text.Json;
using Azure.Functions.Cli.Commands.Start.Initialization;
using Azure.Functions.Cli.Hosting.Events;

namespace Azure.Functions.Cli.Commands.Start.Initialization.Rendering;

/// <summary>
/// NDJSON initialization renderer for programmatic consumers.
/// </summary>
internal sealed class JsonStartInitializationRenderer : IStartInitializationRenderer
{
    private const int SchemaVersion = 1;

    private static readonly JsonWriterOptions _writerOptions = new()
    {
        Indented = false,
        SkipValidation = true,
    };

    private readonly Stream _stdout;
    private readonly bool _ownsStream;
    private readonly Lock _lock = new();

    public JsonStartInitializationRenderer()
        : this(System.Console.OpenStandardOutput(), ownsStream: false)
    {
    }

    internal JsonStartInitializationRenderer(Stream output, bool ownsStream)
    {
        _stdout = output ?? throw new ArgumentNullException(nameof(output));
        _ownsStream = ownsStream;
    }

    public Task OnEventAsync(StartInitializationEvent initializationEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        switch (initializationEvent)
        {
            case StartInitializationStartedEvent started:
                WriteRecord(CliEventKinds.StartInitializationStarted, started.Timestamp, writer =>
                {
                    writer.WriteString("profile", started.ProfileName);
                });
                break;
            case StartInitializationStepStartedEvent step:
                WriteRecord(CliEventKinds.StartInitializationStepStarted, step.Timestamp, writer =>
                {
                    writer.WriteString("step", step.Step.Id);
                    writer.WriteString("title", step.Step.Title);
                    writer.WriteString("display", FormatDisplayKind(step.Step.DisplayKind));
                    if (!string.IsNullOrWhiteSpace(step.Step.Detail))
                    {
                        writer.WriteString("detail", step.Step.Detail);
                    }
                });
                break;
            case StartInitializationProgressEvent progress:
                WriteRecord(CliEventKinds.StartInitializationProgress, progress.Timestamp, writer =>
                {
                    writer.WriteString("step", progress.StepId);
                    writer.WriteNumber("percent", Math.Round(progress.Percent, 3));
                    if (!string.IsNullOrWhiteSpace(progress.Message))
                    {
                        writer.WriteString("message", progress.Message);
                    }
                });
                break;
            case StartInitializationStepCompletedEvent completed:
                WriteRecord(CliEventKinds.StartInitializationStepCompleted, completed.Timestamp, writer =>
                {
                    writer.WriteString("step", completed.StepId);
                    if (!string.IsNullOrWhiteSpace(completed.Message))
                    {
                        writer.WriteString("message", completed.Message);
                    }
                });
                break;
            case StartInitializationCompletedEvent completed:
                WriteRecord(CliEventKinds.StartInitializationCompleted, completed.Timestamp, writer =>
                {
                    writer.WriteString("profile", completed.Result.RunInfo.ProfileName);
                    writer.WriteString("stack", completed.Result.RunInfo.StackName);
                    writer.WriteString("host_version", completed.Result.HostVersion);
                    writer.WriteBoolean("bundle_required", completed.Result.BundleRequired);
                    if (!string.IsNullOrWhiteSpace(completed.Result.BundleVersion))
                    {
                        writer.WriteString("bundle_version", completed.Result.BundleVersion);
                    }
                });
                break;
        }

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Flush();
        if (_ownsStream)
        {
            _stdout.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private static string FormatDisplayKind(StartInitializationDisplayKind kind)
        => kind.ToString().ToLowerInvariant();

    private void WriteRecord(string kind, DateTimeOffset timestamp, Action<Utf8JsonWriter> body)
    {
        var buffer = new ArrayBufferWriter<byte>(512);
        using (var writer = new Utf8JsonWriter(buffer, _writerOptions))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", SchemaVersion);
            writer.WriteString("kind", kind);
            writer.WriteString("timestamp", timestamp.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
            body(writer);
            writer.WriteEndObject();
        }

        lock (_lock)
        {
            _stdout.Write(buffer.WrittenSpan);
            _stdout.Write("\n"u8);
        }
    }

    private void Flush()
    {
        lock (_lock)
        {
            _stdout.Flush();
        }
    }
}
