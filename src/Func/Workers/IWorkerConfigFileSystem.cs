// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workers;

/// <summary>
/// Filesystem seam for validating resolved worker payloads.
/// </summary>
internal interface IWorkerConfigFileSystem
{
    public bool FileExists(string path);
}
