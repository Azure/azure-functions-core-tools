// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// Fetches template content via a targeted git tag fetch. Uses <c>git init</c>
/// + <c>git fetch</c> with an explicit <c>refs/tags/</c> refspec to guarantee
/// only a tag (never a same-named branch) is checked out.
/// </summary>
internal sealed class GitTemplateFetcher(IGitRunner gitRunner, ILogger<GitTemplateFetcher> logger) : ITemplateFetcher
{
    private const string TagRefPrefix = "refs/tags/";

    private readonly IGitRunner _gitRunner = gitRunner ?? throw new ArgumentNullException(nameof(gitRunner));
    private readonly ILogger<GitTemplateFetcher> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public FetchMode Mode => FetchMode.Git;

    /// <inheritdoc />
    public async Task FetchAsync(QuickstartEntry entry, string tempDirectory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (string.IsNullOrWhiteSpace(entry.GitRef))
        {
            throw new ArgumentException(
                $"Template '{entry.Id}' has no GitRef. Only tag-pinned templates are supported.",
                nameof(entry));
        }

        await InitAndFetchTagAsync(entry, tempDirectory, cancellationToken);
        await VerifyTagIntegrityAsync(entry, tempDirectory, cancellationToken);
    }

    /// <summary>
    /// Fetches a specific tag using init + remote add + fetch refspec + checkout.
    /// This avoids <c>git clone --branch</c> which could resolve a branch with the same name.
    /// </summary>
    private async Task InitAndFetchTagAsync(QuickstartEntry entry, string tempDirectory, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Fetching tag {GitRef} from {Url}", entry.GitRef, entry.RepositoryUrl);

        string tagRefspec = $"{TagRefPrefix}{entry.GitRef}:{TagRefPrefix}{entry.GitRef}";

        await _gitRunner.RunAsync(["init", tempDirectory], workingDirectory: null, cancellationToken);

        await _gitRunner.RunAsync(
            ["-C", tempDirectory, "remote", "add", "origin", "--", entry.RepositoryUrl],
            workingDirectory: null,
            cancellationToken);

        try
        {
            await _gitRunner.RunAsync(
                ["-C", tempDirectory, "fetch", "--depth", "1", "--no-tags", "origin", tagRefspec],
                workingDirectory: null,
                cancellationToken);
        }
        catch (GitRunnerException ex)
        {
            throw new InvalidOperationException(
                $"'{entry.GitRef}' is not a tag on '{entry.RepositoryUrl}'. " +
                "Only tag-based git refs are supported. Branch refs (e.g. 'main') are not accepted.",
                ex);
        }

        await _gitRunner.RunAsync(
            ["-C", tempDirectory, "checkout", "--detach", $"{TagRefPrefix}{entry.GitRef}"],
            workingDirectory: null,
            cancellationToken);
    }

    /// <summary>
    /// Verifies the checked-out commit matches the tag's target and that the tag is annotated.
    /// </summary>
    private async Task VerifyTagIntegrityAsync(QuickstartEntry entry, string tempDirectory, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Verifying tag integrity for {GitRef}", entry.GitRef);

        try
        {
            string objectType = await _gitRunner.RunWithOutputAsync(
                ["-C", tempDirectory, "cat-file", "-t", $"{TagRefPrefix}{entry.GitRef}"],
                workingDirectory: null,
                cancellationToken);

            if (!string.Equals(objectType, "tag", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Tag '{entry.GitRef}' in '{entry.RepositoryUrl}' is a lightweight tag (object type: '{objectType}'). " +
                    "Only annotated tags are accepted for template integrity.");
            }
        }
        catch (GitRunnerException ex)
        {
            throw new InvalidOperationException(
                $"Tag '{entry.GitRef}' could not be verified in '{entry.RepositoryUrl}'.",
                ex);
        }

        string headCommit = await _gitRunner.RunWithOutputAsync(
            ["-C", tempDirectory, "rev-parse", "HEAD"],
            workingDirectory: null,
            cancellationToken);

        string tagTarget = await _gitRunner.RunWithOutputAsync(
            ["-C", tempDirectory, "rev-list", "-n", "1", $"{TagRefPrefix}{entry.GitRef}"],
            workingDirectory: null,
            cancellationToken);

        if (!string.Equals(headCommit, tagTarget, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Tag '{entry.GitRef}' target ({tagTarget}) does not match HEAD ({headCommit}). " +
                "The tag may have been tampered with.");
        }
    }
}
