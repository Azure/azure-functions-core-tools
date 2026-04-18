// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Xunit;

namespace Azure.Functions.Cli.Workload.Dotnet.Tests;

public class DotnetCliResultTests
{
    [Fact]
    public void IsSuccess_TrueForExitCodeZero()
    {
        var result = new DotnetCliResult(0, "output", "");
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void IsSuccess_FalseForNonZeroExitCode()
    {
        var result = new DotnetCliResult(1, "", "error");
        Assert.False(result.IsSuccess);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(127)]
    public void IsSuccess_FalseForVariousNonZeroExitCodes(int exitCode)
    {
        var result = new DotnetCliResult(exitCode, "", "");
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Properties_AreAccessible()
    {
        var result = new DotnetCliResult(0, "stdout content", "stderr content");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("stdout content", result.StandardOutput);
        Assert.Equal("stderr content", result.StandardError);
    }
}
