// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands.Workload;
using Azure.Functions.Cli.Workloads.Storage;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands.Workload;

public class WorkloadListCommandTests
{
    private readonly TestInteractionService _interaction = new();
    private readonly IGlobalManifestStore _store = Substitute.For<IGlobalManifestStore>();

    [Fact]
    public async Task EmptyManifest_WritesNoWorkloadsHint_ReturnsZero()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<InstalledWorkload>());

        var cmd = new WorkloadListCommand(_interaction, _store);
        var exit = await InvokeAsync(cmd);

        Assert.Equal(0, exit);
        Assert.Contains("HINT: No workloads installed.", _interaction.Lines);
        Assert.DoesNotContain(_interaction.Lines, l => l.StartsWith("TABLE:"));
    }

    [Fact]
    public async Task SingleEntry_WritesTableWithRow()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                CreateInstalled(
                    packageId: "Azure.Functions.Cli.Workload.Dotnet",
                    version: "1.0.0",
                    displayName: ".NET",
                    description: "C# / F# workload.",
                    aliases: new[] { "dotnet", "dotnet-isolated" }),
            });

        var cmd = new WorkloadListCommand(_interaction, _store);
        var exit = await InvokeAsync(cmd);

        Assert.Equal(0, exit);
        Assert.Contains("TABLE: [Package, Aliases, Name, Description, Version]", _interaction.Lines);
        Assert.Contains(
            "  ROW: [Azure.Functions.Cli.Workload.Dotnet, dotnet, dotnet-isolated, .NET, C# / F# workload., 1.0.0]",
            _interaction.Lines);
    }

    [Fact]
    public async Task MissingAliases_RendersDashPlaceholder()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                CreateInstalled(
                    packageId: "Azure.Functions.Cli.Workload.Custom",
                    version: "0.1.0",
                    aliases: Array.Empty<string>()),
            });

        var cmd = new WorkloadListCommand(_interaction, _store);
        await InvokeAsync(cmd);

        var rowLine = _interaction.Lines.Single(l => l.StartsWith("  ROW:"));
        Assert.Contains(", -, ", rowLine);
        Assert.DoesNotContain("\u2014", rowLine);
    }

    [Fact]
    public async Task MultipleEntries_WritesOneRowEach()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                CreateInstalled(packageId: "Pkg.A", version: "1.0.0"),
                CreateInstalled(packageId: "Pkg.B", version: "2.0.0"),
                CreateInstalled(packageId: "Pkg.A", version: "1.1.0"),
            });

        var cmd = new WorkloadListCommand(_interaction, _store);
        await InvokeAsync(cmd);

        var rows = _interaction.Lines.Where(l => l.StartsWith("  ROW:")).ToList();
        Assert.Equal(3, rows.Count);
    }

    private static Task<int> InvokeAsync(WorkloadListCommand cmd, params string[] args)
    {
        var root = new RootCommand();
        root.Subcommands.Add(cmd);
        return root.Parse(new[] { cmd.Name }.Concat(args).ToArray()).InvokeAsync();
    }

    private static InstalledWorkload CreateInstalled(
        string packageId,
        string version,
        string displayName = "Test Workload",
        string description = "A workload for tests.",
        IReadOnlyList<string>? aliases = null)
        => new(
            packageId,
            version,
            new GlobalManifestEntry
            {
                DisplayName = displayName,
                Description = description,
                Aliases = aliases ?? Array.Empty<string>(),
                InstallPath = $"/install/{packageId}/{version}",
                EntryPoint = new EntryPointSpec { Assembly = "test.dll", Type = "T" },
            });
}
