// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Workloads;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads;

public class WorkloadCommandTests
{
    private readonly TestInteractionService _interaction = new();

    [Fact]
    public void WorkloadCommand_HasExpectedSubcommands()
    {
        var (host, installer, _) = WorkloadTestFactory.Create(_interaction);
        var cmd = new WorkloadCommand(_interaction, installer, host);

        var names = cmd.Subcommands.Select(s => s.Name).ToList();
        Assert.Contains("install", names);
        Assert.Contains("uninstall", names);
        Assert.Contains("list", names);
        Assert.Contains("search", names);
    }

    [Fact]
    public void WorkloadCommand_RegisteredInParser()
    {
        var root = WorkloadTestFactory.CreateParser(_interaction);
        Assert.Contains(root.Subcommands, s => s.Name == "workload");
    }

    [Fact]
    public async Task ListSubcommand_NoneInstalled_ShowsHelp()
    {
        var root = WorkloadTestFactory.CreateParser(_interaction);
        var exit = await root.Parse(["workload", "list"]).InvokeAsync();
        Assert.Equal(0, exit);
        Assert.Contains(_interaction.Lines, l => l.Contains("No workloads installed"));
    }

    [Fact]
    public async Task SearchSubcommand_PrintsCatalog()
    {
        var root = WorkloadTestFactory.CreateParser(_interaction);
        var exit = await root.Parse(["workload", "search"]).InvokeAsync();
        Assert.Equal(0, exit);
        Assert.Contains(_interaction.Lines, l => l.Contains("TABLE"));
        Assert.Contains(_interaction.Lines, l => l.Contains("sample"));
    }
}
