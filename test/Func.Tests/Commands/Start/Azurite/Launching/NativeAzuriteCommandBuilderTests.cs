// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Azurite.Launching;

namespace Azure.Functions.Cli.Tests.Commands.Start.Azurite.Launching;

public class NativeAzuriteCommandBuilderTests
{
    [Fact]
    public void Build_EmitsExpectedArgvPerDesign()
    {
        var request = new AzuriteLaunchRequest(
            mode: AzuriteLaunchMode.Native,
            blobPort: 10000,
            queuePort: 10001,
            tablePort: 10002,
            dataPath: "/var/data",
            logPath: "/var/log/azurite.log",
            executablePath: "/usr/local/bin/azurite");

        var (fileName, args) = NativeAzuriteCommandBuilder.Build(request);

        fileName.Should().Be("/usr/local/bin/azurite");

        string[] expected =
        [
            "-l", "/var/data",
            "--blobHost", "127.0.0.1",
            "--queueHost", "127.0.0.1",
            "--tableHost", "127.0.0.1",
            "--blobPort", "10000",
            "--queuePort", "10001",
            "--tablePort", "10002",
            "--disableProductStyleUrl",
            "--skipApiVersionCheck",
            "--disableTelemetry",
            "--silent",
            "--debug", "/var/log/azurite.log",
        ];

        args.Should().Equal(expected);
    }

    [Fact]
    public void Build_HonorsCustomPorts()
    {
        var request = new AzuriteLaunchRequest(
            mode: AzuriteLaunchMode.Native,
            blobPort: 20000,
            queuePort: 30000,
            tablePort: 40000,
            dataPath: "/d",
            logPath: "/d/azurite.log",
            executablePath: "/azurite");

        var (_, args) = NativeAzuriteCommandBuilder.Build(request);

        args.Should().Contain("20000");
        args.Should().Contain("30000");
        args.Should().Contain("40000");
    }

    [Fact]
    public void Build_RejectsDockerMode()
    {
        var request = new AzuriteLaunchRequest(
            mode: AzuriteLaunchMode.Docker,
            blobPort: 10000,
            queuePort: 10001,
            tablePort: 10002,
            dataPath: "/d",
            logPath: "/d/azurite.log",
            dockerImage: AzuriteDockerImage.Default,
            containerName: "func-azurite-x");

        FluentActions.Invoking(() => NativeAzuriteCommandBuilder.Build(request)).Should().ThrowExactly<ArgumentException>();
    }
}
