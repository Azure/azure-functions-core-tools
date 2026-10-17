// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;

#if FUNC_GO_TEMPLATE_HELPERS
namespace Azure.Functions.Cli.Templates.Go.Shared;
#else
namespace Azure.Functions.Cli.Workloads.Go;
#endif

internal sealed record GoProjectPreparationResult(bool Skipped, int? ExitCode, string StandardError);

/// <summary>
/// Exposes the existing init preparation separately from scaffolding, preserving tool failures for the caller.
/// </summary>
internal class GoProjectPreparation
{
    public async Task<GoProjectPreparationResult> PrepareAsync(string workingDirectory, bool skipGoModTidy, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        if (skipGoModTidy)
        {
            return new(true, null, string.Empty);
        }

        (int exitCode, string stderr) = await RunGoModTidyAsync(workingDirectory, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new(false, exitCode, stderr);
    }

    public virtual async Task<(int ExitCode, string Stderr)> RunGoModTidyAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo("go", "mod tidy")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return (-1, "Failed to start 'go' process.");
            }

            string stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return (process.ExitCode, stderr);
        }
        catch (Exception ex)
        {
            // Preserve the legacy initializer's best-effort result contract; PrepareAsync rechecks cancellation.
            return (-1, ex.Message);
        }
    }
}
