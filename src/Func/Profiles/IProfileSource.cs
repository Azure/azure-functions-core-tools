// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Profiles;

/// <summary>
/// Loads profile definitions from one source.
/// </summary>
internal interface IProfileSource
{
    public Task<ProfileSourceSnapshot> LoadAsync(ProfileSourceContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Inputs available to profile sources.
/// </summary>
internal sealed record ProfileSourceContext(DirectoryInfo WorkingDirectory);
