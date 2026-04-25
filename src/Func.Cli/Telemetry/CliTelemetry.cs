// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Runtime.InteropServices;
using Azure.Functions.Cli.Common;
using OpenTelemetry.Resources;

namespace Azure.Functions.Cli.Telemetry;

/// <summary>
/// Holds the singleton <see cref="ActivitySource"/> and <see cref="Meter"/>
/// used to emit CLI telemetry, and exposes the resource attributes that all
/// spans and metrics share.
/// </summary>
/// <remarks>
/// No OpenTelemetry SDK is wired up here. <c>Program.cs</c> conditionally
/// attaches the Azure Monitor exporter at startup; when it doesn't, no
/// listener is subscribed and the .NET diagnostic APIs are effectively
/// no-ops — so call sites stay free of opt-out branches.
/// </remarks>
public static class CliTelemetry
{
    /// <summary>
    /// OTel source / service name. Lowercase, dotted form to match the
    /// convention used by the Functions host (<c>azure.functions.host</c>).
    /// </summary>
    public const string SourceName = "azure.functions.cli";

    /// <summary>
    /// Sentinel value used in dev/local builds where the real instrumentation
    /// key has not been injected via the build-time attribute.
    /// </summary>
    private const string EmptyInstrumentationKey = "00000000-0000-0000-0000-000000000000";

    public static readonly string CliVersion = ResolveCliVersion();

    public static readonly ActivitySource Trace = new(SourceName, CliVersion);

    public static readonly Meter Metric = new(SourceName, CliVersion);

    /// <summary>
    /// Returns the OS / runtime attributes that should be applied to the OTel
    /// resource (in addition to <c>service.name</c> / <c>service.version</c>,
    /// which are added via <c>AddService</c>).
    /// </summary>
    public static IEnumerable<KeyValuePair<string, object>> GetResourceAttributes()
    {
        return new KeyValuePair<string, object>[]
        {
            new(TelemetryConventions.OsType, RuntimeInformation.OSDescription),
            new(TelemetryConventions.OsArchitecture, RuntimeInformation.OSArchitecture.ToString()),
            new(TelemetryConventions.ProcessRuntimeDescription, RuntimeInformation.FrameworkDescription),
        };
    }

    /// <summary>
    /// Adds the CLI's standard service/OS/runtime resource attributes to the
    /// supplied <see cref="ResourceBuilder"/>. Used by the hosted OTel
    /// integration in <c>Program.cs</c>.
    /// </summary>
    public static void ConfigureResource(ResourceBuilder builder)
    {
        builder
            .AddService(serviceName: SourceName, serviceVersion: CliVersion)
            .AddAttributes(GetResourceAttributes());
    }

    /// <summary>
    /// Builds a standalone <see cref="ResourceBuilder"/> with the same
    /// service/OS/runtime attributes used by the live exporters. Intended
    /// for tests and tooling that need the resource without going through
    /// the host.
    /// </summary>
    public static ResourceBuilder CreateResourceBuilder()
    {
        var builder = ResourceBuilder.CreateDefault();
        ConfigureResource(builder);
        return builder;
    }

    /// <summary>
    /// Returns the Azure Monitor connection string when telemetry is
    /// configured (build has a real instrumentation key) and the user has
    /// not opted out.
    /// </summary>
    public static bool TryGetConnectionString([NotNullWhen(true)] out string? connectionString)
    {
        connectionString = null;

        var key = Constants.TelemetryInstrumentationKey;
        if (string.IsNullOrEmpty(key) || key == EmptyInstrumentationKey)
        {
            return false;
        }

        var optOut = Environment.GetEnvironmentVariable(Constants.TelemetryOptOutEnvVar);
        if (!string.IsNullOrEmpty(optOut) &&
            !(optOut.Equals("0", StringComparison.OrdinalIgnoreCase) ||
              optOut.Equals("false", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        connectionString = $"InstrumentationKey={key}";
        return true;
    }

    private static string ResolveCliVersion()
    {
        return typeof(CliTelemetry).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";
    }
}
