// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Azurite.Launching;

namespace Azure.Functions.Cli.Tests.Commands.Start.Azurite.Launching;

public class AzuriteLaunchRequestTests
{
    [Fact]
    public void Constructor_NativeMode_WithoutExecutablePath_Throws()
    {
        var ex = FluentActions.Invoking(() => new AzuriteLaunchRequest(
            mode: AzuriteLaunchMode.Native,
            blobPort: 10000,
            queuePort: 10001,
            tablePort: 10002,
            dataPath: "/tmp/data",
            logPath: "/tmp/log/azurite.log")).Should().ThrowExactly<ArgumentException>().Which;

        ex.ParamName.Should().Be("executablePath");
    }

    [Fact]
    public void Constructor_DockerMode_WithoutImage_Throws()
    {
        var ex = FluentActions.Invoking(() => new AzuriteLaunchRequest(
            mode: AzuriteLaunchMode.Docker,
            blobPort: 10000,
            queuePort: 10001,
            tablePort: 10002,
            dataPath: "/tmp/data",
            logPath: "/tmp/log/azurite.log",
            containerName: "func-azurite-test")).Should().ThrowExactly<ArgumentException>().Which;

        ex.ParamName.Should().Be("dockerImage");
    }

    [Fact]
    public void Constructor_DockerMode_WithoutContainerName_Throws()
    {
        var ex = FluentActions.Invoking(() => new AzuriteLaunchRequest(
            mode: AzuriteLaunchMode.Docker,
            blobPort: 10000,
            queuePort: 10001,
            tablePort: 10002,
            dataPath: "/tmp/data",
            logPath: "/tmp/log/azurite.log",
            dockerImage: AzuriteDockerImage.Default)).Should().ThrowExactly<ArgumentException>().Which;

        ex.ParamName.Should().Be("containerName");
    }

    [Fact]
    public void Constructor_MissingDataPath_Throws()
    {
        var ex = FluentActions.Invoking(() => new AzuriteLaunchRequest(
            mode: AzuriteLaunchMode.Native,
            blobPort: 10000,
            queuePort: 10001,
            tablePort: 10002,
            dataPath: string.Empty,
            logPath: "/tmp/log/azurite.log",
            executablePath: "/usr/local/bin/azurite")).Should().ThrowExactly<ArgumentException>().Which;

        ex.ParamName.Should().Be("dataPath");
    }

    [Fact]
    public void Constructor_MissingLogPath_Throws()
    {
        var ex = FluentActions.Invoking(() => new AzuriteLaunchRequest(
            mode: AzuriteLaunchMode.Native,
            blobPort: 10000,
            queuePort: 10001,
            tablePort: 10002,
            dataPath: "/tmp/data",
            logPath: string.Empty,
            executablePath: "/usr/local/bin/azurite")).Should().ThrowExactly<ArgumentException>().Which;

        ex.ParamName.Should().Be("logPath");
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

        request.Mode.Should().Be(AzuriteLaunchMode.Native);
        request.ExecutablePath.Should().Be("/usr/local/bin/azurite");
        request.DockerImage.Should().BeNull();
        request.ContainerName.Should().BeNull();
        request.BlobPort.Should().Be(20000);
        request.QueuePort.Should().Be(20001);
        request.TablePort.Should().Be(20002);
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

        request.Mode.Should().Be(AzuriteLaunchMode.Docker);
        request.DockerImage.Should().Be(AzuriteDockerImage.Default);
        request.ContainerName.Should().Be("func-azurite-abc");
        request.ExecutablePath.Should().BeNull();
    }
}
