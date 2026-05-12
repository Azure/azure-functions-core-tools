// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Telemetry;

/// <summary>
/// Constants for OTel attribute / instrument names emitted by the CLI.
/// Values follow the (experimental) OTel CLI semantic conventions and the
/// general OTel resource conventions.
/// </summary>
internal static class TelemetryConventions
{
    // Resource attribute keys (set once on the ResourceBuilder).
    public const string ServiceName = "service.name";
    public const string ServiceVersion = "service.version";
    public const string OsType = "os.type";
    public const string OsArchitecture = "os.architecture";
    public const string ProcessRuntimeDescription = "process.runtime.description";

    // Span / metric tag keys.
    public const string CliCommandName = "cli.command.name";
    public const string ProcessExitCode = "process.exit_code";
    public const string CliWorkloadCount = "cli.workload.count";

    /// <summary>
    /// OTel general semconv attribute used to classify failures. Absent /
    /// empty means success; non-empty is the failure type. We populate it
    /// with <c>Exception.GetType().FullName</c> via
    /// <see cref="ActivityExtensions.Fail"/>. Cardinality is bounded by the
    /// exception types thrown in CLI boot code (a handful), so the same
    /// value works for both span dimensions and metric tags.
    /// </summary>
    public const string ErrorType = "error.type";

    // Activity (span) names.
    public const string WorkloadBootActivityName = "cli.workload.boot";

    // Metric instrument names.
    public const string CommandCountInstrument = "cli.command.count";
    public const string CommandDurationInstrument = "cli.command.duration";
    public const string WorkloadBootDurationInstrument = "cli.workload.boot_duration";
}
