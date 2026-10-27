// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Runtime.InteropServices;
using Azure.Functions.Cli.Common;
using Azure.Monitor.OpenTelemetry.Exporter;
using OpenTelemetry.Resources;

namespace Azure.Functions.Cli.Telemetry;

/// <summary>
/// Holds the singleton <see cref="ActivitySource"/> and <see cref="Meter"/>
/// used to emit CLI telemetry, and exposes the common attributes that all
/// spans and metrics share.
/// </summary>
/// <remarks>
/// No OpenTelemetry SDK is wired up here. <c>Program.cs</c> conditionally
/// attaches the Azure Monitor exporter at startup; when it doesn't, no
/// listener is subscribed and the .NET diagnostic APIs are effectively
/// no-ops — so call sites stay free of opt-out branches.
/// </remarks>
internal static class CliTelemetry
{
    internal const string ShutdownDrainBudgetName =
        "Azure.Monitor.OpenTelemetry.Exporter.ShutdownDrainBudgetMilliseconds";

    /// <summary>
    /// OTel source / service name. Lowercase, dotted form to match the
    /// convention used by the Functions host (<c>azure.functions.host</c>).
    /// </summary>
    public const string SourceName = "azure.functions.cli";

    public static readonly string CliVersion = ResolveCliVersion();

    public static readonly ActivitySource Trace = new(SourceName, CliVersion);

    public static readonly Meter Metric = new(SourceName, CliVersion);

    /// <summary>
    /// Returns the allow-listed dimensions shared by CLI spans and metric measurements.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, object>> GetCommonAttributes()
    {
        return
        [
            new(TelemetryConventions.ServiceName, SourceName),
            new(TelemetryConventions.ServiceVersion, CliVersion),
            new(TelemetryConventions.OsType, RuntimeInformation.OSDescription),
            new(TelemetryConventions.OsArchitecture, RuntimeInformation.OSArchitecture.ToString()),
            new(TelemetryConventions.ProcessRuntimeDescription, RuntimeInformation.FrameworkDescription),
        ];
    }

    /// <summary>
    /// Builds the CLI service identity without ambient resource detectors.
    /// </summary>
    public static ResourceBuilder CreateResourceBuilder()
    {
        return ConfigureResource(ResourceBuilder.CreateEmpty());
    }

    /// <summary>
    /// Keeps service identity in the resource; other dimensions are attached directly to each signal.
    /// </summary>
    public static ResourceBuilder ConfigureResource(ResourceBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Clear()
            .AddService(serviceName: SourceName, serviceVersion: CliVersion);
    }

    internal static void ConfigureExporter(AzureMonitorExporterOptions options, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(connectionString);

        options.ConnectionString = connectionString;
        options.TracesPerSecond = null;
        options.SamplingRatio = 1.0F;
        options.EnableLiveMetrics = false;
        options.EnableStandardMetrics = false;
        options.EnablePerformanceCounters = false;
    }

    internal static void ConfigureProcessSettings()
    {
        AppContext.SetData(ShutdownDrainBudgetName, 0);
    }

    private static readonly HashSet<string> _optOutFalseSentinels =
        new(StringComparer.OrdinalIgnoreCase) { "no", "n", "0", "false", "off" };

    /// <summary>
    /// Returns the Azure Monitor connection string when telemetry is
    /// configured (build has a connection string) and the user has
    /// not opted out.
    /// </summary>
    public static bool TryGetConnectionString([NotNullWhen(true)] out string? connectionString)
    {
        return TryGetConnectionString(Constants.TelemetryConnectionString, out connectionString);
    }

    internal static bool TryGetConnectionString(
        string? configuredConnectionString,
        [NotNullWhen(true)] out string? connectionString)
    {
        connectionString = null;

        if (string.IsNullOrWhiteSpace(configuredConnectionString))
        {
            return false;
        }

        if (IsOptedOut(Constants.TelemetryOptOutEnvVar) ||
            IsOptedOut(Constants.LegacyTelemetryOptOutEnvVar))
        {
            return false;
        }

        connectionString = configuredConnectionString;
        return true;
    }

    private static bool IsOptedOut(string envVarName)
    {
        string? value = Environment.GetEnvironmentVariable(envVarName);
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        // Explicit "off" sentinels mean the user has not opted out;
        // anything else (including unrecognized values) is treated as
        // opt-out, so we fail safe toward not collecting telemetry.
        return !_optOutFalseSentinels.Contains(value.Trim());
    }

    private static string ResolveCliVersion()
    {
        return typeof(CliTelemetry).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";
    }
}
