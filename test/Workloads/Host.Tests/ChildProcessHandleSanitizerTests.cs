// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Host.Interop;
using Azure.Functions.Cli.Workloads.Host.Startup;
using NSubstitute;

namespace Azure.Functions.Cli.Workloads.Host.Tests;

public sealed class ChildProcessHandleSanitizerTests
{
    private const uint HandleFlagInherit = 0x1;
    private const uint HandleFlagProtectFromClose = 0x2;

    [Fact]
    public void Constructor_NullNativeHandleApi_Throws()
        => FluentActions.Invoking(() => new ChildProcessHandleSanitizer(null!)).Should().ThrowExactly<ArgumentNullException>();

    [Fact]
    public void DisableInheritanceOnOpenHandles_ClearsInheritOnlyOnInheritableHandles()
    {
        INativeHandleApi nativeHandleApi = Substitute.For<INativeHandleApi>();
        nativeHandleApi.TryDisableInheritance(Arg.Any<nint>()).Returns(true);

        ConfigureHandle(nativeHandleApi, 0x10, HandleFlagInherit);
        ConfigureHandle(nativeHandleApi, 0x20, HandleFlagInherit);
        ConfigureHandle(nativeHandleApi, 0x30, HandleFlagProtectFromClose);

        var sanitizer = new ChildProcessHandleSanitizer(nativeHandleApi);

        int disabledCount = sanitizer.DisableInheritanceOnOpenHandles();

        disabledCount.Should().Be(2);
        nativeHandleApi.Received(1).TryDisableInheritance((nint)0x10);
        nativeHandleApi.Received(1).TryDisableInheritance((nint)0x20);
        nativeHandleApi.DidNotReceive().TryDisableInheritance((nint)0x30);
    }

    [Fact]
    public void DisableInheritanceOnOpenHandles_HandleThatFailsToClear_IsAttemptedButNotCounted()
    {
        INativeHandleApi nativeHandleApi = Substitute.For<INativeHandleApi>();
        nativeHandleApi.TryDisableInheritance(Arg.Any<nint>()).Returns(true);
        nativeHandleApi.TryDisableInheritance((nint)0x20).Returns(false);

        ConfigureHandle(nativeHandleApi, 0x10, HandleFlagInherit);
        ConfigureHandle(nativeHandleApi, 0x20, HandleFlagInherit);

        var sanitizer = new ChildProcessHandleSanitizer(nativeHandleApi);

        int disabledCount = sanitizer.DisableInheritanceOnOpenHandles();

        disabledCount.Should().Be(1);
        nativeHandleApi.Received(1).TryDisableInheritance((nint)0x20);
    }

    [Fact]
    public void DisableInheritanceOnOpenHandles_NoInheritableHandles_ClearsNothing()
    {
        INativeHandleApi nativeHandleApi = Substitute.For<INativeHandleApi>();

        var sanitizer = new ChildProcessHandleSanitizer(nativeHandleApi);

        int disabledCount = sanitizer.DisableInheritanceOnOpenHandles();

        disabledCount.Should().Be(0);
        nativeHandleApi.DidNotReceive().TryDisableInheritance(Arg.Any<nint>());
    }

    private static void ConfigureHandle(INativeHandleApi nativeHandleApi, int handleValue, uint flags)
        => nativeHandleApi.TryGetHandleFlags((nint)handleValue, out Arg.Any<uint>())
            .Returns(call =>
            {
                call[1] = flags;
                return true;
            });
}
