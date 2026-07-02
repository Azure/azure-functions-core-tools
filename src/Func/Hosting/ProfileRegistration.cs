// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Profiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Registers profile resolution services.
/// </summary>
internal static class ProfileRegistration
{
    internal static readonly Uri ProdCdnBaseUri = new("https://cdn.functions.azure.com/public/");
    internal static readonly Uri StagingCdnBaseUri = new("https://cdn-staging.functions.azure.com/public/");

    public static IServiceCollection AddProfiles(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<CliConfigurationPathsOptions>();
        services.AddSingleton<ProfileDocumentParser>();
        services.AddSingleton<IProfileFileSystem, ProfileFileSystem>();
        services.AddSingleton<IProfileSource, ProjectProfileSource>();
        services.AddSingleton<IProfileSource, UserProfileSource>();
        services.AddSingleton<IProfileSource, RemoteProfileSource>();
        services.AddSingleton<IProfileSource, BuiltInProfileSource>();
        services.AddHttpClient(RemoteProfileSource.HttpClientName, client =>
        {
            client.BaseAddress = ProdCdnBaseUri;
        });
        services.AddSingleton<IConfigureOptions<ProjectProfileOptions>, ProjectProfileOptionsSetup>();
        services.AddSingleton<IConfigureOptions<UserProfilePreferenceOptions>, UserProfilePreferenceOptionsSetup>();
        services.AddSingleton<IProjectProfileConfigStore, ProjectProfileConfigStore>();
        services.AddSingleton<IProfileCatalog, ProfileCatalog>();
        services.AddSingleton<IProfileResolver, ProfileResolver>();

        return services;
    }
}
