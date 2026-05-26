// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Host;
using Azure.Functions.Cli.Hosting.Events;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands;

public class HostProcessOutputParserTests
{
    [Fact]
    public void ParseLine_WhenStdout_MapsToInformationWithStreamAttribute()
    {
        var parser = new LineHostProcessOutputParser();

        HostLogEntry entry = parser.ParseLine(
            HostProcessStreamNames.StandardOutput,
            "Host started",
            DateTimeOffset.UnixEpoch);

        Assert.Equal("Host.Process", entry.Category);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal("Host started", entry.Message);
        Assert.Equal(HostProcessStreamNames.StandardOutput, entry.GetAttribute<string>(HostLogAttributeKeys.Stream));
    }

    [Fact]
    public void ParseLine_WhenStderr_MapsToErrorWithStreamAttribute()
    {
        var parser = new LineHostProcessOutputParser();

        HostLogEntry entry = parser.ParseLine(
            HostProcessStreamNames.StandardError,
            "Host failed",
            DateTimeOffset.UnixEpoch);

        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal("Host failed", entry.Message);
        Assert.Equal(HostProcessStreamNames.StandardError, entry.GetAttribute<string>(HostLogAttributeKeys.Stream));
    }
}
