// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Discards project host-run reports.
/// </summary>
public sealed class NullFunctionsProjectHostRunReporter : IFunctionsProjectHostRunReporter
{
    public static NullFunctionsProjectHostRunReporter Instance { get; } = new();

    private NullFunctionsProjectHostRunReporter()
    {
    }

    public void ReportStatus(string message)
    {
    }

    public void WriteLog(string line, FunctionsProjectReportSeverity severity = FunctionsProjectReportSeverity.Info)
    {
    }
}
