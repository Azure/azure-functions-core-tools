// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Update;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Update;

public sealed class UpdateRegistrationTests
{
    [Fact]
    public void AddCliUpdate_DefaultConfiguration_UsesPublicCdn()
    {
        IProcessEnvironment environment = Substitute.For<IProcessEnvironment>();
        using ServiceProvider provider = CreateProvider(environment);

        UpdateCdnOptions options = provider.GetRequiredService<IOptions<UpdateCdnOptions>>().Value;
        HttpClient client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(UpdateRegistration.HttpClientName);

        options.BaseUrl.Should().Be("https://cdn.functions.azure.com/");
        client.BaseAddress.Should().Be(new Uri("https://cdn.functions.azure.com/"));
    }

    [Fact]
    public void AddCliUpdate_EnvironmentOverride_UsesConfiguredCdn()
    {
        IProcessEnvironment environment = Substitute.For<IProcessEnvironment>();
        environment.Get(UpdateRegistration.CdnBaseUrlEnvironmentVariable).Returns("https://localhost:7443/");
        using ServiceProvider provider = CreateProvider(environment);

        HttpClient client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(UpdateRegistration.HttpClientName);

        client.BaseAddress.Should().Be(new Uri("https://localhost:7443/"));
    }

    [Fact]
    public void AddCliUpdate_PathPrefixedOverride_PreservesPrefix()
    {
        IProcessEnvironment environment = Substitute.For<IProcessEnvironment>();
        environment.Get(UpdateRegistration.CdnBaseUrlEnvironmentVariable).Returns("https://localhost:7443/mirror");
        using ServiceProvider provider = CreateProvider(environment);

        UpdateCdnOptions options = provider.GetRequiredService<IOptions<UpdateCdnOptions>>().Value;
        HttpClient client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(UpdateRegistration.HttpClientName);

        options.BaseUrl.Should().Be("https://localhost:7443/mirror/");
        client.BaseAddress.Should().Be(new Uri("https://localhost:7443/mirror/"));
        new Uri(client.BaseAddress, CdnReleaseFeed.ManifestPath)
            .Should().Be(new Uri("https://localhost:7443/mirror/public/cli/v5/version.json"));
    }

    [Fact]
    public void AddCliUpdate_InvalidOverride_DefersFailureUntilClientCreation()
    {
        IProcessEnvironment environment = Substitute.For<IProcessEnvironment>();
        environment.Get(UpdateRegistration.CdnBaseUrlEnvironmentVariable).Returns("not-a-url");
        using ServiceProvider provider = CreateProvider(environment);

        provider.GetRequiredService<IReleaseFeed>().Should().BeOfType<CdnReleaseFeed>();
        provider.GetRequiredService<ICliUpdater>().Should().BeOfType<CliUpdater>();
        environment.DidNotReceive().Get(UpdateRegistration.CdnBaseUrlEnvironmentVariable);

        FluentActions.Invoking(() =>
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(UpdateRegistration.HttpClientName))
            .Should().Throw<InvalidOperationException>()
            .WithMessage($"*{UpdateRegistration.CdnBaseUrlEnvironmentVariable}*absolute HTTP(S) URL*");
    }

    [Fact]
    public void AddCliUpdate_RegistersReleaseFeedAndUpdater()
    {
        IProcessEnvironment environment = Substitute.For<IProcessEnvironment>();
        using ServiceProvider provider = CreateProvider(environment);

        provider.GetRequiredService<IReleaseFeed>().Should().BeOfType<CdnReleaseFeed>();
        provider.GetRequiredService<ICliUpdater>().Should().BeOfType<CliUpdater>();
    }

    private static ServiceProvider CreateProvider(IProcessEnvironment environment)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(environment);
        services.AddOptions<CliEnvironmentOptions>();
        services.AddCliUpdate();
        return services.BuildServiceProvider();
    }
}
