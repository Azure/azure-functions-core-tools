// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// Fetches template content via shallow git clone with tag verification.
/// When a <c>gitRef</c> is specified, verifies it is a tag (not a branch)
/// and that the tag is annotated (not lightweight).
/// </summary>
internal sealed class GitTemplateFetcher(IGitRunner gitRunner, ILogger<GitTemplateFetcher> logger) : ITemplateFetcher
{
    private readonly IGitRunner _gitRunner = gitRunner ?? throw new ArgumentNullException(nameof(gitRunner));
    private readonly ILogger<GitTemplateFetcher> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public FetchMode Mode => FetchMode.Git;

    /// <inheritdoc />
    public async Task FetchAsync(QuickstartEntry entry, string tempDirectory, CancellationToken cancellationToken)
    {
        if (entry.GitRef is not null)
        {
            await VerifyTagExistsAsync(entry, cancellationToken);
        }

        await CloneAsync(entry, tempDirectory, cancellationToken);

        if (entry.GitRef is not null)
        {
            await VerifyAnnotatedTagAsync(entry, tempDirectory, cancellationToken);
        }
    }

    /// <summary>
    /// Verifies the gitRef resolves to a tag on the remote. Uses <c>--exit-code</c>
    /// so git returns exit code 2 when no matching tag is found (e.g. the ref is a branch).
    /// </summary>
    private async Task VerifyTagExistsAsync(QuickstartEntry entry, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Verifying tag {GitRef} exists on {Url}", entry.GitRef, entry.RepositoryUrl);

        List<string> args = ["ls-remote", "--tags", "--exit-code", "--", entry.RepositoryUrl, $"refs/tags/{entry.GitRef}"];

        try
        {
            await _gitRunner.RunAsync(args, workingDirectory: null, cancellationToken);
        }
        catch (GitRunnerException ex)
        {
            throw new InvalidOperationException(
                $"'{entry.GitRef}' is not a tag on '{entry.RepositoryUrl}'. " +
                "Only tag-based git refs are supported. Branch refs (e.g. 'main') are not accepted.",
                ex);
        }
    }

    private async Task CloneAsync(QuickstartEntry entry, string tempDirectory, CancellationToken cancellationToken)
    {
        List<string> args = ["clone", "--depth", "1"];

        if (entry.GitRef is not null)
        {
            args.Add("--branch");
            args.Add(entry.GitRef);
        }

        // `--` sentinel prevents url/path from being interpreted as flags
        args.Add("--");
        args.Add(entry.RepositoryUrl);
        args.Add(tempDirectory);

        _logger.LogDebug("Cloning {Url} (ref: {GitRef}) into temp directory", entry.RepositoryUrl, entry.GitRef ?? "HEAD");
        await _gitRunner.RunAsync(args, workingDirectory: null, cancellationToken);
    }

    private async Task VerifyAnnotatedTagAsync(QuickstartEntry entry, string tempDirectory, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Verifying {GitRef} is an annotated tag", entry.GitRef);

        List<string> args = ["cat-file", "-t", $"refs/tags/{entry.GitRef}"];

        try
        {
            string objectType = await _gitRunner.RunWithOutputAsync(args, workingDirectory: tempDirectory, cancellationToken);

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
    }
}
