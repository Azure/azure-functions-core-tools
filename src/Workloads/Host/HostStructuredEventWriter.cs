// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Buffers;
using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Workloads.Host;

internal static class HostStructuredEventWriter
{
    public const string Source = "azure-functions-cli-host";

    private const int SchemaVersion = 1;
    private const string CliEventKind = "cli.event_kind";
    private const string FunctionBindings = "function.bindings";
    private const string FunctionEntryPoint = "function.entry_point";
    private const string FunctionHttpMethods = "function.http_methods";
    private const string FunctionId = "function.id";
    private const string FunctionLanguage = "function.language";
    private const string FunctionName = "function.name";
    private const string FunctionRoute = "function.route";
    private const string FunctionScriptFile = "function.script_file";
    private const string FunctionTriggerType = "function.trigger_type";

    private static readonly JsonWriterOptions _writerOptions = new()
    {
        Indented = false,
        SkipValidation = true,
    };

    private static readonly object _consoleSync = new();

    public static void WriteLog(
        string category,
        LogLevel level,
        EventId eventId,
        string message,
        IReadOnlyDictionary<string, object?> attributes,
        Exception? exception = null,
        IReadOnlyDictionary<string, object?>? state = null,
        IReadOnlyList<IReadOnlyDictionary<string, object?>>? scopes = null,
        TextWriter? writer = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(attributes);

        WriteRecord(
            writer ?? SelectWriter(level),
            category,
            level,
            eventId,
            message,
            attributes,
            exception,
            state,
            scopes);
    }

    public static void WriteFunctionDiscovered(FunctionMetadata metadata, TextWriter? writer = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        BindingMetadata? trigger = metadata.Trigger;
        string triggerType = NormalizeTriggerType(trigger?.Type);
        string? route = ResolveRoute(metadata, trigger, triggerType);
        string[] methods = ResolveHttpMethods(trigger);

        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [CliEventKind] = "function_discovered",
            [FunctionName] = metadata.Name,
            [FunctionTriggerType] = triggerType,
            [FunctionBindings] = CreateBindingSnapshot(metadata),
        };

        if (!string.IsNullOrEmpty(metadata.FunctionDirectory))
        {
            attributes[FunctionId] = metadata.FunctionDirectory;
        }

        if (!string.IsNullOrEmpty(metadata.Language))
        {
            attributes[FunctionLanguage] = metadata.Language;
        }

        if (!string.IsNullOrEmpty(metadata.ScriptFile))
        {
            attributes[FunctionScriptFile] = metadata.ScriptFile;
        }

        if (!string.IsNullOrEmpty(metadata.EntryPoint))
        {
            attributes[FunctionEntryPoint] = metadata.EntryPoint;
        }

        if (!string.IsNullOrEmpty(route))
        {
            attributes[FunctionRoute] = route;
        }

        if (methods.Length > 0)
        {
            attributes[FunctionHttpMethods] = methods;
        }

