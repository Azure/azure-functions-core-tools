// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli;
using Azure.Functions.Cli.Commands.Start.Azurite;
using Azure.Functions.Cli.Commands.Start.Azurite.Launching;
using Azure.Functions.Cli.Commands.Start.Azurite.Orchestration;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands.Start.Azurite;

public class AzuriteServiceCollectionExtensionsTests
{
    [Fact]
    public void AddAzuriteProbe_ResolvesIAzuriteProbe()
    {
        IServiceProvider provider = BaseServices().AddAzuriteProbe().BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<IAzuriteProbe>());
    }

    [Fact]
    public void AddAzuriteManagedPaths_ResolvesPathsProviderAndClock()
    {
        IServiceProvider provider = BaseServices().AddAzuriteManagedPaths().BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<IAzuriteManagedPathsProvider>());
        Assert.NotNull(provider.GetRequiredService<IClock>());
    }

    [Fact]
    public void AddAzuriteDiscovery_ResolvesLocatorAndDockerProbe()
    {
        IServiceProvider provider = BaseServices().AddAzuriteDiscovery().BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<IAzuriteExecutableLocator>());
        Assert.NotNull(provider.GetRequiredService<IDockerAvailabilityProbe>());
    }

    [Fact]
    public void AddAzuriteLauncher_ResolvesLauncher()
    {
        IServiceCollection services = BaseServices().AddAzuriteDiscovery().AddAzuriteLauncher();
        IServiceProvider provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<IAzuriteLauncher>());
    }

    [Fact]
    public void AddManagedAzurite_ResolvesOrchestratorAndAllDependencies()
    {
        IServiceProvider provider = BaseServices().AddManagedAzurite().BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IManagedAzuriteOrchestrator>());
        Assert.NotNull(provider.GetRequiredService<IAzureWebJobsStorageClassifier>());
        Assert.NotNull(provider.GetRequiredService<IAzuriteProbe>());
        Assert.NotNull(provider.GetRequiredService<IAzuriteExecutableLocator>());
        Assert.NotNull(provider.GetRequiredService<IDockerAvailabilityProbe>());
        Assert.NotNull(provider.GetRequiredService<IAzuriteLauncher>());
        Assert.NotNull(provider.GetRequiredService<IAzuriteManagedPathsProvider>());
    }

    private static IServiceCollection BaseServices()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(new CliConfigurationPathsOptions(Path.GetTempPath()));
        services.AddSingleton(Substitute.For<IInteractionService>());
        services.AddSingleton(Substitute.For<IPlatform>());
        return services;
    }
}
