// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Resolution;

/// <summary>
/// Reads the worker-runtime hint from <c>local.settings.json</c>.
/// </summary>
internal interface ILocalSettingsReader
{
    /// <summary>
    /// Returns <c>Values.FUNCTIONS_WORKER_RUNTIME</c>, or <c>null</c> when
    /// the file is missing, malformed, or the key is absent. Never throws.
    /// </summary>
    public string? ReadWorkerRuntime(DirectoryInfo directory);
}
