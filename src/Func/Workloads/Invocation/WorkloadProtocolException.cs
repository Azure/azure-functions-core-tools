// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Workloads.Invocation;

/// <summary>
/// Raised when an unexpected exception escapes a workload-owned operation.
/// Subclasses <see cref="GracefulException"/> so the existing top-level error
/// handler renders it without a stack trace; carries the owning
/// <see cref="WorkloadInfo"/> for diagnostics and telemetry.
///
/// Distinct from <see cref="WorkloadOperationException"/>, which surfaces
/// host-detected problems with a workload's <em>declarations</em> (option
/// duplicates, etc.); this one surfaces failures from the workload's
/// <em>execution</em>.
/// </summary>
internal sealed class WorkloadProtocolException : GracefulException
{
    public WorkloadProtocolException(WorkloadInfo workload, string message, Exception innerException)
        : base(message, isUserError: false, verboseMessage: innerException?.ToString())
    {
        ArgumentNullException.ThrowIfNull(workload);
        Workload = workload;
        OriginalException = innerException ?? throw new ArgumentNullException(nameof(innerException));
    }

    /// <summary>The workload whose operation produced the unexpected exception.</summary>
    public WorkloadInfo Workload { get; }

    /// <summary>The exception the workload threw before the invoker wrapped it.</summary>
    public Exception OriginalException { get; }
}
