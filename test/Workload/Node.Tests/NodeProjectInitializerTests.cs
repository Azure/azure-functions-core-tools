// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Xunit;

namespace Azure.Functions.Cli.Workload.Node.Tests;

public class NodeProjectInitializerTests
{
    [Fact]
    public async Task InitializeAsync_Throws_NotImplemented()
    {
        NodeProjectInitializer initializer = new();
        InitContext context = new(
            WorkingDirectory.FromExplicit(Path.GetTempPath()),
            ProjectName: "test",
            Language: null,
            Force: false);

        await Assert.ThrowsAsync<NotImplementedException>(
            () => initializer.InitializeAsync(context, new RootCommand().Parse(string.Empty)));
    }

    [Fact]
    public void Stack_IsNode()
    {
        Assert.Equal("node", new NodeProjectInitializer().Stack);
    }

    [Fact]
    public void GetInitOptions_IsEmpty()
    {
        Assert.Empty(new NodeProjectInitializer().GetInitOptions());
    }
}
