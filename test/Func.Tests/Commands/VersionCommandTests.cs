// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands;

public class VersionCommandTests
{
    private readonly TestInteractionService _interaction;
    private readonly ICliVersionProvider _versionProvider;

    public VersionCommandTests()
    {
        _interaction = new TestInteractionService();
        _versionProvider = Substitute.For<ICliVersionProvider>();
        _versionProvider.Version.Returns("5.0.0");
        _versionProvider.InformationalVersion.Returns("5.0.0+abc1234");
    }

    [Fact]
    public async Task ExecuteAsync_PrintsVersionString()
    {
        var command = new VersionCommand(_interaction, _versionProvider);
        var parseResult = command.Parse([]);

        var exitCode = await parseResult.InvokeAsync();

        Assert.Equal(0, exitCode);
        Assert.Single(_interaction.Lines);
        Assert.Contains("5.0.0", _interaction.Lines[0]);
    }

    [Fact]
    public void AssemblyCliVersionProvider_ReturnsNonEmptyVersion()
    {
        var provider = new AssemblyCliVersionProvider();

        Assert.False(string.IsNullOrWhiteSpace(provider.Version));
        Assert.Contains("5.0.0", provider.Version);
    }

    [Fact]
    public void ExecuteDetailed_PrintsTable()
    {
        var command = new VersionCommand(_interaction, _versionProvider);

        var exitCode = command.ExecuteDetailed();

        Assert.Equal(0, exitCode);
        Assert.Contains(_interaction.Lines, l => l.Contains("Azure Functions CLI"));
        Assert.Contains("Version", _interaction.AllOutput);
        Assert.Contains("Runtime", _interaction.AllOutput);
    }
}
