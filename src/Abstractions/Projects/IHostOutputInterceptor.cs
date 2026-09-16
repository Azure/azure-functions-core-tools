// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Intercepts raw stdout lines from the host process before they enter the log pipeline.
/// Workloads implement this to capture protocol-specific output (e.g. the .NET worker's
/// <c>azfuncjsonlog:</c> lines) and optionally suppress them from normal console rendering.
/// </summary>
public interface IHostOutputInterceptor : IAsyncDisposable
{
    /// <summary>
    /// Inspects a raw stdout line. Returns <c>true</c> if the line was consumed by this
    /// interceptor and should be suppressed from the normal host log stream. Returns
    /// <c>false</c> to let the line flow through parsing and rendering as usual.
    /// </summary>
    public bool TryIntercept(string line);
}
