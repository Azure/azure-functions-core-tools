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
    public void Prints_WhenPreviewInteractiveAndConflictDetected()
    {
        var interaction = new InteractiveTestInteractionService();
        IFuncInvocation invocation = Conflict("/foreign/func");
        ICliVersionProvider version = Version(isPrerelease: true);

        new FuncAliasNudge(interaction, invocation, version).TryPrint();

        Assert.Contains(interaction.Lines, l => l.Contains("/foreign/func", StringComparison.Ordinal));
        Assert.Contains(interaction.Lines, l => l.Contains("func5", StringComparison.Ordinal));
    }

    [Fact]
    public void Silent_WhenNotPreview()
    {
        var interaction = new InteractiveTestInteractionService();
        IFuncInvocation invocation = Conflict("/foreign/func");
        ICliVersionProvider version = Version(isPrerelease: false);

        new FuncAliasNudge(interaction, invocation, version).TryPrint();

        Assert.Empty(interaction.Lines);
    }

    [Fact]
    public void Silent_WhenNotInteractive()
    {
        var interaction = new TestInteractionService(); // IsInteractive == false
        IFuncInvocation invocation = Conflict("/foreign/func");
        ICliVersionProvider version = Version(isPrerelease: true);

        new FuncAliasNudge(interaction, invocation, version).TryPrint();

        Assert.Empty(interaction.Lines);
    }

    [Fact]
    public void Silent_WhenNoConflict()
    {
        var interaction = new InteractiveTestInteractionService();
        IFuncInvocation invocation = Substitute.For<IFuncInvocation>();
        invocation.ConflictDetected.Returns(false);
        ICliVersionProvider version = Version(isPrerelease: true);

        new FuncAliasNudge(interaction, invocation, version).TryPrint();

        Assert.Empty(interaction.Lines);
    }

    [Fact]
    public void NullArgs_Throw()
    {
        var interaction = new InteractiveTestInteractionService();
        IFuncInvocation invocation = Substitute.For<IFuncInvocation>();
        ICliVersionProvider version = Substitute.For<ICliVersionProvider>();

        Assert.Throws<ArgumentNullException>(() => new FuncAliasNudge(null!, invocation, version));
        Assert.Throws<ArgumentNullException>(() => new FuncAliasNudge(interaction, null!, version));
        Assert.Throws<ArgumentNullException>(() => new FuncAliasNudge(interaction, invocation, null!));
    }

    private static IFuncInvocation Conflict(string path)
    {
        IFuncInvocation invocation = Substitute.For<IFuncInvocation>();
        invocation.ConflictDetected.Returns(true);
        invocation.ConflictingPath.Returns(path);
        invocation.RecommendedName.Returns("func5");
        return invocation;
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
