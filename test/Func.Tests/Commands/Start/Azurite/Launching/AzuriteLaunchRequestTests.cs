// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Azurite.Launching;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands.Start.Azurite.Launching;

public class AzuriteLaunchRequestTests
{
    [Fact]
    public void Constructor_NativeMode_WithoutExecutablePath_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => new AzuriteLaunchRequest(
            mode: AzuriteLaunchMode.Native,
            blobPort: 10000,
            queuePort: 10001,
            tablePort: 10002,
            dataPath: "/tmp/data",
            logPath: "/tmp/log/azurite.log"));

        Assert.Equal("executablePath", ex.ParamName);
    }

    [Fact]
    public void Constructor_DockerMode_WithoutImage_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => new AzuriteLaunchRequest(
            mode: AzuriteLaunchMode.Docker,
            blobPort: 10000,
            queuePort: 10001,
            tablePort: 10002,
            dataPath: "/tmp/data",
            logPath: "/tmp/log/azurite.log",
            containerName: "func-azurite-test"));

        Assert.Equal("dockerImage", ex.ParamName);
    }

    [Fact]
    public void Constructor_DockerMode_WithoutContainerName_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => new AzuriteLaunchRequest(
            mode: AzuriteLaunchMode.Docker,
            blobPort: 10000,
            queuePort: 10001,
            tablePort: 10002,
            dataPath: "/tmp/data",
            logPath: "/tmp/log/azurite.log",
            dockerImage: AzuriteDockerImage.Default));

        Assert.Equal("containerName", ex.ParamName);
    }

    [Fact]
    public void Constructor_MissingDataPath_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => new AzuriteLaunchRequest(
            mode: AzuriteLaunchMode.Native,
            blobPort: 10000,
            queuePort: 10001,
            tablePort: 10002,
            dataPath: string.Empty,
            logPath: "/tmp/log/azurite.log",
            executablePath: "/usr/local/bin/azurite"));

        Assert.Equal("dataPath", ex.ParamName);
    }

    [Fact]
    public void Constructor_MissingLogPath_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => new AzuriteLaunchRequest(
            mode: AzuriteLaunchMode.Native,
            blobPort: 10000,
            queuePort: 10001,
            tablePort: 10002,
            dataPath: "/tmp/data",
            logPath: string.Empty,
            executablePath: "/usr/local/bin/azurite"));

        Assert.Equal("logPath", ex.ParamName);
    }

    [Fact]
    public void Constructor_Native_ValidInputs_PopulatesProperties()
    {
        var request = new AzuriteLaunchRequest(
            mode: AzuriteLaunchMode.Native,
            blobPort: 20000,
            queuePort: 20001,
            tablePort: 20002,
            dataPath: "/var/data",
            logPath: "/var/log/azurite.log",
            executablePath: "/usr/local/bin/azurite");

        Assert.Equal(AzuriteLaunchMode.Native, request.Mode);
        Assert.Equal("/usr/local/bin/azurite", request.ExecutablePath);
        Assert.Null(request.DockerImage);
        Assert.Null(request.ContainerName);
        Assert.Equal(20000, request.BlobPort);
        Assert.Equal(20001, request.QueuePort);
        Assert.Equal(20002, request.TablePort);
    }

    [Fact]
    public void Constructor_Docker_ValidInputs_PopulatesProperties()
    {
        var request = new AzuriteLaunchRequest(
            mode: AzuriteLaunchMode.Docker,
            blobPort: 10000,
            queuePort: 10001,
            tablePort: 10002,
            dataPath: "/var/data",
            logPath: "/var/log/azurite.log",
            dockerImage: AzuriteDockerImage.Default,
            containerName: "func-azurite-abc");

        Assert.Equal(AzuriteLaunchMode.Docker, request.Mode);
        Assert.Equal(AzuriteDockerImage.Default, request.DockerImage);
        Assert.Equal("func-azurite-abc", request.ContainerName);
        Assert.Null(request.ExecutablePath);
    }
}
