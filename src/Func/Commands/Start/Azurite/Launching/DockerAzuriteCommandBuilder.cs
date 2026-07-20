// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;

namespace Azure.Functions.Cli.Commands.Start.Azurite.Launching;

/// <summary>
/// Builds the argv for launching Azurite via <c>docker run</c> per the
/// managed-Azurite design (§9.3). Container-internal ports are fixed at
/// 10000/10001/10002 and the request's host-side ports are published to
/// <c>127.0.0.1</c>.
/// </summary>
internal static class DockerAzuriteCommandBuilder
{
    private const int ContainerBlobPort = 10000;
    private const int ContainerQueuePort = 10001;
    private const int ContainerTablePort = 10002;
    private const string ContainerDataPath = "/data";
    private const string ContainerLogDirectory = "/logs";
    private const string ContainerLogPath = "/logs/azurite.log";

    public static (string FileName, IReadOnlyList<string> Arguments) Build(AzuriteLaunchRequest request, string dockerCommand = "docker")
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(dockerCommand))
        {
            throw new ArgumentException("Docker command must be provided.", nameof(dockerCommand));
        }

        if (request.Mode != AzuriteLaunchMode.Docker)
        {
            throw new ArgumentException(
                $"Expected mode {nameof(AzuriteLaunchMode.Docker)} but got {request.Mode}.",
                nameof(request));
        }

        string logDirectory = Path.GetDirectoryName(request.LogPath)
            ?? throw new ArgumentException(
                "Log path must include a directory component.",
                nameof(request));

        List<string> args =
        [
            "run", "--rm",
            "--name", request.ContainerName!,
            "-p", FormatPortMapping(request.BlobPort, ContainerBlobPort),
            "-p", FormatPortMapping(request.QueuePort, ContainerQueuePort),
            "-p", FormatPortMapping(request.TablePort, ContainerTablePort),
            "-v", FormatVolumeMount(request.DataPath, ContainerDataPath),
            "-v", FormatVolumeMount(logDirectory, ContainerLogDirectory),
            request.DockerImage!,
            "azurite",
            "-l", ContainerDataPath,
            "--blobHost", "0.0.0.0",
            "--queueHost", "0.0.0.0",
            "--tableHost", "0.0.0.0",
            "--blobPort", ContainerBlobPort.ToString(CultureInfo.InvariantCulture),
            "--queuePort", ContainerQueuePort.ToString(CultureInfo.InvariantCulture),
            "--tablePort", ContainerTablePort.ToString(CultureInfo.InvariantCulture),
            "--disableProductStyleUrl",
            "--skipApiVersionCheck",
            "--disableTelemetry",
            "--silent",
            "--debug", ContainerLogPath,
        ];

        return (dockerCommand, args);
    }

    private static string FormatPortMapping(int hostPort, int containerPort) =>
        string.Format(CultureInfo.InvariantCulture, "127.0.0.1:{0}:{1}", hostPort, containerPort);

    private static string FormatVolumeMount(string hostPath, string containerPath) =>
        string.Format(CultureInfo.InvariantCulture, "{0}:{1}", hostPath, containerPath);
}
