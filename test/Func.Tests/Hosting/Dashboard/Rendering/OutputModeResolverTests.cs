// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting.Dashboard.Rendering;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Hosting.Dashboard.Rendering;

public class OutputModeResolverTests
{
    [Fact]
    public void Explicit_Mode_AlwaysWins()
    {
        var interaction = Substitute.For<IInteractionService>();
        interaction.IsInteractive.Returns(true);

        Assert.Equal(OutputMode.Json, OutputModeResolver.Resolve(OutputMode.Json, noTui: false, interaction));
        Assert.Equal(OutputMode.Plain, OutputModeResolver.Resolve(OutputMode.Plain, noTui: false, interaction));
    }

    [Fact]
    public void NoTui_IsAliasForPlain_WhenNoExplicitMode()
    {
        var interaction = Substitute.For<IInteractionService>();
        interaction.IsInteractive.Returns(true);

        Assert.Equal(OutputMode.Plain, OutputModeResolver.Resolve(explicitMode: null, noTui: true, interaction));
    }

    [Fact]
    public void Auto_PicksCompact_ForInteractive()
    {
        var interaction = Substitute.For<IInteractionService>();
        interaction.IsInteractive.Returns(true);
        Assert.Equal(OutputMode.Compact, OutputModeResolver.Resolve(null, noTui: false, interaction));
    }

    [Fact]
    public void Auto_PicksPlain_ForNonInteractive()
    {
        var interaction = Substitute.For<IInteractionService>();
        interaction.IsInteractive.Returns(false);
        Assert.Equal(OutputMode.Plain, OutputModeResolver.Resolve(null, noTui: false, interaction));
    }

    [Theory]
    [InlineData("compact", "compact")]
    [InlineData("plain", "plain")]
    [InlineData("json", "json")]
    [InlineData("JSON", "json")]
    [InlineData("  compact  ", "compact")]
    public void TryParse_AcceptsKnownValues(string input, string expectedName)
    {
        Assert.True(OutputModeResolver.TryParse(input, out var mode));
        Assert.Equal(expectedName, mode.ToString().ToLowerInvariant());
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("dashboard")]
    public void TryParse_RejectsUnknownValues(string? input)
    {
        Assert.False(OutputModeResolver.TryParse(input, out _));
    }
}
