// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Azurite.Processes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Commands.Start.Azurite.Processes;

public class ListeningProcessInspectorTests
{
    private readonly IProcessRunner _runner = Substitute.For<IProcessRunner>();
    private readonly IPortOwnershipStrategy _strategy = Substitute.For<IPortOwnershipStrategy>();

    private static ProcessOutcome Output(string standardOutput) =>
        new(ExitCode: 0, standardOutput, StandardError: string.Empty, TimedOut: false, ExecutableNotFound: false);

    private static (string FileName, IReadOnlyList<string> Arguments) Cmd(string fileName, params string[] arguments) =>
        (fileName, arguments);

    [Fact]
    public async Task ResolvesSingleProcess_WhenOneListener()
    {
        _strategy.BuildListenerLookup(10000).Returns(Cmd("netstat", "-ano"));
        _strategy.BuildCommandLineLookup(4242).Returns(Cmd("ps", "-p", "4242"));
        _runner.RunAsync(Arg.Is<ProcessRunRequest>(r => r.FileName == "netstat"), Arg.Any<CancellationToken>())
            .Returns(Output("listener"));
        _runner.RunAsync(Arg.Is<ProcessRunRequest>(r => r.FileName == "ps"), Arg.Any<CancellationToken>())
            .Returns(Output("azurite -l /data"));
        _strategy.ParseListenerPids("listener", 10000).Returns([4242]);
        _strategy.ParseCommandLine("azurite -l /data").Returns("azurite -l /data");

        IReadOnlyList<ListeningProcessInfo> processes = await CreateSut().GetListeningProcessesAsync(10000, CancellationToken.None);

        processes.Should().ContainSingle();
        processes[0].ProcessId.Should().Be(4242);
        processes[0].CommandLine.Should().Be("azurite -l /data");
    }

    [Fact]
    public async Task ResolvesAllProcesses_WhenMultipleListeners()
    {
        _strategy.BuildListenerLookup(10000).Returns(Cmd("netstat", "-ano"));
        _strategy.BuildCommandLineLookup(111).Returns(Cmd("cmd111"));
        _strategy.BuildCommandLineLookup(222).Returns(Cmd("cmd222"));
        _runner.RunAsync(Arg.Is<ProcessRunRequest>(r => r.FileName == "netstat"), Arg.Any<CancellationToken>())
            .Returns(Output("listeners"));
        _runner.RunAsync(Arg.Is<ProcessRunRequest>(r => r.FileName == "cmd111"), Arg.Any<CancellationToken>())
            .Returns(Output("out111"));
        _runner.RunAsync(Arg.Is<ProcessRunRequest>(r => r.FileName == "cmd222"), Arg.Any<CancellationToken>())
            .Returns(Output("out222"));
        _strategy.ParseListenerPids("listeners", 10000).Returns([111, 222]);
        _strategy.ParseCommandLine("out111").Returns("first process");
        _strategy.ParseCommandLine("out222").Returns("second process");

        IReadOnlyList<ListeningProcessInfo> processes = await CreateSut().GetListeningProcessesAsync(10000, CancellationToken.None);

        processes.Should().HaveCount(2);
        processes[0].ProcessId.Should().Be(111);
        processes[0].CommandLine.Should().Be("first process");
        processes[1].ProcessId.Should().Be(222);
        processes[1].CommandLine.Should().Be("second process");
    }

    [Fact]
    public async Task ReturnsEmpty_WhenNoListenerPid()
    {
        _strategy.BuildListenerLookup(10000).Returns(Cmd("netstat", "-ano"));
        _runner.RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>()).Returns(Output("nothing"));
        _strategy.ParseListenerPids("nothing", 10000).Returns([]);

        IReadOnlyList<ListeningProcessInfo> processes = await CreateSut().GetListeningProcessesAsync(10000, CancellationToken.None);

        processes.Should().BeEmpty();
    }

    [Fact]
    public async Task ReturnsEmpty_WhenListenerToolMissing()
    {
        _strategy.BuildListenerLookup(10000).Returns(Cmd("lsof", "-Fp"));
        _runner.RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessOutcome(ExitCode: null, StandardOutput: string.Empty, StandardError: string.Empty, TimedOut: false, ExecutableNotFound: true));

        IReadOnlyList<ListeningProcessInfo> processes = await CreateSut().GetListeningProcessesAsync(10000, CancellationToken.None);

        processes.Should().BeEmpty();
    }

    [Fact]
    public async Task ReturnsEmptyCommandLine_WhenCommandLineUnavailable()
    {
        _strategy.BuildListenerLookup(10000).Returns(Cmd("netstat", "-ano"));
        _strategy.BuildCommandLineLookup(4242).Returns(Cmd("ps", "-p", "4242"));
        _runner.RunAsync(Arg.Is<ProcessRunRequest>(r => r.FileName == "netstat"), Arg.Any<CancellationToken>())
            .Returns(Output("listener"));
        _runner.RunAsync(Arg.Is<ProcessRunRequest>(r => r.FileName == "ps"), Arg.Any<CancellationToken>())
            .Returns(new ProcessOutcome(ExitCode: null, StandardOutput: string.Empty, StandardError: string.Empty, TimedOut: true, ExecutableNotFound: false));
        _strategy.ParseListenerPids("listener", 10000).Returns([4242]);

        IReadOnlyList<ListeningProcessInfo> processes = await CreateSut().GetListeningProcessesAsync(10000, CancellationToken.None);

        processes.Should().ContainSingle();
        processes[0].ProcessId.Should().Be(4242);
        processes[0].CommandLine.Should().BeEmpty();
    }

    private ListeningProcessInspector CreateSut() =>
        new(_runner, _strategy, NullLogger<ListeningProcessInspector>.Instance);
}
