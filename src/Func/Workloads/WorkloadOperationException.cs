// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Raised when the host detects that a workload-supplied <see cref="FuncCommand"/>
/// declares its options, arguments, or subcommands in a way the CLI cannot
/// honor (duplicate names, null entries, missing metadata, etc.). Carrying the
/// owning <see cref="WorkloadInfo"/> lets the top-level error handler attribute
/// the failure to the workload that produced it instead of presenting a
/// generic <see cref="InvalidOperationException"/>.
///
/// Internal because the throw sites all run inside <see cref="ExternalCommand"/>
/// after the workload's <see cref="IWorkload.Configure"/> has already returned;
/// workload code is never on the call stack and does not need to catch this.
/// </summary>
internal sealed class WorkloadOperationException : Exception
{
    public WorkloadOperationException(WorkloadInfo workload, string message)
        : base(BuildMessage(workload, message))
    {
        Workload = workload;
    }

    public WorkloadOperationException(WorkloadInfo workload, string message, Exception innerException)
        : base(BuildMessage(workload, message), innerException)
    {
        Workload = workload;
    }

    /// <summary>The workload whose contribution triggered the failure.</summary>
    public WorkloadInfo Workload { get; }

    private static string BuildMessage(WorkloadInfo workload, string message)
    {
        ArgumentNullException.ThrowIfNull(workload);
        return $"Workload '{workload.PackageId}' {message}";
    }
}
