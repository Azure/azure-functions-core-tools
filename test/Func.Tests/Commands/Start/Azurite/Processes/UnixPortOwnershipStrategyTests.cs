// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Azurite.Processes;

namespace Azure.Functions.Cli.Tests.Commands.Start.Azurite.Processes;

public class UnixPortOwnershipStrategyTests
{
    private readonly UnixPortOwnershipStrategy _strategy = new();

    [Fact]
    public void ParseListenerPids_ReadsPidFieldLines()
    {
        _strategy.ParseListenerPids("p12345\nf10\n", 10000)
            .Should().ContainSingle().Which.Should().Be(12345);
    }

    [Fact]
    public void ParseListenerPids_DeduplicatesPids()
    {
        _strategy.ParseListenerPids("p777\np777\n", 10000)
            .Should().ContainSingle().Which.Should().Be(777);
    }

    [Fact]
    public void ParseListenerPids_EmptyWhenNoProcessLines()
    {
        _strategy.ParseListenerPids("f10\nn127.0.0.1:10000\n", 10000).Should().BeEmpty();
    }

    [Fact]
    public void ParseCommandLine_TrimsOutput()
    {
        _strategy.ParseCommandLine("node /usr/local/lib/azurite -l /data\n")
            .Should().Be("node /usr/local/lib/azurite -l /data");
    }

    [Fact]
    public void BuildListenerLookup_UsesLsofForPort()
    {
        (string fileName, IReadOnlyList<string> arguments) = _strategy.BuildListenerLookup(10000);

        fileName.Should().Be("lsof");
        arguments.Should().Contain("-iTCP:10000").And.Contain("-sTCP:LISTEN");
    }

    [Fact]
    public void BuildCommandLineLookup_UsesPsForPid()
    {
        (string fileName, IReadOnlyList<string> arguments) = _strategy.BuildCommandLineLookup(555);

        fileName.Should().Be("ps");
        arguments.Should().Contain("555");
    }
}
