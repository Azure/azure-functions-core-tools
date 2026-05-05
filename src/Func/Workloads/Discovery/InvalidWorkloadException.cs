// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Discovery;

/// <summary>
/// Thrown when an installed workload package fails structural validation.
/// </summary>
/// <remarks>
/// This is a domain exception. Callers (e.g. the workload install command)
/// are responsible for catching it and translating it into whatever
/// user-facing presentation makes sense at the call site.
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
