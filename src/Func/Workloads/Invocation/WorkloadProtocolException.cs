// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Workloads.Invocation;

/// <summary>
/// Raised when an unexpected exception escapes a workload-owned operation.
/// Subclasses <see cref="GracefulException"/> so the top-level error
/// handler renders it without a stack trace.
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

    public WorkloadInfo Workload { get; }

    public Exception OriginalException { get; }
}