        WriteLog(
            "Host.FunctionMetadata",
            LogLevel.Information,
            new EventId(1000, "FunctionDiscovered"),
            $"Function '{metadata.Name}' discovered ({triggerType}).",
            attributes,
            writer: writer);
    }

    private static void WriteRecord(
        TextWriter writer,
        string category,
        LogLevel level,
        EventId eventId,
        string message,
        IReadOnlyDictionary<string, object?> attributes,
        Exception? exception,
        IReadOnlyDictionary<string, object?>? state,
        IReadOnlyList<IReadOnlyDictionary<string, object?>>? scopes)
    {
        var buffer = new ArrayBufferWriter<byte>(512);
        using (var json = new Utf8JsonWriter(buffer, _writerOptions))
        {
            json.WriteStartObject();
            json.WriteString("source", Source);
            json.WriteNumber("schema_version", SchemaVersion);
            json.WriteString("record_type", "log");
            json.WriteString("timestamp", DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            json.WriteString("category", category);
            json.WriteString("level", ToLevelString(level));
            WriteEventId(json, eventId);
            json.WriteString("message", message);
            WriteProperties(json, "attributes", attributes);
            if (state is not null)
            {
                WriteProperties(json, "state", state);
            }

            if (scopes is not null)
            {
                WriteScopes(json, scopes);
            }

            if (exception is not null)
            {
                WriteException(json, exception);
            }

            json.WriteEndObject();
        }

        lock (_consoleSync)
        {
            writer.WriteLine(Encoding.UTF8.GetString(buffer.WrittenSpan));
            writer.Flush();
        }
    }

    private static TextWriter SelectWriter(LogLevel level)
        => level >= LogLevel.Error ? Console.Error : Console.Out;

    private static void WriteEventId(Utf8JsonWriter writer, EventId eventId)
    {
        writer.WriteStartObject("event_id");
        writer.WriteNumber("id", eventId.Id);
        if (!string.IsNullOrEmpty(eventId.Name))
        {
            writer.WriteString("name", eventId.Name);
        }

        writer.WriteEndObject();
    }

    private static void WriteScopes(Utf8JsonWriter writer, IReadOnlyList<IReadOnlyDictionary<string, object?>> scopes)
    {
        writer.WriteStartArray("scopes");
        foreach (IReadOnlyDictionary<string, object?> scope in scopes)
        {
            WriteObject(writer, scope);
        }

        writer.WriteEndArray();
    }

    private static void WriteProperties(Utf8JsonWriter writer, string name, IReadOnlyDictionary<string, object?> properties)
    {
        writer.WriteStartObject(name);
        foreach (KeyValuePair<string, object?> property in properties)
        {
            WriteProperty(writer, property.Key, property.Value);
        }

        writer.WriteEndObject();
    }

    private static void WriteObject(Utf8JsonWriter writer, IReadOnlyDictionary<string, object?> properties)
    {
        writer.WriteStartObject();
        foreach (KeyValuePair<string, object?> property in properties)
        {
            WriteProperty(writer, property.Key, property.Value);
        }

        writer.WriteEndObject();
    }

    private static void WriteException(Utf8JsonWriter writer, Exception exception)
    {
        writer.WriteStartObject("exception");
        writer.WriteString("type", exception.GetType().FullName ?? exception.GetType().Name);
        writer.WriteString("message", exception.Message);
        if (!string.IsNullOrEmpty(exception.StackTrace))
        {
            writer.WriteString("stack", exception.StackTrace);
        }

        writer.WriteEndObject();
    }

    private static void WriteProperty(Utf8JsonWriter writer, string name, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNull(name);
                break;
            case string s:
                writer.WriteString(name, s);
                break;
            case bool b:
                writer.WriteBoolean(name, b);
                break;
            case byte b:
                writer.WriteNumber(name, b);
                break;
            case sbyte b:
                writer.WriteNumber(name, b);
                break;
            case short s:
                writer.WriteNumber(name, s);
                break;
            case ushort s:
                writer.WriteNumber(name, s);
                break;
            case int i:
                writer.WriteNumber(name, i);
                break;
            case uint i:
                writer.WriteNumber(name, i);
                break;
            case long l:
                writer.WriteNumber(name, l);
                break;
            case ulong l:
                writer.WriteNumber(name, l);
                break;
            case float f:
                writer.WriteNumber(name, f);
                break;
            case double d:
                writer.WriteNumber(name, d);
                break;
            case decimal d:
                writer.WriteNumber(name, d);
                break;
            case Guid g:
                writer.WriteString(name, g);
                break;
            case DateTimeOffset dto:
                writer.WriteString(name, dto.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
                break;
            case DateTime dt:
                writer.WriteString(name, dt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
                break;
            case TimeSpan ts:
                writer.WriteString(name, ts.ToString("c", CultureInfo.InvariantCulture));
                break;
            case Uri uri:
                writer.WriteString(name, uri.ToString());
                break;
            case IReadOnlyDictionary<string, object?> properties:
                WriteProperties(writer, name, properties);
                break;
            case IEnumerable<KeyValuePair<string, object?>> properties:
                writer.WriteStartObject(name);
                foreach (KeyValuePair<string, object?> property in properties)
                {
                    WriteProperty(writer, property.Key, property.Value);
                }

                writer.WriteEndObject();
                break;
            case IEnumerable values:
                writer.WriteStartArray(name);
                foreach (object? item in values)
                {
                    WriteValue(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                writer.WriteString(name, Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string s:
                writer.WriteStringValue(s);
                break;
            case bool b:
                writer.WriteBooleanValue(b);
                break;
            case byte b:
                writer.WriteNumberValue(b);
                break;
            case sbyte b:
                writer.WriteNumberValue(b);
                break;
            case short s:
                writer.WriteNumberValue(s);
                break;
            case ushort s:
                writer.WriteNumberValue(s);
                break;
            case int i:
                writer.WriteNumberValue(i);
                break;
            case uint i:
                writer.WriteNumberValue(i);
                break;
            case long l:
                writer.WriteNumberValue(l);
                break;
            case ulong l:
                writer.WriteNumberValue(l);
                break;
            case float f:
                writer.WriteNumberValue(f);
                break;
            case double d:
                writer.WriteNumberValue(d);
                break;
            case decimal d:
                writer.WriteNumberValue(d);
                break;
            case Guid g:
                writer.WriteStringValue(g);
                break;
            case DateTimeOffset dto:
                writer.WriteStringValue(dto.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
                break;
            case DateTime dt:
                writer.WriteStringValue(dt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
                break;
            case TimeSpan ts:
                writer.WriteStringValue(ts.ToString("c", CultureInfo.InvariantCulture));
                break;
            case Uri uri:
                writer.WriteStringValue(uri.ToString());
                break;
            case IReadOnlyDictionary<string, object?> properties:
                WriteObject(writer, properties);
                break;
            case IEnumerable<KeyValuePair<string, object?>> properties:
                writer.WriteStartObject();
                foreach (KeyValuePair<string, object?> property in properties)
                {
                    WriteProperty(writer, property.Key, property.Value);
                }

                writer.WriteEndObject();
                break;
            case IEnumerable values:
                writer.WriteStartArray();
                foreach (object? item in values)
                {
                    WriteValue(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                writer.WriteStringValue(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> CreateBindingSnapshot(FunctionMetadata metadata)
    {
        var bindings = new List<IReadOnlyDictionary<string, object?>>(metadata.Bindings.Count);
        foreach (BindingMetadata binding in metadata.Bindings)
        {
            var snapshot = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = binding.Name,
                ["type"] = binding.Type,
                ["direction"] = binding.Direction.ToString(),
                ["is_trigger"] = binding.IsTrigger,
            };

            if (!string.IsNullOrEmpty(binding.Connection))
            {
                snapshot["connection"] = binding.Connection;
            }

            foreach (KeyValuePair<string, object> property in binding.Properties)
            {
                if (!snapshot.ContainsKey(property.Key))
                {
                    snapshot[property.Key] = property.Value;
                }
            }

            bindings.Add(snapshot);
        }

        return bindings;
    }

    private static string NormalizeTriggerType(string? triggerType)
    {
        if (string.IsNullOrEmpty(triggerType))
        {
            return "unknown";
        }

        const string Suffix = "Trigger";
        return triggerType.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase)
            ? triggerType[..^Suffix.Length].ToLowerInvariant()
            : triggerType.ToLowerInvariant();
    }

    private static string? ResolveRoute(FunctionMetadata metadata, BindingMetadata? trigger, string triggerType)
    {
        if (trigger is null)
        {
            return null;
        }

        if (string.Equals(triggerType, "http", StringComparison.OrdinalIgnoreCase))
        {
            string route = TryGetStringProperty(trigger, "route") ?? metadata.Name;
            return route.StartsWith('/') ? route : "/api/" + route;
        }

        return TryGetStringProperty(trigger, "queueName")
            ?? TryGetStringProperty(trigger, "path")
            ?? TryGetStringProperty(trigger, "schedule");
    }

    private static string[] ResolveHttpMethods(BindingMetadata? trigger)
    {
        if (trigger?.Properties.TryGetValue("methods", out object? value) != true || value is null)
        {
            return [];
        }

        if (value is IEnumerable enumerable and not string)
        {
            return [
                .. enumerable
                .Cast<object?>()
                .Select(item => Convert.ToString(item, CultureInfo.InvariantCulture))
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item!.ToUpperInvariant())
            ];
        }

        string? single = Convert.ToString(value, CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(single) ? [] : [single.ToUpperInvariant()];
    }

    private static string? TryGetStringProperty(BindingMetadata binding, string propertyName)
        => binding.Properties.TryGetValue(propertyName, out object? value)
            ? Convert.ToString(value, CultureInfo.InvariantCulture)
            : null;

    private static string ToLevelString(LogLevel level) => level switch
    {
        LogLevel.Trace => "trace",
        LogLevel.Debug => "debug",
        LogLevel.Information => "information",
        LogLevel.Warning => "warning",
        LogLevel.Error => "error",
        LogLevel.Critical => "critical",
        LogLevel.None => "none",
        _ => level.ToString().ToLowerInvariant(),
    };
}
