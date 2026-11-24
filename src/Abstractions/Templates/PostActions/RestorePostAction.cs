// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.PostActions;

/// <summary>
/// Processes the standard restore post action using a workload-specific restore operation.
/// </summary>
public abstract class RestorePostAction : IPostActionProcessor
{
    /// <inheritdoc />
    public Guid Id => new("210D431B-A78B-4D2F-B762-4ED3E3EA9025");

    /// <inheritdoc />
    public abstract bool CanProcess(PostActionSelectionContext context);

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">A restore target resolves outside the generated output directory.</exception>
    public async Task<bool> ProcessAsync(PostActionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        string outputDirectory = Path.GetFullPath(context.OutputBasePath);
        IReadOnlyList<string> configuredFiles = context.GetConfiguredFiles("files");
        IEnumerable<string> targets = configuredFiles.Count > 0
            ? configuredFiles
            : context.PrimaryOutputs;

        StringComparer pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        List<string> validatedTargets = [];
        foreach (string target in targets)
        {
            string fullPath = Path.GetFullPathWithinDirectory(target, outputDirectory);
            if (!validatedTargets.Contains(fullPath, pathComparer))
            {
                validatedTargets.Add(fullPath);
            }
        }

        bool allSucceeded = true;
        foreach (string target in validatedTargets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.Reporter.ReportStatus(
                $"Restoring dependencies for '{Path.GetRelativePath(outputDirectory, target)}'.");
            bool succeeded = await RestoreAsync(context, target, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!succeeded)
            {
                allSucceeded = false;
            }
        }

        return allSucceeded;
    }

    /// <summary>
    /// Restores one generated target.
    /// </summary>
    /// <param name="context">The post-action context.</param>
    /// <param name="targetPath">The absolute path of the target to restore.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True when the restore succeeds; otherwise, false.</returns>
    protected abstract Task<bool> RestoreAsync(
        PostActionContext context, string targetPath, CancellationToken cancellationToken);
}
