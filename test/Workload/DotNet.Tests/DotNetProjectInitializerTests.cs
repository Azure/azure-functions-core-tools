// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads;
using Xunit;

namespace Azure.Functions.Cli.Workload.DotNet.Tests;

public class DotNetProjectInitializerTests
{
    [Fact]
    public async Task InitializeAsync_Throws_NotImplemented()
    {
        var initializer = new DotNetProjectInitializer();
        var context = new InitContext(
            WorkingDirectory.FromExplicit(Path.GetTempPath()),
            ProjectName: "test",
            Language: null,
            Force: false);

        await Assert.ThrowsAsync<NotImplementedException>(
            () => initializer.InitializeAsync(context, new RootCommand().Parse(string.Empty)));
    }

    [Fact]
    public void Stack_IsDotNet()
    {
        Assert.Equal("dotnet", new DotNetProjectInitializer().Stack);
    }

    [Fact]
    public void GetInitOptions_IsEmpty()
    {
        Assert.Empty(new DotNetProjectInitializer().GetInitOptions());
    }
}
