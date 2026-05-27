// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Workloads.Host;

internal static class HostLogSemanticEnricher
{
    private const string CliEventKind = "cli.event_kind";
    private const string DurationMs = "duration_ms";
    private const string FunctionId = "function.id";
    private const string FunctionInvocationId = "function.invocation_id";
    private const string FunctionName = "function.name";
    private const string FunctionResult = "function.result";
    private const string HostState = "host.state";
    private const string HttpMethod = "http.method";
    private const string HttpStatusCode = "http.status_code";
    private const string HttpTarget = "http.target";
    private const string ParentSpanId = "parent_span_id";
    private const string SpanId = "span_id";
    private const string TraceId = "trace_id";

    public static IReadOnlyDictionary<string, object?> BuildAttributes(
        string category,
        EventId eventId,
        string message,
        IReadOnlyDictionary<string, object?> state,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> scopes)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);

        AddIfPresent(attributes, FunctionName, ReadString(state, "functionName"));
        AddIfPresent(attributes, FunctionId, ReadString(state, "functionId"));
        AddIfPresent(attributes, FunctionInvocationId, ReadString(state, "invocationId"));
        AddTraceAttributes(attributes, scopes);

        if (TryGetFunctionNameFromCategory(category, out string? categoryFunctionName))
        {
            attributes.TryAdd(FunctionName, categoryFunctionName);
        }

        AddHostStateAttributes(attributes, category, eventId, state);
        AddHttpAttributes(attributes, category, eventId, state);
        AddFunctionExecutionAttributes(attributes, message);

        return attributes;
    }

    private static void AddHostStateAttributes(
        IDictionary<string, object?> attributes,
        string category,
        EventId eventId,
        IReadOnlyDictionary<string, object?> state)
    {
        if (!string.Equals(category, "Host.General", StringComparison.Ordinal)
            || eventId.Id != 529
            || !string.Equals(eventId.Name, "HostStateChanged", StringComparison.Ordinal))
        {
            return;
        }

        string? rawState = ReadString(state, "newState");
        string? normalized = NormalizeHostState(rawState);
        if (normalized is null)
        {
            return;
        }

        attributes[CliEventKind] = "host_state_changed";
        attributes[HostState] = normalized;
    }

    private static void AddHttpAttributes(
        IDictionary<string, object?> attributes,
        string category,
        EventId eventId,
        IReadOnlyDictionary<string, object?> state)
    {
        if (!string.Equals(
                category,
                "Microsoft.Azure.WebJobs.Script.WebHost.Middleware.SystemTraceMiddleware",
                StringComparison.Ordinal))
        {
            return;
        }

        if (eventId.Id == 527
            || eventId.Id == 528
            || string.Equals(eventId.Name, "ExecutingHttpRequest", StringComparison.Ordinal)
            || string.Equals(eventId.Name, "ExecutedHttpRequest", StringComparison.Ordinal))
        {
            AddIfPresent(attributes, HttpMethod, ReadString(state, "httpMethod"));
            AddIfPresent(attributes, HttpTarget, ReadString(state, "uri"));
        }

        if (eventId.Id == 528 || string.Equals(eventId.Name, "ExecutedHttpRequest", StringComparison.Ordinal))
        {
            AddNumberIfPresent(attributes, HttpStatusCode, state, "statusCode");
            AddNumberIfPresent(attributes, DurationMs, state, "duration");
        }
    }

    private static void AddFunctionExecutionAttributes(IDictionary<string, object?> attributes, string message)
    {
        if (!TryParseFunctionExecutionMessage(message, out ParsedFunctionExecution? execution))
        {
            return;
        }

        ParsedFunctionExecution parsed = execution!;
        attributes[FunctionName] = parsed.FunctionName;
        attributes[FunctionInvocationId] = parsed.InvocationId;
        attributes[CliEventKind] = parsed.Completed ? "invocation_completed" : "invocation_started";

        if (parsed.Completed)
        {
            attributes[FunctionResult] = parsed.Succeeded ? "succeeded" : "failed";
        }

        if (parsed.DurationMs is { } duration)
        {
            attributes[DurationMs] = duration;
        }
    }

    private static void AddTraceAttributes(
        IDictionary<string, object?> attributes,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> scopes)
    {
        foreach (IReadOnlyDictionary<string, object?> scope in scopes)
        {
            if (!scope.TryGetValue("values", out object? valuesObject)
                || valuesObject is not IReadOnlyDictionary<string, object?> values)
            {
                continue;
            }

            AddIfPresent(attributes, TraceId, ReadString(values, "TraceId"));
            AddIfPresent(attributes, SpanId, ReadString(values, "SpanId"));
            AddIfPresent(attributes, ParentSpanId, ReadString(values, "ParentId"));
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

    private static string? NormalizeHostState(string? rawState)
        => rawState?.ToLowerInvariant() switch
        {
            "default" or "initialized" => "starting",
            "running" => "ready",
            "stopping" or "stopped" or "error" => "stopped",
            _ => null,
        };

    private static void AddIfPresent(IDictionary<string, object?> attributes, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            attributes[key] = value;
        }
    }

    private static void AddNumberIfPresent(
        IDictionary<string, object?> attributes,
        string attributeName,
        IReadOnlyDictionary<string, object?> state,
        string stateName)
    {
        if (!state.TryGetValue(stateName, out object? value) || value is null)
        {
            return;
        }

        if (value is int i)
        {
            attributes[attributeName] = attributeName == DurationMs ? (double)i : i;
        }
        else if (value is long l)
        {
            attributes[attributeName] = attributeName == DurationMs ? (double)l : l;
        }
        else if (value is double d)
        {
            attributes[attributeName] = d;
        }
        else if (double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
        {
            attributes[attributeName] = attributeName == DurationMs ? parsed : (int)parsed;
        }
    }

    private static string? ReadString(IReadOnlyDictionary<string, object?> values, string key)
        => values.TryGetValue(key, out object? value)
            ? Convert.ToString(value, CultureInfo.InvariantCulture)
            : null;

    private sealed record ParsedFunctionExecution(
        string FunctionName,
        string InvocationId,
        bool Completed,
        bool Succeeded,
        double? DurationMs);
}
