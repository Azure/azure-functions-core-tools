// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Xunit;

namespace Azure.Functions.Cli.Workloads.Python.Tests;

public class PythonProjectInitializerTests
{
    [Fact]
    public async Task InitializeAsync_Throws_NotImplemented()
    {
        var initializer = new PythonProjectInitializer();
        var context = new InitContext(
            WorkingDirectory.FromExplicit(Path.GetTempPath()),
            ProjectName: "test",
            Language: null,
            Force: false);

        await Assert.ThrowsAsync<NotImplementedException>(
            () => initializer.InitializeAsync(context, new RootCommand().Parse(string.Empty)));
    }

    [Fact]
    public void Stack_IsPython()
    {
        Assert.Equal("python", new PythonProjectInitializer().Stack);
    }

    [Fact]
    public void GetInitOptions_IsEmpty()
    {
        Assert.Empty(new PythonProjectInitializer().GetInitOptions());
    }
}
