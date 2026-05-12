// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Resolution;

/// <summary>
/// Reads the worker-runtime hint from <c>local.settings.json</c> in a project
/// directory. Behind an interface so the resolver and its tests do not touch
/// the real filesystem.
/// </summary>
internal interface ILocalSettingsReader
{
    /// <summary>
    /// Returns the value of <c>Values.FUNCTIONS_WORKER_RUNTIME</c> in
    /// <c>&lt;directory&gt;/local.settings.json</c>, or <c>null</c> when the
    /// file is missing, the key is absent, the value is empty, or the file
    /// cannot be parsed. Never throws on bad input.
    /// </summary>
    public string? ReadWorkerRuntime(DirectoryInfo directory);
}
