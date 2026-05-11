// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands.Workload;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands.Workload;

public class WorkloadInstallCommandTests
{
    private readonly TestInteractionService _interaction = new();
    private readonly IWorkloadInstaller _installer = Substitute.For<IWorkloadInstaller>();

    [Fact]
    public void Install_HasWorkloadArgumentAndForceOption()
    {
        var cmd = new WorkloadInstallCommand(_interaction, _installer);
        Assert.Single(cmd.Arguments, a => a.Name == "id");
        Assert.Single(cmd.Options, o => o.Name == "--force");
    }

    [Fact]
    public void Install_NonNupkgArgument_FailsValidation()
    {
        var cmd = new WorkloadInstallCommand(_interaction, _installer);
        var root = new RootCommand();
        root.Subcommands.Add(cmd);

        ParseResult parse = root.Parse([cmd.Name, "Test.Workload"]);

        Assert.NotEmpty(parse.Errors);
        Assert.Contains(parse.Errors, e => e.Message.Contains("not yet supported") && e.Message.Contains(".nupkg"));
    }

    [Fact]
    public async Task Install_NupkgArgument_DelegatesToInstaller_WritesSuccess()
    {
        StubResult(alreadyInstalled: false);

        var cmd = new WorkloadInstallCommand(_interaction, _installer);
        int exit = await InvokeAsync(cmd, "Test.Workload.1.0.0.nupkg");

        Assert.Equal(0, exit);
        await _installer.Received(1).InstallFromPackageAsync("Test.Workload.1.0.0.nupkg", false, Arg.Any<CancellationToken>());
        Assert.Contains(
            _interaction.Lines,
            l => l.StartsWith("SUCCESS:")
                && l.Contains("Installed workload")
                && l.Contains("test.workload")
                && l.Contains("1.0.0")
                && l.Contains("entry point: T"));
    }

    [Fact]
    public async Task Install_AlreadyInstalled_WritesIdempotentMessage()
    {
        StubResult(alreadyInstalled: true);

        var cmd = new WorkloadInstallCommand(_interaction, _installer);
        int exit = await InvokeAsync(cmd, "Test.Workload.1.0.0.nupkg");

        Assert.Equal(0, exit);
        Assert.Contains(
            _interaction.Lines,
            l => l.StartsWith("SUCCESS:")
                && l.Contains("already installed")
                && l.Contains("test.workload"));
    }

    [Fact]
    public async Task Install_ContentKind_MessageMentionsContentPath()
    {
        _installer.InstallFromPackageAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new WorkloadInstallResult(
                new WorkloadEntry
                {
                    PackageId = "test.content",
                    PackageVersion = "1.0.0",
                    Kind = WorkloadKind.Content,
                    Source = "/abs/path/to/test.content.1.0.0.nupkg",
                },
                AlreadyInstalled: false));

        var cmd = new WorkloadInstallCommand(_interaction, _installer);
        int exit = await InvokeAsync(cmd, "test.content.1.0.0.nupkg");

        Assert.Equal(0, exit);
        Assert.Contains(
            _interaction.Lines,
            l => l.StartsWith("SUCCESS:")
                && l.Contains("content at")
                && l.Contains("/abs/path/to/test.content.1.0.0.nupkg"));
    }

    [Fact]
    public async Task Install_ForceFlag_PassesForceToInstaller()
    {
        StubResult(alreadyInstalled: false);

        var cmd = new WorkloadInstallCommand(_interaction, _installer);
        int exit = await InvokeAsync(cmd, "Test.Workload.1.0.0.nupkg", "--force");

        Assert.Equal(0, exit);
        await _installer.Received(1).InstallFromPackageAsync("Test.Workload.1.0.0.nupkg", true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Install_ForceShortAlias_PassesForceToInstaller()
    {
        StubResult(alreadyInstalled: false);

        var cmd = new WorkloadInstallCommand(_interaction, _installer);
        int exit = await InvokeAsync(cmd, "Test.Workload.1.0.0.nupkg", "-f");

        Assert.Equal(0, exit);
        await _installer.Received(1).InstallFromPackageAsync("Test.Workload.1.0.0.nupkg", true, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(typeof(FileNotFoundException), "missing nupkg")]
    [InlineData(typeof(InvalidWorkloadException), "bad package")]
    [InlineData(typeof(InvalidOperationException), "already installed")]
    public async Task Install_InstallerThrowsExpectedFailure_WrappedInGracefulException(Type exceptionType, string message)
    {
        var inner = (Exception)Activator.CreateInstance(exceptionType, message)!;
        _installer.InstallFromPackageAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Throws(inner);

        var cmd = new WorkloadInstallCommand(_interaction, _installer);
        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => InvokeAsync(cmd, "Test.Workload.1.0.0.nupkg"));
        Assert.Equal(message, ex.Message);
        Assert.True(ex.IsUserError);
    }

    [Fact]
    public async Task Install_InstallerThrowsUnexpected_PropagatesUnchanged()
    {
        // Anything outside the catch list is treated as a runtime bug and
        // surfaces unwrapped so Program.cs prints a stack trace.
        _installer.InstallFromPackageAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Throws(new NullReferenceException("oops"));

        var cmd = new WorkloadInstallCommand(_interaction, _installer);
        await Assert.ThrowsAsync<NullReferenceException>(
            () => InvokeAsync(cmd, "Test.Workload.1.0.0.nupkg"));
    }

    private void StubResult(bool alreadyInstalled) =>
        _installer.InstallFromPackageAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new WorkloadInstallResult(
                new WorkloadEntry
                {
                    PackageId = "test.workload",
                    PackageVersion = "1.0.0",
                    EntryPoint = new EntryPointSpec { AssemblyPath = "x.dll", Type = "T" },
                },
                alreadyInstalled));

    private static Task<int> InvokeAsync(WorkloadInstallCommand cmd, params string[] args)
    {
        var root = new RootCommand();
        root.Subcommands.Add(cmd);
        var config = new InvocationConfiguration { EnableDefaultExceptionHandler = false };
        return root.Parse(new[] { cmd.Name }.Concat(args).ToArray()).InvokeAsync(config);
    }
}
