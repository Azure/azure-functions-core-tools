// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO;
using Azure.Functions.Cli.Commands.Start.Azurite.Launching;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands.Start.Azurite.Launching;

public class DockerAzuriteCommandBuilderTests
{
    [Fact]
    public void Build_EmitsExpectedArgvPerDesign()
    {
        var request = new AzuriteLaunchRequest(
            mode: AzuriteLaunchMode.Docker,
            blobPort: 10000,
            queuePort: 10001,
            tablePort: 10002,
            dataPath: "/var/data",
            logPath: "/var/log/azurite/azurite.log",
            dockerImage: AzuriteDockerImage.Default,
            containerName: "func-azurite-abc");

        var (fileName, args) = DockerAzuriteCommandBuilder.Build(request);

        Assert.Equal("docker", fileName);

        string expectedLogDir = Path.GetDirectoryName("/var/log/azurite/azurite.log")!;
        string[] expected =
        [
            "run", "--rm",
            "--name", "func-azurite-abc",
            "-p", "127.0.0.1:10000:10000",
            "-p", "127.0.0.1:10001:10001",
            "-p", "127.0.0.1:10002:10002",
            "-v", "/var/data:/data",
            "-v", $"{expectedLogDir}:/logs",
            AzuriteDockerImage.Default,
            "azurite",
            "-l", "/data",
            "--blobHost", "0.0.0.0",
            "--queueHost", "0.0.0.0",
            "--tableHost", "0.0.0.0",
            "--blobPort", "10000",
            "--queuePort", "10001",
            "--tablePort", "10002",
            "--disableProductStyleUrl",
            "--skipApiVersionCheck",
            "--disableTelemetry",
            "--silent",
            "--debug", "/logs/azurite.log",
        ];

        Assert.Equal(expected, args);
    }

    [Fact]
    public void Build_MapsCustomHostPorts_ToContainerInternalPorts()
    {
        var request = new AzuriteLaunchRequest(
            mode: AzuriteLaunchMode.Docker,
            blobPort: 21000,
            queuePort: 21001,
            tablePort: 21002,
            dataPath: "/data",
            logPath: "/logs/azurite.log",
            dockerImage: AzuriteDockerImage.Default,
            containerName: "func-azurite-test");

        var (_, args) = DockerAzuriteCommandBuilder.Build(request);

        Assert.Contains("127.0.0.1:21000:10000", args);
        Assert.Contains("127.0.0.1:21001:10001", args);
        Assert.Contains("127.0.0.1:21002:10002", args);
    }

    [Fact]
    public void Build_MountsLogDirectory_NotLogFile()
    {
        string logDir = Path.Combine(Path.GetTempPath(), "azurite-logs");
        string logPath = Path.Combine(logDir, "azurite.log");

        var request = new AzuriteLaunchRequest(
            mode: AzuriteLaunchMode.Docker,
            blobPort: 10000,
            queuePort: 10001,
            tablePort: 10002,
            dataPath: "/data",
            logPath: logPath,
            dockerImage: AzuriteDockerImage.Default,
            containerName: "func-azurite-test");

        var (_, args) = DockerAzuriteCommandBuilder.Build(request);

        Assert.Contains($"{logDir}:/logs", args);
        Assert.DoesNotContain(logPath + ":/logs", args);
    }

    [Fact]
    public void Build_UsesPinnedImageByDefault()
    {
        var request = new AzuriteLaunchRequest(
            mode: AzuriteLaunchMode.Docker,
            blobPort: 10000,
            queuePort: 10001,
            tablePort: 10002,
            dataPath: "/data",
            logPath: "/logs/azurite.log",
            dockerImage: AzuriteDockerImage.Default,
            containerName: "func-azurite-test");

        var (_, args) = DockerAzuriteCommandBuilder.Build(request);

        Assert.Contains(AzuriteDockerImage.Default, args);
        Assert.Equal("mcr.microsoft.com/azure-storage/azurite:3.35.0", AzuriteDockerImage.Default);
    }

    [Fact]
    public void Build_HonorsDockerCommandOverride()
    {
        var request = new AzuriteLaunchRequest(
            mode: AzuriteLaunchMode.Docker,
            blobPort: 10000,
            queuePort: 10001,
            tablePort: 10002,
            dataPath: "/data",
            logPath: "/logs/azurite.log",
            dockerImage: AzuriteDockerImage.Default,
            containerName: "func-azurite-test");

        var (fileName, _) = DockerAzuriteCommandBuilder.Build(request, dockerCommand: "/usr/local/bin/podman");

        Assert.Equal("/usr/local/bin/podman", fileName);
    }

    [Fact]
    public void Build_RejectsNativeMode()
    {
        var request = new AzuriteLaunchRequest(
            mode: AzuriteLaunchMode.Native,
            blobPort: 10000,
            queuePort: 10001,
            tablePort: 10002,
            dataPath: "/d",
            logPath: "/d/azurite.log",
            executablePath: "/azurite");

        Assert.Throws<ArgumentException>(() => DockerAzuriteCommandBuilder.Build(request));
    }
}
