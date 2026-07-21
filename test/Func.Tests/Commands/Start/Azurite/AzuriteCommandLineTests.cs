// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Azurite;

namespace Azure.Functions.Cli.Tests.Commands.Start.Azurite;

public class AzuriteCommandLineTests
{
    [Theory]
    [InlineData("node /usr/lib/azurite/azurite.js -l /data --blobPort 10000")]
    [InlineData("C:\\Program Files\\nodejs\\node.exe C:\\azurite\\azurite -l C:\\data")]
    [InlineData("AZURITE -l /data")]
    public void LooksLikeAzurite_TrueForAzuriteCommandLines(string commandLine)
        => AzuriteCommandLine.LooksLikeAzurite(commandLine).Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("com.docker.backend --proxy tcp 127.0.0.1:10000")]
    [InlineData("node /usr/lib/something-else/server.js")]
    public void LooksLikeAzurite_FalseOtherwise(string? commandLine)
        => AzuriteCommandLine.LooksLikeAzurite(commandLine).Should().BeFalse();

    [Fact]
    public void TryGetDataDirectory_ExtractsShortFlag()
    {
        bool found = AzuriteCommandLine.TryGetDataDirectory(
            "node azurite -l /home/me/.azure-functions/azurite/data --blobPort 10000",
            out string? dataDirectory);

        found.Should().BeTrue();
        dataDirectory.Should().Be("/home/me/.azure-functions/azurite/data");
    }

    [Fact]
    public void TryGetDataDirectory_ExtractsLongFlag()
    {
        bool found = AzuriteCommandLine.TryGetDataDirectory(
            "azurite --location /var/azurite --silent",
            out string? dataDirectory);

        found.Should().BeTrue();
        dataDirectory.Should().Be("/var/azurite");
    }

    [Fact]
    public void TryGetDataDirectory_HandlesQuotedPathWithSpaces()
    {
        bool found = AzuriteCommandLine.TryGetDataDirectory(
            "\"C:\\Program Files\\nodejs\\node.exe\" azurite -l \"C:\\Users\\John Doe\\.azure-functions\\azurite\\data\" --silent",
            out string? dataDirectory);

        found.Should().BeTrue();
        dataDirectory.Should().Be("C:\\Users\\John Doe\\.azure-functions\\azurite\\data");
    }

    [Theory]
    [InlineData("azurite --location=/var/azurite --silent", "/var/azurite")]
    [InlineData("node azurite.js -l=/data --blobPort 10000", "/data")]
    public void TryGetDataDirectory_ExtractsEqualsForm(string commandLine, string expected)
    {
        bool found = AzuriteCommandLine.TryGetDataDirectory(commandLine, out string? dataDirectory);

        found.Should().BeTrue();
        dataDirectory.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("azurite --silent --blobPort 10000")]
    [InlineData("azurite -l")]
    public void TryGetDataDirectory_FalseWhenNoDirectory(string? commandLine)
    {
        bool found = AzuriteCommandLine.TryGetDataDirectory(commandLine, out string? dataDirectory);

        found.Should().BeFalse();
        dataDirectory.Should().BeNull();
    }
}
