// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Commands.Start.Initialization;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Console.Terminal;
using Azure.Functions.Cli.Console.Theme;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Hosting.Dashboard;
using Azure.Functions.Cli.Hosting.Dashboard.Rendering;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Commands;

public class RunInvocationPolicyTests
{
    [Theory]
    [InlineData("run", true)]
    [InlineData("start", true)]
    [InlineData("run --no-tui", false)]
    [InlineData("run --format=plain", false)]
    [InlineData("run --format=json", false)]
    [InlineData("run --no-tui --format=compact", true)]
    [InlineData("run --help --format=invalid", false)]
    [InlineData("run --unrecognized-option", false)]
    public void OwnedLifetime_OnlyAppliesToAnInteractiveRunAction(string arguments, bool expected)
    {
        IInteractionService interaction = Substitute.For<IInteractionService>();
        ITerminalSessionFactory factory = Substitute.For<ITerminalSessionFactory>();
        interaction.IsInteractive.Returns(true);
        interaction.Theme.Returns(new DefaultTheme());
        interaction.CanUseFullScreen(factory).Returns(true);
        IPlatform platform = Substitute.For<IPlatform>();
        StartCommand command = new(interaction, new FunctionPalette(), Substitute.For<ICliVersionProvider>(),
            Substitute.For<IStartInitializationRunner>(), Substitute.For<IOptionsMonitor<HostStartupOptions>>(),
            new CompactDashboardShortcutLabels(platform), platform, terminalFactory: factory);
        RootCommand root = new() { Subcommands = { command } };

        ParseResult result = root.Parse(arguments);

        command.UsesFullScreen(result).Should().Be(expected);
    }

    [Fact]
    public void CapabilityFallback_DoesNotChangeParserLifetime()
    {
        IInteractionService interaction = Substitute.For<IInteractionService>();
        ITerminalSessionFactory factory = Substitute.For<ITerminalSessionFactory>();
        interaction.IsInteractive.Returns(true);
        interaction.Theme.Returns(new DefaultTheme());
        interaction.CanUseFullScreen(factory).Returns(false);
        IPlatform platform = Substitute.For<IPlatform>();
        StartCommand command = new(interaction, new FunctionPalette(), Substitute.For<ICliVersionProvider>(),
            Substitute.For<IStartInitializationRunner>(), Substitute.For<IOptionsMonitor<HostStartupOptions>>(),
            new CompactDashboardShortcutLabels(platform), platform, terminalFactory: factory);
        RootCommand root = new() { Subcommands = { command } };

        command.UsesFullScreen(root.Parse("run --format=compact")).Should().BeFalse();
    }
}
