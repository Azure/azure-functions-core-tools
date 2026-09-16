// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common.Processes;

namespace Azure.Functions.Cli.Commands.Start.Azurite;

/// <inheritdoc cref="IDockerAvailabilityProbe" />
internal sealed class DockerAvailabilityProbe(IProcessRunner processRunner) : IDockerAvailabilityProbe
{
    private const string DockerExecutable = "docker";
    private const int MaxReasonLength = 512;
    private static readonly TimeSpan _versionTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _infoTimeout = TimeSpan.FromSeconds(10);

    private readonly IProcessRunner _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));

    public async Task<DockerAvailability> ProbeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ProcessOutcome versionOutcome = await _processRunner.RunAsync(
            new ProcessRunRequest(DockerExecutable, ["--version"], WorkingDirectory: null, _versionTimeout),
            cancellationToken);

        if (versionOutcome.ExecutableNotFound)
        {
            return new DockerAvailability(
                DockerAvailabilityStatus.ExecutableNotFound,
                "The 'docker' executable was not found on the host.");
        }

        if (versionOutcome.TimedOut)
        {
            return new DockerAvailability(
                DockerAvailabilityStatus.CommandFailed,
                "'docker --version' did not complete within the allotted time.");
        }

        if (versionOutcome.ExitCode != 0)
        {
            return new DockerAvailability(
                DockerAvailabilityStatus.CommandFailed,
                Truncate($"'docker --version' exited with code {versionOutcome.ExitCode}. {versionOutcome.StandardError.Trim()}"));
        }

        string? version = ExtractFirstLine(versionOutcome.StandardOutput);

        ProcessOutcome infoOutcome = await _processRunner.RunAsync(
            new ProcessRunRequest(DockerExecutable, ["info"], WorkingDirectory: null, _infoTimeout),
            cancellationToken);

        if (infoOutcome.ExecutableNotFound)
        {
            return new DockerAvailability(
                DockerAvailabilityStatus.ExecutableNotFound,
                "The 'docker' executable was not found on the host.");
        }

        if (infoOutcome.TimedOut)
        {
            return new DockerAvailability(
                DockerAvailabilityStatus.CommandFailed,
                "'docker info' did not complete within the allotted time.",
                version);
        }

        if (infoOutcome.ExitCode != 0)
        {
            return new DockerAvailability(
                DockerAvailabilityStatus.DaemonUnavailable,
                Truncate($"'docker info' exited with code {infoOutcome.ExitCode}. {infoOutcome.StandardError.Trim()}"),
                version);
        }

        return new DockerAvailability(
            DockerAvailabilityStatus.Available,
            "Docker is installed and the daemon is reachable.",
            version);
    }

    private static string? ExtractFirstLine(string standardOutput)
    {
        if (string.IsNullOrWhiteSpace(standardOutput))
        {
            return null;
        }

        return standardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
    }

    private static string Truncate(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        string trimmed = value.Trim();
        return trimmed.Length <= MaxReasonLength ? trimmed : trimmed[..MaxReasonLength];
    }
}
