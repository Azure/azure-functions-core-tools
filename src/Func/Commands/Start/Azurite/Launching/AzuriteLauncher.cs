// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using System.Diagnostics;

namespace Azure.Functions.Cli.Commands.Start.Azurite.Launching;

/// <summary>
/// Launches Azurite in either native or Docker mode. The launcher is pure
/// mechanism: it builds the right argv, starts the process, and wraps it in
/// an <see cref="IAzuriteProcess"/> handle. Readiness probing and orchestration
/// live elsewhere.
/// </summary>
internal sealed class AzuriteLauncher : IAzuriteLauncher
{
    private readonly string _dockerCommand;

    public AzuriteLauncher()
        : this("docker")
    {
    }

    /// <summary>
    /// Test seam: lets tests substitute the Docker executable used to launch
    /// containers and to issue graceful stops.
    /// </summary>
    internal AzuriteLauncher(string dockerCommand)
    {
        if (string.IsNullOrWhiteSpace(dockerCommand))
        {
            throw new ArgumentException("Docker command must be provided.", nameof(dockerCommand));
        }

        _dockerCommand = dockerCommand;
    }

    public Task<IAzuriteProcess> StartAsync(AzuriteLaunchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        (string fileName, IReadOnlyList<string> arguments) = request.Mode switch
        {
            AzuriteLaunchMode.Native => NativeAzuriteCommandBuilder.Build(request),
            AzuriteLaunchMode.Docker => DockerAzuriteCommandBuilder.Build(request, _dockerCommand),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Mode, "Unsupported launch mode."),
        };

        ProcessStartInfo psi = new()
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (string arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        Process process = new() { StartInfo = psi };

        try
        {
            if (!process.Start())
            {
                process.Dispose();
                throw new AzuriteLaunchException(
                    request.Mode,
                    fileName,
                    $"Failed to start Azurite via '{fileName}'.");
            }
        }
        catch (Win32Exception ex)
        {
            process.Dispose();
            throw new AzuriteLaunchException(
                request.Mode,
                fileName,
                $"Failed to start Azurite via '{fileName}': {ex.Message}",
                ex);
        }
        catch (Exception ex) when (ex is not AzuriteLaunchException)
        {
            process.Dispose();
            throw new AzuriteLaunchException(
                request.Mode,
                fileName,
                $"Failed to start Azurite via '{fileName}': {ex.Message}",
                ex);
        }

        IAzuriteProcess handle = new AzuriteProcess(
            process,
            request.Mode,
            request.ContainerName,
            _dockerCommand);

        return Task.FromResult(handle);
    }
}
