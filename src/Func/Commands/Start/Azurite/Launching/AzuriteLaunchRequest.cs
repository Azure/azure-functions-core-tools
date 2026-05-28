// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Azurite.Launching;

/// <summary>
/// Inputs the launcher needs to start a managed Azurite instance.
/// </summary>
/// <remarks>
/// The record validates its mode-specific requirements in the constructor:
/// <see cref="ExecutablePath"/> is required for <see cref="AzuriteLaunchMode.Native"/>,
/// and <see cref="DockerImage"/> plus <see cref="ContainerName"/> are required
/// for <see cref="AzuriteLaunchMode.Docker"/>.
/// </remarks>
internal sealed record AzuriteLaunchRequest
{
    public AzuriteLaunchRequest(
        AzuriteLaunchMode mode,
        int blobPort,
        int queuePort,
        int tablePort,
        string dataPath,
        string logPath,
        string? executablePath = null,
        string? dockerImage = null,
        string? containerName = null)
    {
        if (string.IsNullOrWhiteSpace(dataPath))
        {
            throw new ArgumentException("Data path must be provided.", nameof(dataPath));
        }

        if (string.IsNullOrWhiteSpace(logPath))
        {
            throw new ArgumentException("Log path must be provided.", nameof(logPath));
        }

        switch (mode)
        {
            case AzuriteLaunchMode.Native:
                if (string.IsNullOrWhiteSpace(executablePath))
                {
                    throw new ArgumentException(
                        "Executable path is required for native launch mode.",
                        nameof(executablePath));
                }

                break;

            case AzuriteLaunchMode.Docker:
                if (string.IsNullOrWhiteSpace(dockerImage))
                {
                    throw new ArgumentException(
                        "Docker image is required for Docker launch mode.",
                        nameof(dockerImage));
                }

                if (string.IsNullOrWhiteSpace(containerName))
                {
                    throw new ArgumentException(
                        "Container name is required for Docker launch mode.",
                        nameof(containerName));
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported launch mode.");
        }

        Mode = mode;
        BlobPort = blobPort;
        QueuePort = queuePort;
        TablePort = tablePort;
        DataPath = dataPath;
        LogPath = logPath;
        ExecutablePath = executablePath;
        DockerImage = dockerImage;
        ContainerName = containerName;
    }

    public AzuriteLaunchMode Mode { get; }

    public int BlobPort { get; }

    public int QueuePort { get; }

    public int TablePort { get; }

    /// <summary>
    /// Absolute path to the data directory Azurite will use for persistence.
    /// </summary>
    public string DataPath { get; }

    /// <summary>
    /// Absolute path to the Azurite debug log file.
    /// </summary>
    public string LogPath { get; }

    /// <summary>
    /// Absolute path to the Azurite executable. Required for
    /// <see cref="AzuriteLaunchMode.Native"/>.
    /// </summary>
    public string? ExecutablePath { get; }

    /// <summary>
    /// Container image and tag. Required for <see cref="AzuriteLaunchMode.Docker"/>.
    /// </summary>
    public string? DockerImage { get; }

    /// <summary>
    /// Container name passed to <c>docker run --name</c>. Required for
    /// <see cref="AzuriteLaunchMode.Docker"/>.
    /// </summary>
    public string? ContainerName { get; }
}
