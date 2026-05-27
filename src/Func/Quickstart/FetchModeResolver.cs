// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// Resolves <see cref="FetchMode.Auto"/> by probing for git availability.
/// Falls back to <see cref="FetchMode.Http"/> when git is not on PATH.
/// </summary>
internal sealed class FetchModeResolver(IGitRunner gitRunner) : IFetchModeResolver
{
    private readonly IGitRunner _gitRunner = gitRunner ?? throw new ArgumentNullException(nameof(gitRunner));

    /// <inheritdoc />
    public async Task<FetchMode> ResolveAsync(FetchMode requested, CancellationToken cancellationToken)
    {
        if (requested != FetchMode.Auto)
        {
            return requested;
        }

        string? version = await _gitRunner.TryGetVersionAsync(cancellationToken);
        return version is not null ? FetchMode.Git : FetchMode.Http;
    }
}
