// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// Resolves <see cref="FetchMode.Auto"/> into a concrete fetch mode
/// by probing the environment (e.g., git availability).
/// </summary>
internal interface IFetchModeResolver
{
    /// <summary>
    /// Returns the concrete <see cref="FetchMode"/> to use.
    /// If <paramref name="requested"/> is not <see cref="FetchMode.Auto"/>,
    /// returns it unchanged.
    /// </summary>
    public Task<FetchMode> ResolveAsync(FetchMode requested, CancellationToken cancellationToken);
}
