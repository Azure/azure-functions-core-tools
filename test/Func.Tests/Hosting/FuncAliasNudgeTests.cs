// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Hosting;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Hosting;

public class FuncAliasNudgeTests
{
    [Fact]
    public void Prints_OnFailedInvocation()
    {
        var interaction = new InteractiveTestInteractionService();
        ICliVersionProvider version = Version(isPrerelease: true);

        new FuncAliasNudge(interaction, version).TryPrint(exitCode: 1, isBareInvocation: false);

        Assert.Contains(interaction.Lines, l => l.Contains("func5", StringComparison.Ordinal));
    }

    [Fact]
    public void Prints_OnBareInvocation_EvenWhenExitCodeZero()
    {
        var interaction = new InteractiveTestInteractionService();
        ICliVersionProvider version = Version(isPrerelease: true);

        new FuncAliasNudge(interaction, version).TryPrint(exitCode: 0, isBareInvocation: true);

        Assert.Contains(interaction.Lines, l => l.Contains("func5", StringComparison.Ordinal));
    }

    [Fact]
    public void Silent_OnSuccessfulNonBareInvocation()
    {
        var interaction = new InteractiveTestInteractionService();
        ICliVersionProvider version = Version(isPrerelease: true);

        new FuncAliasNudge(interaction, version).TryPrint(exitCode: 0, isBareInvocation: false);

        Assert.Empty(interaction.Lines);
    }

    [Fact]
    public void Silent_WhenNotPreview()
    {
        var interaction = new InteractiveTestInteractionService();
        ICliVersionProvider version = Version(isPrerelease: false);

        new FuncAliasNudge(interaction, version).TryPrint(exitCode: 1, isBareInvocation: true);

        Assert.Empty(interaction.Lines);
    }

    [Fact]
    public void Silent_WhenNotInteractive()
    {
        var interaction = new TestInteractionService(); // IsInteractive == false
        ICliVersionProvider version = Version(isPrerelease: true);

        new FuncAliasNudge(interaction, version).TryPrint(exitCode: 1, isBareInvocation: true);

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
