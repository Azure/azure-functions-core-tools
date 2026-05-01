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

    // Metric instrument names.
    public const string CommandCountInstrument = "cli.command.count";
    public const string CommandDurationInstrument = "cli.command.duration";
}
