// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Azurite;
using Azure.Functions.Cli.Configuration;
using Xunit;

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
        Assert.Equal(Path.Combine(expectedRoot, "data"), paths.DataDirectory);
        Assert.Equal(Path.Combine(expectedRoot, "azurite.log"), paths.LogFilePath);
    }

    [Fact]
    public void GetPaths_DoesNotCreateDirectories()
    {
        using var temp = new TempDirectory();
        var configurationPaths = new CliConfigurationPathsOptions(temp.Path);

        var provider = new AzuriteManagedPathsProvider(configurationPaths);
        AzuriteManagedPaths paths = provider.GetPaths();

        Assert.False(Directory.Exists(paths.DataDirectory));
        Assert.False(Directory.Exists(Path.GetDirectoryName(paths.LogFilePath)!));
    }

    [Fact]
    public async Task EnsureCreatedAsync_CreatesDataAndLogDirectories()
    {
        using var temp = new TempDirectory();
        var configurationPaths = new CliConfigurationPathsOptions(temp.Path);

        var provider = new AzuriteManagedPathsProvider(configurationPaths);
        AzuriteManagedPaths paths = provider.GetPaths();

        await provider.EnsureCreatedAsync(paths, CancellationToken.None);

        Assert.True(Directory.Exists(paths.DataDirectory));
        Assert.True(Directory.Exists(Path.GetDirectoryName(paths.LogFilePath)!));
    }

    [Fact]
    public void Constructor_NullConfigurationPaths_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new AzuriteManagedPathsProvider(null!));
    }
}
