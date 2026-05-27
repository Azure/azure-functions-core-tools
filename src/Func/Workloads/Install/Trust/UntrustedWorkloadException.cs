// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Install.Trust;

/// <summary>
/// Thrown when a workload package fails the trust gate: missing/invalid
/// signature, signing cert not in the bundle, or package id outside the
/// Azure Functions namespace. The user can re-run with
/// <c>--allow-untrusted</c> for local development packages.
/// </summary>
internal sealed class UntrustedWorkloadException : Exception
{
    public UntrustedWorkloadException(string message)
        : base(message)
    {
    }

    public UntrustedWorkloadException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
