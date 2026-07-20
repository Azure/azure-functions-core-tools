// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Azurite;
using Azure.Functions.Cli.Configuration;

namespace Azure.Functions.Cli.Tests.Commands.Start.Azurite;

public class AzuriteManagedPathsProviderTests
{
    [Fact]
    public void GetPaths_ComposesUnderFuncHomeAzurite()
    {
        using var temp = new TempDirectory();
        var configurationPaths = new CliConfigurationPathsOptions(temp.Path);

        var provider = new AzuriteManagedPathsProvider(configurationPaths);
        AzuriteManagedPaths paths = provider.GetPaths();

        string expectedRoot = Path.Combine(configurationPaths.Home, "azurite");
        paths.DataDirectory.Should().Be(Path.Combine(expectedRoot, "data"));
        paths.LogFilePath.Should().Be(Path.Combine(expectedRoot, "azurite.log"));
    }

    [Fact]
    public void GetPaths_DoesNotCreateDirectories()
    {
        using var temp = new TempDirectory();
        var configurationPaths = new CliConfigurationPathsOptions(temp.Path);

        var provider = new AzuriteManagedPathsProvider(configurationPaths);
        AzuriteManagedPaths paths = provider.GetPaths();

        Directory.Exists(paths.DataDirectory).Should().BeFalse();
        Directory.Exists(Path.GetDirectoryName(paths.LogFilePath)!).Should().BeFalse();
    }

    [Fact]
    public async Task EnsureCreatedAsync_CreatesDataAndLogDirectories()
    {
        using var temp = new TempDirectory();
        var configurationPaths = new CliConfigurationPathsOptions(temp.Path);

        var provider = new AzuriteManagedPathsProvider(configurationPaths);
        AzuriteManagedPaths paths = provider.GetPaths();

        await provider.EnsureCreatedAsync(paths, CancellationToken.None);

        Directory.Exists(paths.DataDirectory).Should().BeTrue();
        Directory.Exists(Path.GetDirectoryName(paths.LogFilePath)!).Should().BeTrue();
    }

    [Fact]
    public void Constructor_NullConfigurationPaths_Throws()
    {
        FluentActions.Invoking(() => new AzuriteManagedPathsProvider(null!)).Should().ThrowExactly<ArgumentNullException>();
    }
}
