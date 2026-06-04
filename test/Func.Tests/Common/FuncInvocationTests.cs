// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Common;

public class FuncInvocationTests
{
    [Fact]
    public void NoResolvedFunc_RecommendsFunc()
    {
        IFuncOnPathResolver resolver = Substitute.For<IFuncOnPathResolver>();
        resolver.ResolveFuncOnPath().Returns((string?)null);

        var invocation = new FuncInvocation(resolver, processPath: "/install/dir/func");

        Assert.Equal("func", invocation.RecommendedName);
        Assert.False(invocation.ConflictDetected);
        Assert.Null(invocation.ConflictingPath);
    }

    [Fact]
    public void ResolvedFuncMatchesProcessPath_NoConflict()
    {
        string ourPath = Path.Combine(Path.GetTempPath(), "func-self-" + Guid.NewGuid().ToString("N"));
        IFuncOnPathResolver resolver = Substitute.For<IFuncOnPathResolver>();
        resolver.ResolveFuncOnPath().Returns(ourPath);

        var invocation = new FuncInvocation(resolver, processPath: ourPath);

        Assert.Equal("func", invocation.RecommendedName);
        Assert.False(invocation.ConflictDetected);
    }

    [Fact]
    public void ResolvedFuncDiffersFromProcessPath_RecommendsFunc5()
    {
        string ourPath = Path.Combine(Path.GetTempPath(), "v5-install", "func");
        string foreignPath = Path.Combine(Path.GetTempPath(), "v4-install", "func");
        IFuncOnPathResolver resolver = Substitute.For<IFuncOnPathResolver>();
        resolver.ResolveFuncOnPath().Returns(foreignPath);

        var invocation = new FuncInvocation(resolver, processPath: ourPath);

        Assert.Equal("func5", invocation.RecommendedName);
        Assert.True(invocation.ConflictDetected);
        Assert.Equal(Path.GetFullPath(foreignPath), invocation.ConflictingPath);
    }

    [Fact]
    public void NullProcessPath_NoConflict()
    {
        IFuncOnPathResolver resolver = Substitute.For<IFuncOnPathResolver>();
        resolver.ResolveFuncOnPath().Returns("/somewhere/func");

        var invocation = new FuncInvocation(resolver, processPath: null);

        Assert.False(invocation.ConflictDetected);
        Assert.Equal("func", invocation.RecommendedName);
    }

    [Fact]
    public void ResolverThrows_FailsClosedToDefaults()
    {
        IFuncOnPathResolver resolver = Substitute.For<IFuncOnPathResolver>();
        resolver.ResolveFuncOnPath().Returns(_ => throw new IOException("boom"));

        var invocation = new FuncInvocation(resolver, processPath: "/install/func");

        Assert.False(invocation.ConflictDetected);
        Assert.Equal("func", invocation.RecommendedName);
    }

    [Fact]
    public void DetectionIsMemoised_ResolverCalledOnce()
    {
        IFuncOnPathResolver resolver = Substitute.For<IFuncOnPathResolver>();
        resolver.ResolveFuncOnPath().Returns("/v4/func");

        var invocation = new FuncInvocation(resolver, processPath: "/v5/func");

        _ = invocation.RecommendedName;
        _ = invocation.ConflictDetected;
        _ = invocation.ConflictingPath;

        resolver.Received(1).ResolveFuncOnPath();
    }

    [Fact]
    public void NullPathResolver_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new FuncInvocation(null!));
    }
}
