// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Profiles;

/// <summary>
/// Filesystem access for profile documents.
/// </summary>
internal interface IProfileFileSystem
{
    public Task<string?> ReadAllTextIfExistsAsync(string path, CancellationToken cancellationToken);
}
