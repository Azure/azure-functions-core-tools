// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands.Workload;
using Azure.Functions.Cli.Common;
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
    public void Install_HasIdArgumentAndOptions()
    {
        var cmd = new WorkloadInstallCommand(_interaction, _installer);
        Assert.Single(cmd.Arguments, a => a.Name == "packageId");
        Assert.Single(cmd.Options, o => o.Name == "--version");
        Assert.Single(cmd.Options, o => o.Name == "--from");
    }

    [Fact]
    public async Task Install_NoFromFlag_FeedAcquisitionNotYetSupported()
    {
        var cmd = new WorkloadInstallCommand(_interaction, _installer);
        var ex = await Assert.ThrowsAsync<GracefulException>(
            () => InvokeAsync(cmd, "Test.Workload"));

        Assert.True(ex.IsUserError);
        Assert.Contains("not yet supported", ex.Message);
        Assert.Contains("--from", ex.Message);
    }

    [Fact]
    public async Task Install_DelegatesToInstaller_WritesSuccess()
    {
        var temp = Directory.CreateTempSubdirectory("install-cmd-").FullName;
        try
        {
            _installer.InstallFromDirectoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new InstalledWorkload(
                    "Test.Workload",
                    "1.0.0",
                    new GlobalManifestEntry
                    {
                        DisplayName = "Test",
                        InstallPath = "/install",
                        EntryPoint = new EntryPointSpec { Assembly = "x.dll", Type = "T" },
                    }));

            var cmd = new WorkloadInstallCommand(_interaction, _installer);
            var exit = await InvokeAsync(cmd, "Test.Workload", "--from", temp);

            Assert.Equal(0, exit);
            await _installer.Received(1).InstallFromDirectoryAsync(temp, Arg.Any<CancellationToken>());
            Assert.Contains(_interaction.Lines, l => l.StartsWith("SUCCESS:") && l.Contains("Test.Workload") && l.Contains("1.0.0"));
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Install_InstallerThrowsGraceful_PropagatesUnchanged()
    {
        var temp = Directory.CreateTempSubdirectory("install-cmd-").FullName;
        try
        {
            _installer.InstallFromDirectoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Throws(new GracefulException("nope", isUserError: true));

            var cmd = new WorkloadInstallCommand(_interaction, _installer);
            await Assert.ThrowsAsync<GracefulException>(
                () => InvokeAsync(cmd, "Test.Workload", "--from", temp));
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Install_FromIdMismatch_WritesHint()
    {
        var temp = Directory.CreateTempSubdirectory("install-cmd-").FullName;
        try
        {
            _installer.InstallFromDirectoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new InstalledWorkload(
                    "Other.Workload",
                    "2.0.0",
                    new GlobalManifestEntry
                    {
                        DisplayName = "Other",
                        InstallPath = "/install",
                        EntryPoint = new EntryPointSpec { Assembly = "x.dll", Type = "T" },
                    }));

            var cmd = new WorkloadInstallCommand(_interaction, _installer);
            await InvokeAsync(cmd, "Test.Workload", "--version", "1.0.0", "--from", temp);

            Assert.Contains(_interaction.Lines, l => l.StartsWith("HINT:") && l.Contains("Other.Workload"));
            Assert.Contains(_interaction.Lines, l => l.StartsWith("HINT:") && l.Contains("2.0.0"));
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    private static Task<int> InvokeAsync(WorkloadInstallCommand cmd, params string[] args)
    {
        var root = new RootCommand();
        root.Subcommands.Add(cmd);
        var config = new System.CommandLine.InvocationConfiguration { EnableDefaultExceptionHandler = false };
        return root.Parse(new[] { cmd.Name }.Concat(args).ToArray()).InvokeAsync(config);
    }
}
