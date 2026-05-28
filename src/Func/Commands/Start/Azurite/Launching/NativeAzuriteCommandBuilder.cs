// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;

namespace Azure.Functions.Cli.Commands.Start.Azurite.Launching;

/// <summary>
/// Builds the argv for launching a locally installed Azurite executable per
/// the managed-Azurite design (§9.1).
/// </summary>
internal static class NativeAzuriteCommandBuilder
{
    public static (string FileName, IReadOnlyList<string> Arguments) Build(AzuriteLaunchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Mode != AzuriteLaunchMode.Native)
        {
            throw new ArgumentException(
                $"Expected mode {nameof(AzuriteLaunchMode.Native)} but got {request.Mode}.",
                nameof(request));
        }

        string fileName = request.ExecutablePath!;
        List<string> args =
        [
            "-l", request.DataPath,
            "--blobHost", "127.0.0.1",
            "--queueHost", "127.0.0.1",
            "--tableHost", "127.0.0.1",
            "--blobPort", request.BlobPort.ToString(CultureInfo.InvariantCulture),
            "--queuePort", request.QueuePort.ToString(CultureInfo.InvariantCulture),
            "--tablePort", request.TablePort.ToString(CultureInfo.InvariantCulture),
            "--disableProductStyleUrl",
            "--skipApiVersionCheck",
            "--disableTelemetry",
            "--silent",
            "--debug", request.LogPath,
        ];

        return (fileName, args);
    }
}
