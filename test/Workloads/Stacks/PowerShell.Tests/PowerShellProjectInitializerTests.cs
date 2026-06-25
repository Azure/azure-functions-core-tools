// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Xunit;

namespace Azure.Functions.Cli.Workloads.PowerShell.Tests;

public class PowerShellProjectInitializerTests
{
    [Fact]
    public void Stack_IsPowerShell()
    {
        Assert.Equal("powershell", new PowerShellProjectInitializer().Stack);
    }

    [Fact]
    public void SupportedLanguages_ContainsPowerShell()
    {
        Assert.Equal("PowerShell", Assert.Single(new PowerShellProjectInitializer().SupportedLanguages));
    }

    [Fact]
    public void GetInitOptions_IsEmpty()
    {
        RootCommand root = [];
        IReadOnlyList<Option> options = new PowerShellProjectInitializer().GetInitOptions(new InitOptionRegistry(root));

        Assert.Empty(options);
    }

    [Fact]
    public void GetInitOptions_NullRegistry_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new PowerShellProjectInitializer().GetInitOptions(null!));
    }

    [Fact]
    public async Task InitializeAsync_Throws_NotImplemented()
    {
        PowerShellProjectInitializer initializer = new();
        InitContext context = new(
            WorkingDirectory.FromExplicit(Path.GetTempPath()),
            ProjectName: "my-ps-app",
            Language: null,
            Force: false);
        RootCommand root = [];

        await Assert.ThrowsAsync<NotImplementedException>(
            () => initializer.InitializeAsync(context, root.Parse([])));
    }
}
