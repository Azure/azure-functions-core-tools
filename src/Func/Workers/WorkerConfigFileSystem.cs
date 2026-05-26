// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workers;

/// <summary>
/// Production filesystem access for worker payload validation.
/// </summary>
internal sealed class WorkerConfigFileSystem : IWorkerConfigFileSystem
{
    public bool FileExists(string path) => File.Exists(path);
}
