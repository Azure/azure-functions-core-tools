// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting.Dashboard.Rendering;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Hosting.Dashboard.Rendering;

public class OutputModeResolverTests
{
    [Fact]
    public void Explicit_Mode_AlwaysWins()
    {
        var interaction = Substitute.For<IInteractionService>();
        interaction.IsInteractive.Returns(true);

        OutputModeResolver.Resolve(OutputMode.Json, noTui: false, interaction).Should().Be(OutputMode.Json);
        OutputModeResolver.Resolve(OutputMode.Plain, noTui: false, interaction).Should().Be(OutputMode.Plain);
    }

    [Fact]
    public void NoTui_IsAliasForPlain_WhenNoExplicitMode()
    {
        var interaction = Substitute.For<IInteractionService>();
        interaction.IsInteractive.Returns(true);

        OutputModeResolver.Resolve(explicitMode: null, noTui: true, interaction).Should().Be(OutputMode.Plain);
    }

    [Fact]
    public void Auto_PicksCompact_ForInteractive()
    {
        var interaction = Substitute.For<IInteractionService>();
        interaction.IsInteractive.Returns(true);
        OutputModeResolver.Resolve(null, noTui: false, interaction).Should().Be(OutputMode.Compact);
    }

    [Fact]
    public void Auto_PicksPlain_ForNonInteractive()
    {
        var interaction = Substitute.For<IInteractionService>();
        interaction.IsInteractive.Returns(false);
        OutputModeResolver.Resolve(null, noTui: false, interaction).Should().Be(OutputMode.Plain);
    }

    [Theory]
    [InlineData("compact", "compact")]
    [InlineData("plain", "plain")]
    [InlineData("json", "json")]
    [InlineData("JSON", "json")]
    [InlineData("  compact  ", "compact")]
    public void TryParse_AcceptsKnownValues(string input, string expectedName)
    {
        OutputModeResolver.TryParse(input, out var mode).Should().BeTrue();
        mode.ToString().ToLowerInvariant().Should().Be(expectedName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("dashboard")]
    public void TryParse_RejectsUnknownValues(string? input)
    {
        OutputModeResolver.TryParse(input, out _).Should().BeFalse();
    }
}
