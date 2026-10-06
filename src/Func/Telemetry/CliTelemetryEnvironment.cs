// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Telemetry;

/// <summary>
/// Scopes exporter-only environment switches without changing the Functions host's inherited settings.
/// </summary>
internal sealed class CliTelemetryEnvironment(IProcessEnvironment environment, Action<string, string?> setVariable)
    : IProcessEnvironment, ITelemetryEnvironmentScope
{
    internal const string SdkStatsDisabled = "APPLICATIONINSIGHTS_SDKSTATS_DISABLED";
    internal const string StatsbeatDisabled = "APPLICATIONINSIGHTS_STATSBEAT_DISABLED";
    internal const string ResourceMetricsEnabled = "OTEL_DOTNET_AZURE_MONITOR_ENABLE_RESOURCE_METRICS";

    private readonly IProcessEnvironment _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    private readonly Action<string, string?> _setVariable = setVariable ?? throw new ArgumentNullException(nameof(setVariable));
    private readonly Dictionary<string, string?> _originalValues =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public string? Get(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return _originalValues.TryGetValue(name, out string? value) ? value : _environment.Get(name);
    }

    void ITelemetryEnvironmentScope.Apply()
    {
        Override(SdkStatsDisabled, "true");
        Override(StatsbeatDisabled, "true");
        Override(ResourceMetricsEnabled, "false");
    }

    void ITelemetryEnvironmentScope.RestoreInheritedVariables(IDictionary<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        foreach ((string name, string? value) in _originalValues)
        {
            if (value is null)
            {
                environment.Remove(name);
            }
            else
            {
                environment[name] = value;
            }
        }
    }

    public void Dispose()
    {
        foreach ((string name, string? value) in _originalValues)
        {
            _setVariable(name, value);
        }

        _originalValues.Clear();
    }

    private void Override(string name, string value)
    {
        if (!_originalValues.ContainsKey(name))
        {
            _originalValues.Add(name, _environment.Get(name));
        }

        _setVariable(name, value);
    }
}
