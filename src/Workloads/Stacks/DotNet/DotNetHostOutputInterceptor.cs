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
/// with <c>source: "azure-functions-cli-host"</c>. This interceptor looks for messages containing
/// the worker PID pattern and writes the PID in the <c>{"processId":N}</c> format that
/// Visual Studio expects for debugger attachment.
/// </summary>
internal sealed partial class DotNetHostOutputInterceptor : IHostOutputInterceptor
{
    // v4 prefix — the startup hook may still emit these on some host versions.
    internal const string JsonLogPrefix = "azfuncjsonlog:";

    // Pattern for the worker PID message the host emits via Host.Function.Console category.
    [GeneratedRegex(@"\(PID:\s*(\d+)\)", RegexOptions.Compiled)]
    private static partial Regex WorkerPidRegex();

    private readonly TextWriter? _writer;
    private bool _pidCaptured;

    internal DotNetHostOutputInterceptor(string? outputFilePath)
    {
        if (!string.IsNullOrWhiteSpace(outputFilePath))
        {
            var stream = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, bufferSize: 4096, useAsync: true);
            _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true,
            };
        }
    }

    public bool TryIntercept(string line)
    {
        // v4 path: raw azfuncjsonlog: prefix (if the host passes it through verbatim).
        if (line.StartsWith(JsonLogPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string payload = line[JsonLogPrefix.Length..];
            _writer?.WriteLine(payload);
            return true;
        }

        // v5 path: structured JSON record from the host.
        if (!_pidCaptured && line.Length > 0 && line[0] == '{')
        {
            return TryInterceptStructuredRecord(line);
        }

        return false;
    }

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

            string? message = messageElement.GetString();
            if (message is null)
            {
                return false;
            }

            // Check for v4-style azfuncjsonlog prefix embedded in the message field.
            if (message.StartsWith(JsonLogPrefix, StringComparison.OrdinalIgnoreCase))
            {
                _writer?.WriteLine(message[JsonLogPrefix.Length..]);
                return true;
            }

            // Extract worker PID from the human-readable debug message.
            // Write the PID to the file but return false so the message still renders in the console.
            Match match = WorkerPidRegex().Match(message);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int pid))
            {
                _writer?.WriteLine($"{{\"processId\":{pid}}}");
                _pidCaptured = true;
                return false;
            }
        }
        catch (JsonException)
        {
            // Not valid JSON — fall through.
        }

        return false;
    }

    public async ValueTask DisposeAsync()
    {
        if (_writer is not null)
        {
            await _writer.DisposeAsync();
        }
    }
}
