// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Azurite.Processes;

namespace Azure.Functions.Cli.Tests.Commands.Start.Azurite.Processes;

public class WindowsPortOwnershipStrategyTests
{
    private const string NetstatSample =
        "Active Connections\n" +
        "\n" +
        "  Proto  Local Address          Foreign Address        State           PID\n" +
        "  TCP    0.0.0.0:445            0.0.0.0:0              LISTENING       4\n" +
        "  TCP    127.0.0.1:10000        0.0.0.0:0              LISTENING       21988\n" +
        "  TCP    127.0.0.1:10001        0.0.0.0:0              LISTENING       21988\n" +
        "  TCP    [::1]:10000            [::]:0                 LISTENING       21988\n" +
        "  TCP    127.0.0.1:50123        127.0.0.1:443          ESTABLISHED     6120\n";

    private readonly WindowsPortOwnershipStrategy _strategy = new();

    [Fact]
    public void ParseListenerPids_FindsPidForPort()
    {
        IReadOnlyList<int> pids = _strategy.ParseListenerPids(NetstatSample, 10000);

        pids.Should().ContainSingle().Which.Should().Be(21988);
    }

    [Fact]
    public void ParseListenerPids_IgnoresNonListeningEntries()
    {
        _strategy.ParseListenerPids(NetstatSample, 50123).Should().BeEmpty();
    }

    [Fact]
    public void ParseListenerPids_EmptyForUnmatchedPort()
    {
        _strategy.ParseListenerPids(NetstatSample, 44444).Should().BeEmpty();
    }

    [Fact]
    public void ParseListenerPids_IgnoresLocalizedStateColumn()
    {
        // The state column is localized on non-English Windows, so detection
        // keys off the wildcard foreign address rather than the word "LISTENING".
        const string localized = "  TCP    127.0.0.1:10000        0.0.0.0:0              ESCUCHANDO      7777\n";

        _strategy.ParseListenerPids(localized, 10000).Should().ContainSingle().Which.Should().Be(7777);
    }

    [Fact]
    public void ParseCommandLine_TrimsOutput()
    {
        string? commandLine = _strategy.ParseCommandLine("  \"C:\\Program Files\\nodejs\\node.exe\" azurite -l C:\\data \r\n");

        commandLine.Should().Be("\"C:\\Program Files\\nodejs\\node.exe\" azurite -l C:\\data");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseCommandLine_NullWhenEmpty(string standardOutput)
        => _strategy.ParseCommandLine(standardOutput).Should().BeNull();

    [Fact]
    public void BuildListenerLookup_UsesNetstat()
    {
        (string fileName, IReadOnlyList<string> arguments) = _strategy.BuildListenerLookup(10000);

        fileName.Should().Be("netstat");
        arguments.Should().Contain("-a").And.Contain("-n").And.Contain("-o");
    }

    [Fact]
    public void BuildCommandLineLookup_QueriesCimForPid()
    {
        (string fileName, IReadOnlyList<string> arguments) = _strategy.BuildCommandLineLookup(4242);

        fileName.Should().Be("powershell");
        arguments.Should().Contain(argument => argument.Contains("Win32_Process") && argument.Contains("4242"));
    }
}
