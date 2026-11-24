// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Common;

/// <summary>
/// Discards Functions operation reports.
/// </summary>
public sealed class NullFunctionsReporter : IFunctionsReporter
{
    public static NullFunctionsReporter Instance { get; } = new();

    private NullFunctionsReporter()
    {
    }

    public void ReportStatus(string message)
    {
    }

    public void WriteLog(string line, FunctionsReportSeverity severity = FunctionsReportSeverity.Info)
    {
    }
}
