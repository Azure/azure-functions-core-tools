// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Reports project preparation status and log output before the host starts.
/// </summary>
public interface IFunctionsProjectHostRunReporter
{
    public void ReportStatus(string message);

    public void WriteLog(string line, FunctionsProjectReportSeverity severity = FunctionsProjectReportSeverity.Info);
}
