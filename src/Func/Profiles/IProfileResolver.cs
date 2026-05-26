// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Profiles;

/// <summary>
/// Resolves the profile active for a CLI command invocation.
/// </summary>
internal interface IProfileResolver
{
    public Task<ProfileResolution> ResolveAsync(ProfileResolutionContext context, CancellationToken cancellationToken);
}
