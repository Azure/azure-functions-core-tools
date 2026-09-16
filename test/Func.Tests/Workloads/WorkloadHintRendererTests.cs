// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Tests.Workloads;

public class WorkloadHintRendererTests
{
    private readonly TestInteractionService _interaction = new();
    private readonly WorkloadHintRenderer _renderer;

    public WorkloadHintRendererTests()
    {
        _renderer = new WorkloadHintRenderer(_interaction);
    }

    [Fact]
    public void Render_NoMatchingStack_KnownStack_ShowsSpecificSetupCommand()
    {
        // Arrange
        var hint = new WorkloadHint(
            WorkloadHintKind.NoMatchingStack,
            "initialize a project",
            "node",
            ["dotnet"]);

        // Act
        _renderer.Render(hint);

        // Assert
        _interaction.AllOutput.Should().Contain("No installed stack matches 'node'.");
        _interaction.AllOutput.Should().Contain("func setup --features node");
        _interaction.AllOutput.Should().Contain("func workload search");
    }

    [Fact]
    public void Render_NoMatchingStack_UnknownStack_ShowsFullSetupMenu()
    {
        // Arrange — no installed stacks to filter, so all known stacks appear
        var hint = new WorkloadHint(
            WorkloadHintKind.NoMatchingStack,
            "initialize a project",
            "ruby",
            []);

        // Act
        _renderer.Render(hint);

        // Assert
        _interaction.AllOutput.Should().Contain("No installed stack matches 'ruby'.");
        _interaction.AllOutput.Should().Contain("func setup --features dotnet");
        _interaction.AllOutput.Should().Contain("func setup --features node");
        _interaction.AllOutput.Should().Contain("func setup --features python");
        _interaction.AllOutput.Should().Contain("func workload search");
    }

    [Fact]
    public void Render_NoMatchingStack_UnknownStack_FiltersInstalledFromMenu()
    {
        // Arrange — dotnet is installed, so it shouldn't appear in the setup menu
        var hint = new WorkloadHint(
            WorkloadHintKind.NoMatchingStack,
            "initialize a project",
            "ruby",
            ["dotnet"]);

        // Act
        _renderer.Render(hint);

        // Assert
        _interaction.AllOutput.Should().NotContain("func setup --features dotnet");
        _interaction.AllOutput.Should().Contain("func setup --features node");
        _interaction.AllOutput.Should().Contain("func setup --features python");
    }

    [Fact]
    public void Render_NoMatchingStack_KnownStack_ListsInstalledStacks()
    {
        // Arrange
        var hint = new WorkloadHint(
            WorkloadHintKind.NoMatchingStack,
            "initialize a project",
            "python",
            ["dotnet", "node"]);

        // Act
        _renderer.Render(hint);

        // Assert
        _interaction.AllOutput.Should().Contain("Installed stacks:");
        _interaction.AllOutput.Should().Contain("dotnet");
        _interaction.AllOutput.Should().Contain("node");
    }

    [Fact]
    public void Render_NoWorkloadsInstalled_ShowsSetupGuidance()
    {
        // Arrange
        var hint = new WorkloadHint(
            WorkloadHintKind.NoWorkloadsInstalled,
            "initialize a project",
            null,
            []);

        // Act
        _renderer.Render(hint);

        // Assert
        _interaction.AllOutput.Should().Contain("No stacks installed.");
        _interaction.AllOutput.Should().Contain("func setup --features dotnet");
        _interaction.AllOutput.Should().Contain("func workload search");
    }

    [Fact]
    public void Render_AmbiguousStackChoice_ListsInstalledStacks()
    {
        // Arrange
        var hint = new WorkloadHint(
            WorkloadHintKind.AmbiguousStackChoice,
            "initialize a project",
            null,
            ["dotnet", "node"]);

        // Act
        _renderer.Render(hint);

        // Assert
        _interaction.AllOutput.Should().Contain("Multiple stacks installed; pass --stack <name> to choose.");
        _interaction.AllOutput.Should().Contain("dotnet");
        _interaction.AllOutput.Should().Contain("node");
    }

    [Fact]
    public void Render_AutoSelectedSoleWorkload_ShowsSelectedStack()
    {
        // Arrange
        var hint = new WorkloadHint(
            WorkloadHintKind.AutoSelectedSoleWorkload,
            "initialize a project",
            "dotnet",
            ["dotnet"]);

        // Act
        _renderer.Render(hint);

        // Assert
        _interaction.AllOutput.Should().Contain("Auto-selecting 'dotnet'");
        _interaction.AllOutput.Should().Contain("func workload search");
    }
}
