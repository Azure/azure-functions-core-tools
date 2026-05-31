// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Hosting.Events;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Commands.Start.Host;

internal sealed class LineHostProcessOutputParser : IHostProcessOutputParser
{
    private const string ConsoleLogContinuationPrefix = "      ";
    private readonly object _consoleLogLock = new();
    private readonly Dictionary<string, ConsoleLogContext> _consoleLogContexts = new(StringComparer.Ordinal);

    public HostLogEntry ParseLine(string streamName, string line, DateTimeOffset timestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamName);
        ArgumentNullException.ThrowIfNull(line);

        if (TryParseStructuredHostRecord(streamName, line, timestamp, out HostLogEntry? structured))
        {
            return structured!;
        }

        if (TryParseConsoleLogRecord(streamName, line, timestamp, out HostLogEntry? consoleLog))
        {
            return consoleLog!;
        }

        LogLevel level = string.Equals(streamName, HostProcessStreamNames.StandardError, StringComparison.Ordinal)
            ? LogLevel.Error
            : LogLevel.Information;

        var attributes = new Dictionary<string, object?>
        {
            [HostLogAttributeKeys.Stream] = streamName,
        };

        return new HostLogEntry(
            timestamp,
            "Host.Process",
            level,
            default,
            line,
            Exception: null,
            attributes);
    }

    private bool TryParseConsoleLogRecord(string streamName, string line, DateTimeOffset timestamp, out HostLogEntry? entry)
    {
        entry = null;
        if (TryParseConsoleLogHeader(line, out LogLevel level, out string? category, out int eventId))
        {
            var headerContext = new ConsoleLogContext(category!, level, new EventId(eventId));
            lock (_consoleLogLock)
            {
                _consoleLogContexts[streamName] = headerContext;
            }

            Dictionary<string, object?> attributes = CreateStreamAttributes(streamName);
            EnrichFromCategory(headerContext.Category, attributes);
            entry = new HostLogEntry(
                timestamp,
                headerContext.Category,
                headerContext.Level,
                headerContext.EventId,
                Message: string.Empty,
                Exception: null,
                attributes);
            return true;
        }

        if (line.StartsWith(ConsoleLogContinuationPrefix, StringComparison.Ordinal)
            && TryGetConsoleLogContext(streamName, out ConsoleLogContext context))
        {
            string message = line[ConsoleLogContinuationPrefix.Length..];
            Dictionary<string, object?> attributes = CreateStreamAttributes(streamName);
            EnrichFromCategory(context.Category, attributes);
            EnrichFromMessage(message, attributes);
            entry = new HostLogEntry(timestamp, context.Category, context.Level, context.EventId, message, Exception: null, attributes);
            return true;
        }

        ClearConsoleLogContext(streamName);
        return false;
    }

    private static bool TryParseConsoleLogHeader(string line, out LogLevel level, out string? category, out int eventId)
    {
        level = default;
        category = null;
        eventId = 0;

        if (line.Length < 8 || line[4] != ':' || line[5] != ' ')
        {
            return false;
        }

        if (!TryParseConsoleLogLevel(line[..4], out level))
        {
            return false;
        }

        int eventIdStart = line.LastIndexOf('[', line.Length - 1);
        if (eventIdStart <= 6 || line[^1] != ']')
        {
            return false;
        }

        category = line[6..eventIdStart];
        return !string.IsNullOrWhiteSpace(category)
            && int.TryParse(line[(eventIdStart + 1)..^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out eventId);
    }

    private static bool TryParseConsoleLogLevel(string value, out LogLevel level)
    {
        switch (value)
        {
            case "trce":
                level = LogLevel.Trace;
                return true;
            case "dbug":
                level = LogLevel.Debug;
                return true;
            case "info":
                level = LogLevel.Information;
                return true;
            case "warn":
                level = LogLevel.Warning;
                return true;
            case "fail":
                level = LogLevel.Error;
                return true;
            case "crit":
                level = LogLevel.Critical;
                return true;
            default:
                level = default;
                return false;
        }
    }

    private bool TryGetConsoleLogContext(string streamName, out ConsoleLogContext context)
    {
        lock (_consoleLogLock)
        {
            return _consoleLogContexts.TryGetValue(streamName, out context);
        }
    }

    private void ClearConsoleLogContext(string streamName)
    {
        lock (_consoleLogLock)
        {
            _consoleLogContexts.Remove(streamName);
        }
    }

    private static Dictionary<string, object?> CreateStreamAttributes(string streamName)
        => new(StringComparer.Ordinal)
        {
            [HostLogAttributeKeys.Stream] = streamName,
        };

    private static bool TryParseStructuredHostRecord(
        string streamName,
        string line,
        DateTimeOffset fallbackTimestamp,
        out HostLogEntry? entry)
    {
        entry = null;
        if (line.Length == 0 || line[0] != '{')
        {
            return false;
        }

        try
        {
            var document = JsonDocument.Parse(line);
            using (document)
            {
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !IsStructuredHostRecord(root))
                {
                    return false;
                }

                DateTimeOffset timestamp = ReadTimestamp(root, fallbackTimestamp);
                string category = ReadString(root, "category") ?? "Host.Process";
                LogLevel level = ParseLevel(ReadString(root, "level"), streamName);
                EventId eventId = ReadEventId(root);
                string message = ReadString(root, "message") ?? line;
                HostLogExceptionDetails? exceptionDetails = ReadException(root);
                Exception? exception = exceptionDetails is not null ? new HostProcessException(exceptionDetails) : null;
                Dictionary<string, object?> attributes = root.TryGetProperty("attributes", out JsonElement attributesElement)
                    && attributesElement.ValueKind == JsonValueKind.Object
                        ? ReadAttributes(attributesElement)
                        : new Dictionary<string, object?>(StringComparer.Ordinal);

                attributes[HostLogAttributeKeys.Stream] = streamName;
                EnrichFromCategory(category, attributes);
                EnrichFromMessage(message, attributes);

                entry = new HostLogEntry(timestamp, category, level, eventId, message, exception, attributes)
                {
                    ExceptionDetails = exceptionDetails,
                };
                return true;
            }
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsStructuredHostRecord(JsonElement root)
        => root.TryGetProperty("source", out JsonElement source)
           && source.ValueKind == JsonValueKind.String
           && string.Equals(source.GetString(), "azure-functions-cli-host", StringComparison.Ordinal)
           && root.TryGetProperty("schema_version", out JsonElement schemaVersion)
           && schemaVersion.ValueKind == JsonValueKind.Number
           && schemaVersion.TryGetInt32(out int version)
           && version == 1;

    private static DateTimeOffset ReadTimestamp(JsonElement root, DateTimeOffset fallback)
        => root.TryGetProperty("timestamp", out JsonElement timestamp)
           && timestamp.ValueKind == JsonValueKind.String
           && DateTimeOffset.TryParse(timestamp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed)
            ? parsed
            : fallback;

    private static EventId ReadEventId(JsonElement root)
    {
        if (!root.TryGetProperty("event_id", out JsonElement eventId) || eventId.ValueKind != JsonValueKind.Object)
        {
            return default;
        }

        int id = eventId.TryGetProperty("id", out JsonElement idElement) && idElement.TryGetInt32(out int parsedId)
            ? parsedId
            : 0;
        string? name = ReadString(eventId, "name");
        return new EventId(id, name);
    }

    private static HostLogExceptionDetails? ReadException(JsonElement root)
    {
        if (!root.TryGetProperty("exception", out JsonElement exception) || exception.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return ReadExceptionObject(exception);
    }

    private static HostLogExceptionDetails ReadExceptionObject(JsonElement exception)
    {
        string type = ReadString(exception, "type") ?? typeof(Exception).FullName!;
        string message = ReadString(exception, "message") ?? "Host process exception.";
        string? stack = ReadString(exception, "stack");
        HostLogExceptionDetails? innerException = exception.TryGetProperty("inner_exception", out JsonElement inner)
            && inner.ValueKind == JsonValueKind.Object
                ? ReadExceptionObject(inner)
                : null;

        return new HostLogExceptionDetails(type, message, stack, innerException);
    }

    private static Dictionary<string, object?> ReadAttributes(JsonElement attributesElement)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (JsonProperty property in attributesElement.EnumerateObject())
        {
            attributes[property.Name] = ReadAttributeValue(property.Name, property.Value);
        }

        return attributes;
    }

    private static object? ReadAttributeValue(string name, JsonElement value)
    {
        if (string.Equals(name, HostLogAttributeKeys.DurationMs, StringComparison.Ordinal)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out double duration))
        {
            return duration;
        }

        if (string.Equals(name, HostLogAttributeKeys.HttpStatusCode, StringComparison.Ordinal)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out int statusCode))
        {
            return statusCode;
        }

        return ReadJsonValue(value);
    }

    private static object? ReadJsonValue(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when value.TryGetInt64(out long l) => l,
            JsonValueKind.Number when value.TryGetDouble(out double d) => d,
            JsonValueKind.Array => ReadJsonArray(value),
            JsonValueKind.Object => ReadJsonObject(value),
            JsonValueKind.Null => null,
            _ => value.GetRawText(),
        };

    private static object? ReadJsonArray(JsonElement array)
    {
        List<object?> values = [];
        bool allStrings = true;
        foreach (JsonElement item in array.EnumerateArray())
        {
            object? value = ReadJsonValue(item);
            values.Add(value);
            allStrings &= value is string;
        }

        return allStrings ? values.Cast<string>().ToArray() : values.ToArray();
    }

    private static Dictionary<string, object?> ReadJsonObject(JsonElement obj)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (JsonProperty property in obj.EnumerateObject())
        {
            result[property.Name] = ReadJsonValue(property.Value);
        }

        return result;
    }

    private static string? ReadString(JsonElement obj, string propertyName)
        => obj.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static LogLevel ParseLevel(string? level, string streamName)
    {
        if (string.IsNullOrEmpty(level))
        {
            return string.Equals(streamName, HostProcessStreamNames.StandardError, StringComparison.Ordinal)
                ? LogLevel.Error
                : LogLevel.Information;
        }

        return level.ToLowerInvariant() switch
        {
            "trace" => LogLevel.Trace,
            "debug" => LogLevel.Debug,
            "information" or "info" => LogLevel.Information,
            "warning" or "warn" => LogLevel.Warning,
            "error" => LogLevel.Error,
            "critical" => LogLevel.Critical,
            "none" => LogLevel.None,
            _ => LogLevel.Information,
        };
    }

    private static void EnrichFromCategory(string category, IDictionary<string, object?> attributes)
    {
        if (!attributes.ContainsKey(HostLogAttributeKeys.FunctionName)
            && TryGetFunctionNameFromCategory(category, out string? functionName))
        {
            attributes[HostLogAttributeKeys.FunctionName] = functionName;
        }
    }

    private static bool TryGetFunctionNameFromCategory(string category, out string? functionName)
    {
        const string Prefix = "Function.";
        functionName = null;
        if (!category.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        functionName = category[Prefix.Length..];
        const string UserSuffix = ".User";
        if (functionName.EndsWith(UserSuffix, StringComparison.Ordinal))
        {
            functionName = functionName[..^UserSuffix.Length];
        }

        return !string.IsNullOrWhiteSpace(functionName);
    }

    private static void EnrichFromMessage(string message, IDictionary<string, object?> attributes)
    {
        if (TryParseFunctionExecutionMessage(message, out ParsedFunctionExecution? execution))
        {
            ParsedFunctionExecution parsed = execution!;
            attributes[HostLogAttributeKeys.FunctionName] = parsed.FunctionName;
            attributes[HostLogAttributeKeys.FunctionInvocationId] = parsed.InvocationId;
            attributes[HostLogAttributeKeys.CliEventKind] = parsed.Completed
                ? CliEventKinds.InvocationCompleted
                : CliEventKinds.InvocationStarted;

            if (parsed.Completed)
            {
                attributes[HostLogAttributeKeys.FunctionResult] = parsed.Succeeded ? "succeeded" : "failed";
            }

            if (parsed.DurationMs is { } duration)
            {
                attributes[HostLogAttributeKeys.DurationMs] = duration;
            }
        }
    }

    private static bool TryParseFunctionExecutionMessage(string message, out ParsedFunctionExecution? execution)
    {
        execution = null;
        bool completed;
        if (message.StartsWith("Executing '", StringComparison.Ordinal))
        {
            completed = false;
        }
        else if (message.StartsWith("Executed '", StringComparison.Ordinal))
        {
            completed = true;
        }
        else
        {
            return false;
        }

        int nameStart = message.IndexOf('\'', StringComparison.Ordinal);
        if (nameStart < 0)
        {
            return false;
        }

        int nameEnd = message.IndexOf('\'', nameStart + 1);
        if (nameEnd <= nameStart)
        {
            return false;
        }

        string functionName = NormalizeFunctionName(message[(nameStart + 1)..nameEnd]);
        string? invocationId = ReadDelimitedValue(message, "Id=");
        if (string.IsNullOrEmpty(functionName) || string.IsNullOrEmpty(invocationId))
        {
            return false;
        }

        bool succeeded = true;
        if (completed)
        {
            int detailsStart = message.IndexOf('(', nameEnd);
            int firstComma = detailsStart >= 0 ? message.IndexOf(',', detailsStart) : -1;
            if (detailsStart >= 0 && firstComma > detailsStart)
            {
                string result = message[(detailsStart + 1)..firstComma].Trim();
                succeeded = !result.Equals("Failed", StringComparison.OrdinalIgnoreCase);
            }
        }

        double? duration = null;
        string? durationText = ReadDelimitedValue(message, "Duration=");
        if (!string.IsNullOrEmpty(durationText)
            && durationText.EndsWith("ms", StringComparison.OrdinalIgnoreCase))
        {
            durationText = durationText[..^2];
        }

        if (!string.IsNullOrEmpty(durationText)
            && double.TryParse(durationText, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedDuration))
        {
            duration = parsedDuration;
        }

        execution = new ParsedFunctionExecution(functionName, invocationId, completed, succeeded, duration);
        return true;
    }

    private static string NormalizeFunctionName(string rawName)
    {
        const string FunctionsPrefix = "Functions.";
        return rawName.StartsWith(FunctionsPrefix, StringComparison.Ordinal)
            ? rawName[FunctionsPrefix.Length..]
            : rawName;
    }

    private static string? ReadDelimitedValue(string value, string marker)
    {
        int start = value.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length;
        int end = start;
        while (end < value.Length && value[end] is not ',' and not ')' and not ' ')
        {
            end++;
        }

        return end > start ? value[start..end].Trim('\'', '"') : null;
    }

    private sealed class HostProcessException(HostLogExceptionDetails details) : Exception(details.Message)
    {
        public string RemoteType => details.Type;

        public string? RemoteStack => details.Stack;
    }

    private readonly record struct ConsoleLogContext(string Category, LogLevel Level, EventId EventId);

    private sealed record ParsedFunctionExecution(
        string FunctionName,
        string InvocationId,
        bool Completed,
        bool Succeeded,
        double? DurationMs);
}
