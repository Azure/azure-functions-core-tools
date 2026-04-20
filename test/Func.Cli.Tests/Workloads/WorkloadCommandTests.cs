// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads;

public class WorkloadCommandTests
{
    private readonly TestInteractionService _interaction;
    private readonly WorkloadManager _workloadManager;
    private readonly string _tempDir;

    public WorkloadCommandTests()
    {
        _interaction = new TestInteractionService();
        _tempDir = Path.Combine(Path.GetTempPath(), $"func-wl-cmd-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _workloadManager = new WorkloadManager(_interaction, _tempDir);
    }

    [Fact]
    public void CreateCommand_HasWorkloadSubcommand()
    {
        var root = Parser.CreateCommand(_interaction, _workloadManager);
        var names = root.Subcommands.Select(c => c.Name).ToList();

        Assert.Contains("workload", names);
    }

    [Fact]
    public void WorkloadCommand_HasExpectedSubcommands()
    {
        var root = Parser.CreateCommand(_interaction, _workloadManager);
        var workloadCmd = root.Subcommands.First(c => c.Name == "workload");
        var subNames = workloadCmd.Subcommands.Select(c => c.Name).ToList();

        Assert.Contains("install", subNames);
        Assert.Contains("uninstall", subNames);
        Assert.Contains("list", subNames);
        Assert.Contains("update", subNames);
    }

    [Fact]
    public void Parse_WorkloadInstall_DoesNotProduceErrors()
    {
        var root = Parser.CreateCommand(_interaction, _workloadManager);
        var result = root.Parse("workload install Azure.Functions.Cli.Workload.Dotnet");

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Parse_WorkloadInstallWithVersion_DoesNotProduceErrors()
    {
        var root = Parser.CreateCommand(_interaction, _workloadManager);
        var result = root.Parse("workload install Azure.Functions.Cli.Workload.Dotnet --version 1.0.0");

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Parse_WorkloadUninstall_DoesNotProduceErrors()
    {
        var root = Parser.CreateCommand(_interaction, _workloadManager);
        var result = root.Parse("workload uninstall dotnet");

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Parse_WorkloadList_DoesNotProduceErrors()
    {
        var root = Parser.CreateCommand(_interaction, _workloadManager);
        var result = root.Parse("workload list");

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Parse_WorkloadUpdate_DoesNotProduceErrors()
    {
        var root = Parser.CreateCommand(_interaction, _workloadManager);
        var result = root.Parse("workload update dotnet");

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Parse_WorkloadUpdateAll_DoesNotProduceErrors()
    {
        var root = Parser.CreateCommand(_interaction, _workloadManager);
        var result = root.Parse("workload update");

        Assert.Empty(result.Errors);
    }
}
