// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Host.Tests;

public sealed class HostShellTests
{
    [Fact]
    public async Task RunAsync_RunsHostWithProvidedArguments()
    {
        var hostRunner = new TestHostRunner();
        var shell = new HostShell(hostRunner);

        int exitCode = await shell.RunAsync(
            ["--urls", "http://0.0.0.0:7072", "--hostid", "abc"],
            CancellationToken.None);

        exitCode.Should().Be(0);
        hostRunner.Args.Should().Equal(["--urls", "http://0.0.0.0:7072", "--hostid", "abc"]);
        hostRunner.EnableAuth.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_EnableAuth_RemovesShellArgumentAndEnablesHostAuth()
    {
        var hostRunner = new TestHostRunner();
        var shell = new HostShell(hostRunner);

        await shell.RunAsync(
            ["--urls", "http://0.0.0.0:7072", "--enable-auth", "--hostid", "abc"],
            CancellationToken.None);

        hostRunner.Args.Should().Equal(["--urls", "http://0.0.0.0:7072", "--hostid", "abc"]);
        hostRunner.EnableAuth.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_PassesCancellationTokenToHost()
    {
        var hostRunner = new TestHostRunner();
        var shell = new HostShell(hostRunner);
        using CancellationTokenSource cancellationTokenSource = new();

        await shell.RunAsync([], cancellationTokenSource.Token);

        hostRunner.CancellationToken.Should().Be(cancellationTokenSource.Token);
    }

    private sealed class TestHostRunner : IFunctionsHostRunner
    {
        public IReadOnlyList<string> Args { get; private set; } = [];

        public bool EnableAuth { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Task RunAsync(string[] args, bool enableAuth, CancellationToken cancellationToken)
        {
            Args = args;
            EnableAuth = enableAuth;
            CancellationToken = cancellationToken;
            return Task.CompletedTask;
        }
    }
}
