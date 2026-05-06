// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Discovery;

/// <summary>
/// Thrown when an installed workload package fails structural validation,
/// for example its <c>workload.json</c> is missing, malformed, or omits a
/// required property.
/// </summary>
/// <remarks>
/// Domain exception. Callers (e.g. the workload install command) catch it
/// and translate it into whatever user-facing presentation makes sense at
/// the call site.
/// </remarks>
internal sealed class InvalidWorkloadException : Exception
{
    public InvalidWorkloadException(string message)
        : base(message)
    {
    }

    public InvalidWorkloadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
