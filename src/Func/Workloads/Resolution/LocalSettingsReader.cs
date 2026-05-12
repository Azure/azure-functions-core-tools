// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;

namespace Azure.Functions.Cli.Workloads.Resolution;

/// <summary>
/// Default <see cref="ILocalSettingsReader"/>. Reads
/// <c>&lt;directory&gt;/local.settings.json</c> and returns
/// <c>Values.FUNCTIONS_WORKER_RUNTIME</c>. Tolerant of every "this file is
/// not what we expected" failure (missing, malformed, wrong shape, wrong
/// type) so the resolver can fall through to the detector pass on a
/// best-effort basis.
/// </summary>
internal sealed class LocalSettingsReader : ILocalSettingsReader
{
    private const string FileName = "local.settings.json";
    private const string ValuesProperty = "Values";
    private const string WorkerRuntimeKey = "FUNCTIONS_WORKER_RUNTIME";

    public string? ReadWorkerRuntime(DirectoryInfo directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        string path = Path.Combine(directory.FullName, FileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using FileStream stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);

            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty(ValuesProperty, out JsonElement values)
                || values.ValueKind != JsonValueKind.Object
                || !values.TryGetProperty(WorkerRuntimeKey, out JsonElement runtime)
                || runtime.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            string? value = runtime.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
