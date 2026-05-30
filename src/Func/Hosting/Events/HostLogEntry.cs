// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Hosting.Events;

/// <summary>
/// Single structured log record flowing from the host (or a fake source) to
/// the CLI dashboard. The shape is intentionally generic — the CLI does all
/// semantic interpretation by inspecting <see cref="Category"/>,
/// <see cref="EventId"/>, and <see cref="Attributes"/>.
/// </summary>
/// <param name="Timestamp">When the record was produced.</param>
/// <param name="Category">Logger category, e.g. <c>Function.HttpTrigger1</c> or <c>Host.Startup</c>.</param>
/// <param name="Level">Severity.</param>
/// <param name="EventId">Logger event id; <see cref="EventId.None"/> when not set.</param>
/// <param name="Message">Already-formatted message text.</param>
/// <param name="Exception">Associated exception, if any.</param>
/// <param name="Attributes">
/// Open attribute bag. Well-known keys live on <see cref="HostLogAttributeKeys"/>;
/// unknown keys flow through untouched.
/// </param>
internal sealed record HostLogEntry(
    DateTimeOffset Timestamp,
    string Category,
    LogLevel Level,
    EventId EventId,
    string Message,
    Exception? Exception,
    IReadOnlyDictionary<string, object?> Attributes)
{
    public static IReadOnlyDictionary<string, object?> EmptyAttributes { get; } =
        new Dictionary<string, object?>(0);

    public HostLogExceptionDetails? ExceptionDetails { get; init; } = Exception is not null ? HostLogExceptionDetails.FromException(Exception) : null;

    /// <summary>
    /// Convenience accessor: returns the attribute value as <typeparamref name="T"/>
    /// when present and assignable, otherwise <paramref name="fallback"/>.
    /// </summary>
    public T? GetAttribute<T>(string key, T? fallback = default)
    {
        if (Attributes.TryGetValue(key, out object? value) && value is T typed)
        {
            return typed;
        }

        return fallback;
    }
}
