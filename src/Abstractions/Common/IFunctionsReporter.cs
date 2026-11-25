// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Common;

/// <summary>
/// Reports operation status and log output to the CLI.
/// </summary>
public interface IFunctionsReporter
{
    /// <summary>
    /// Reports an operation's current status.
    /// </summary>
    public void ReportStatus(string message);

    /// <summary>
    /// Writes a plain-text output line. Calls may occur concurrently from separate output streams.
    /// </summary>
    public void WriteLog(string line, FunctionsReportSeverity severity = FunctionsReportSeverity.Info);
}
