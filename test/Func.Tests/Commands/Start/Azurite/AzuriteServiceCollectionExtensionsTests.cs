// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Azurite;
using Azure.Functions.Cli.Commands.Start.Azurite.Launching;
using Azure.Functions.Cli.Commands.Start.Azurite.Orchestration;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Commands.Start.Azurite;

public class AzuriteServiceCollectionExtensionsTests
{
    [Fact]
    public void AddAzuriteProbe_ResolvesIAzuriteProbe()
    {
        IServiceProvider provider = BaseServices().AddAzuriteProbe().BuildServiceProvider();
        provider.GetRequiredService<IAzuriteProbe>().Should().NotBeNull();
    }

    [Fact]
    public void AddAzuriteManagedPaths_ResolvesPathsProviderAndClock()
    {
        IServiceProvider provider = BaseServices().AddAzuriteManagedPaths().BuildServiceProvider();
        provider.GetRequiredService<IAzuriteManagedPathsProvider>().Should().NotBeNull();
        provider.GetRequiredService<IClock>().Should().NotBeNull();
    }

    [Fact]
    public void AddAzuriteDiscovery_ResolvesLocatorAndDockerProbe()
    {
        IServiceProvider provider = BaseServices().AddAzuriteDiscovery().BuildServiceProvider();
        provider.GetRequiredService<IAzuriteExecutableLocator>().Should().NotBeNull();
        provider.GetRequiredService<IDockerAvailabilityProbe>().Should().NotBeNull();
    }

    [Fact]
    public void AddAzuriteLauncher_ResolvesLauncher()
    {
        IServiceCollection services = BaseServices().AddAzuriteDiscovery().AddAzuriteLauncher();
        IServiceProvider provider = services.BuildServiceProvider();
        provider.GetRequiredService<IAzuriteLauncher>().Should().NotBeNull();
    }

    [Fact]
    public void AddManagedAzurite_ResolvesOrchestratorAndAllDependencies()
    {
        IServiceProvider provider = BaseServices().AddManagedAzurite().BuildServiceProvider();

        provider.GetRequiredService<IManagedAzuriteOrchestrator>().Should().NotBeNull();
        provider.GetRequiredService<IAzureWebJobsStorageClassifier>().Should().NotBeNull();
        provider.GetRequiredService<IAzuriteProbe>().Should().NotBeNull();
        provider.GetRequiredService<IAzuriteExecutableLocator>().Should().NotBeNull();
        provider.GetRequiredService<IDockerAvailabilityProbe>().Should().NotBeNull();
        provider.GetRequiredService<IAzuriteLauncher>().Should().NotBeNull();
        provider.GetRequiredService<IAzuriteManagedPathsProvider>().Should().NotBeNull();
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
