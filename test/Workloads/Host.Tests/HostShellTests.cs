// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Xunit;

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

        Assert.Equal(0, exitCode);
        Assert.Equal(["--urls", "http://0.0.0.0:7072", "--hostid", "abc"], hostRunner.Args);
        Assert.False(hostRunner.EnableAuth);
    }

    [Fact]
    public async Task RunAsync_EnableAuth_RemovesShellArgumentAndEnablesHostAuth()
    {
        var hostRunner = new TestHostRunner();
        var shell = new HostShell(hostRunner);

        await shell.RunAsync(
            ["--urls", "http://0.0.0.0:7072", "--enable-auth", "--hostid", "abc"],
            CancellationToken.None);

        Assert.Equal(["--urls", "http://0.0.0.0:7072", "--hostid", "abc"], hostRunner.Args);
        Assert.True(hostRunner.EnableAuth);
    }

    [Fact]
    public async Task RunAsync_PassesCancellationTokenToHost()
    {
        var hostRunner = new TestHostRunner();
        var shell = new HostShell(hostRunner);
        using CancellationTokenSource cancellationTokenSource = new();

        await shell.RunAsync([], cancellationTokenSource.Token);

        Assert.Equal(cancellationTokenSource.Token, hostRunner.CancellationToken);
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
