// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads.DotNet;

/// <summary>
/// Intercepts structured JSON host output lines that carry .NET worker debug information
/// (worker PID, JSON log payloads). In v5, the host wraps all output in structured JSON records
/// with <c>source: "azure-functions-cli-host"</c>. This interceptor extracts the worker PID and
/// writes it in the same format the worker's startup hook emits via the <c>azfuncjsonlog:</c>
/// protocol, so consumers (VS, VS Code) see the same <c>workerProcessId</c> field v4 produced.
/// </summary>
internal sealed partial class DotNetHostOutputInterceptor : IHostOutputInterceptor
{
    // v4 prefix — the startup hook may still emit these on some host versions.
    internal const string JsonLogPrefix = "azfuncjsonlog:";

    // Matches the full worker debug message: "Azure Functions .NET Worker (PID: 12345) initialized in debug mode."
    [GeneratedRegex(@"Azure Functions \.NET Worker \(PID:\s*(\d+)\) initialized in debug mode", RegexOptions.Compiled)]
    private static partial Regex WorkerPidRegex();

    private readonly string? _outputFilePath;
    private TextWriter? _writer;
    private bool _pidCaptured;

    internal DotNetHostOutputInterceptor(string? outputFilePath)
    {
        _outputFilePath = string.IsNullOrWhiteSpace(outputFilePath) ? null : outputFilePath;
    }

    public bool TryIntercept(string line)
    {
        // v4 path: raw azfuncjsonlog: prefix (if the host passes it through verbatim).
        if (line.StartsWith(JsonLogPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string payload = line[JsonLogPrefix.Length..];
            WriteToDisk(payload);
            _pidCaptured = true;

            // When there is no output file, don't suppress — let the line flow to console.
            return _writer is not null;
        }

        // v5 path: structured JSON record from the host.
        if (line.Length > 0 && line[0] == '{')
        {
            return TryInterceptStructuredRecord(line);
        }

        return false;
    }

    // The host emits tooling log records under this category.
    private const string ToolingConsoleLogCategory = "Host.Function.ToolingConsoleLog";

    private bool TryInterceptStructuredRecord(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            JsonElement root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("message", out JsonElement messageElement)
                || messageElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            // Only process records from the tooling console log category.
            if (!root.TryGetProperty("category", out JsonElement categoryElement)
                || !string.Equals(categoryElement.GetString(), ToolingConsoleLogCategory, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string? message = messageElement.GetString();
            if (message is null)
            {
                return false;
            }

            // Check for v4-style azfuncjsonlog prefix embedded in the message field.
            if (message.StartsWith(JsonLogPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string payload = message[JsonLogPrefix.Length..];
                WriteToDisk(payload);
                _pidCaptured = true;

                // Suppress only when we have a file consumer; otherwise let it render.
                return _writer is not null;
            }

            // Extract worker PID from the human-readable debug message as a fallback.
            // Only fires when no genuine azfuncjsonlog PID event was received yet.
            if (!_pidCaptured)
            {
                Match match = WorkerPidRegex().Match(message);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int pid))
                {
                    WriteToDisk($"{{ \"name\":\"dotnet-worker-startup\", \"workerProcessId\" : {pid} }}");
                    _pidCaptured = true;
                    return false;
                }
            }
        }
        catch (JsonException)
        {
            // Not valid JSON — fall through.
        }

        return false;
    }

    private void WriteToDisk(string payload)
    {
        if (_outputFilePath is null)
        {
            return;
        }

        // Lazy-create the file on first write so we never leave an empty file when
        // dotnet-isolated isn't the active stack.
        if (_writer is null)
        {
            var stream = new FileStream(_outputFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, bufferSize: 4096, useAsync: true);
            _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true,
            };
        }

        _writer.WriteLine(payload);
    }

    public async ValueTask DisposeAsync()
    {
        if (_writer is not null)
        {
            await _writer.DisposeAsync();
        }
    }
}
