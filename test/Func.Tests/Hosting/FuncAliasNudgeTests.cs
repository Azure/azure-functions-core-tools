// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Hosting;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Hosting;

public class FuncAliasNudgeTests
{
    [Theory]
    [InlineData("init")]
    [InlineData("new")]
    [InlineData("setup")]
    [InlineData("start")]
    [InlineData("help")]
    [InlineData("profile set")]
    [InlineData("workload install")]
    [InlineData("unknown")]
    [InlineData("")]
    [InlineData(null)]
    public void Prints_WhenPreviewInteractiveAndAllowedCommand(string? commandName)
    {
        var interaction = new InteractiveTestInteractionService();
        ICliVersionProvider version = Version(isPrerelease: true);

        new FuncAliasNudge(interaction, version).TryPrint(commandName);

        Assert.Contains(interaction.Lines, l => l.Contains("func5", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("--version")]
    public void Silent_OnCommandsWithoutFuncHints(string commandName)
    {
        var interaction = new InteractiveTestInteractionService();
        ICliVersionProvider version = Version(isPrerelease: true);

        new FuncAliasNudge(interaction, version).TryPrint(commandName);

        Assert.Empty(interaction.Lines);
    }

    [Fact]
    public void Silent_WhenNotPreview()
    {
        var interaction = new InteractiveTestInteractionService();
        ICliVersionProvider version = Version(isPrerelease: false);

        new FuncAliasNudge(interaction, version).TryPrint("init");

        Assert.Empty(interaction.Lines);
    }

    [Fact]
    public void Silent_WhenNotInteractive()
    {
        var interaction = new TestInteractionService(); // IsInteractive == false
        ICliVersionProvider version = Version(isPrerelease: true);

        new FuncAliasNudge(interaction, version).TryPrint("init");

        Assert.Empty(interaction.Lines);
    }

    [Fact]
    public void NullArgs_Throw()
    {
        var interaction = new InteractiveTestInteractionService();
        ICliVersionProvider version = Substitute.For<ICliVersionProvider>();

        Assert.Throws<ArgumentNullException>(() => new FuncAliasNudge(null!, version));
        Assert.Throws<ArgumentNullException>(() => new FuncAliasNudge(interaction, null!));
    }

    private static ICliVersionProvider Version(bool isPrerelease)
    {
        ICliVersionProvider version = Substitute.For<ICliVersionProvider>();
        version.IsPrerelease.Returns(isPrerelease);
        return version;
    }

    private sealed class InteractiveTestInteractionService : TestInteractionService
    {
        public override bool IsInteractive => true;
    }
}
