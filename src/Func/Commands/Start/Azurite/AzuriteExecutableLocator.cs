// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli;
using Azure.Functions.Cli.Common.Processes;

namespace Azure.Functions.Cli.Commands.Start.Azurite;

/// <inheritdoc cref="IAzuriteExecutableLocator" />
internal sealed class AzuriteExecutableLocator(
    IPlatform platform,
    IAzuriteHostEnvironment hostEnvironment,
    IProcessRunner processRunner) : IAzuriteExecutableLocator
{
    private static readonly TimeSpan _versionProbeTimeout = TimeSpan.FromSeconds(5);
    private static readonly string[] _windowsCandidateNames = ["azurite.cmd", "azurite.exe", "azurite"];
    private static readonly string[] _unixCandidateNames = ["azurite"];

    private readonly IPlatform _platform = platform ?? throw new ArgumentNullException(nameof(platform));
    private readonly IAzuriteHostEnvironment _hostEnvironment = hostEnvironment ?? throw new ArgumentNullException(nameof(hostEnvironment));
    private readonly IProcessRunner _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));

    public async Task<AzuriteExecutable?> FindAsync(string projectRoot, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        cancellationToken.ThrowIfCancellationRequested();

        string? projectLocal = TryFindProjectLocal(projectRoot);
        if (projectLocal is not null)
        {
            string? version = await TryReadVersionAsync(projectLocal, cancellationToken);
            return new AzuriteExecutable(projectLocal, AzuriteExecutableSource.ProjectLocal, version);
        }

        string? pathMatch = TryFindOnPath();
        if (pathMatch is not null)
        {
            string? version = await TryReadVersionAsync(pathMatch, cancellationToken);
            return new AzuriteExecutable(pathMatch, AzuriteExecutableSource.Path, version);
        }

        return null;
    }

    private string? TryFindProjectLocal(string projectRoot)
    {
        string fileName = _platform.IsWindows ? "azurite.cmd" : "azurite";
        string candidate = Path.Combine(projectRoot, "node_modules", ".bin", fileName);
        return _hostEnvironment.ExecutableExists(candidate) ? candidate : null;
    }

    private string? TryFindOnPath()
    {
        string? pathValue = _hostEnvironment.GetPathVariable();
        if (string.IsNullOrEmpty(pathValue))
        {
            return null;
        }

        string[] candidateNames = _platform.IsWindows ? _windowsCandidateNames : _unixCandidateNames;
        char pathSeparator = _platform.IsWindows ? ';' : ':';
        string[] entries = pathValue.Split(pathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Try each candidate name across all PATH entries in the platform's
        // preferred order: on Windows the shell resolves .cmd before .exe
        // before the bare name, so the loop nesting must match that order.
        foreach (string name in candidateNames)
        {
            foreach (string entry in entries)
            {
                string candidate = Path.Combine(entry, name);
                if (_hostEnvironment.ExecutableExists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private async Task<string?> TryReadVersionAsync(string executablePath, CancellationToken cancellationToken)
    {
        ProcessRunRequest request = new(
            FileName: executablePath,
            Arguments: ["--version"],
            WorkingDirectory: null,
            Timeout: _versionProbeTimeout);

        ProcessOutcome outcome;
        try
        {
            outcome = await _processRunner.RunAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // §8.3: a version-check failure must never block discovery.
            return null;
        }

        if (outcome.ExecutableNotFound || outcome.TimedOut || outcome.ExitCode != 0)
        {
            return null;
        }

        return NormalizeVersion(outcome.StandardOutput);
    }

    private static string? NormalizeVersion(string standardOutput)
    {
        if (string.IsNullOrWhiteSpace(standardOutput))
        {
            return null;
        }

        string firstLine = standardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;

        if (firstLine.Length == 0)
        {
            return null;
        }

        if (firstLine.Length > 64)
        {
            firstLine = firstLine[..64];
        }

        return firstLine;
    }
}
