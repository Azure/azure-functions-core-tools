// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Reports workload-owned initialization warnings through the CLI's user-output channel.
/// </summary>
public interface IProjectInitializationReporter
{
    /// <summary>
    /// Writes an actionable warning without changing the initialization outcome.
    /// </summary>
    public void WriteWarning(string message);
}
