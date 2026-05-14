// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Hosting.Events;

namespace Azure.Functions.Cli.Hosting.Dashboard;

/// <summary>
/// Renderer-facing projection of a <see cref="HostLogEntry"/>: the bits the
/// log region needs to print a single line, with the function name lifted
/// out of the attribute bag (when present) for prefixing.
/// </summary>
internal sealed record LogRecord(
    DateTimeOffset Timestamp,
    Microsoft.Extensions.Logging.LogLevel Level,
    string Category,
    string? Function,
    string Message,
    Exception? Exception)
{
    public static LogRecord From(HostLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        string? function = entry.GetAttribute<string>(HostLogAttributeKeys.FunctionName);
        return new LogRecord(entry.Timestamp, entry.Level, entry.Category, function, entry.Message, entry.Exception);
    }
}
