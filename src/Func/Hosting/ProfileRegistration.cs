// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
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
    public static IServiceCollection AddProfiles(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<CliConfigurationPathsOptions>();
        services.AddSingleton<ProfileDocumentParser>();
        services.AddSingleton<IProfileFileSystem, ProfileFileSystem>();
        services.AddSingleton<IProfileSource, ProjectProfileSource>();
        services.AddSingleton<IProfileSource, UserProfileSource>();

        services.AddOptions<RemoteProfileOptions>()
            .Configure(opts =>
            {
                string? overrideUrl = Environment.GetEnvironmentVariable(Constants.ProfilesCdnBaseUrlEnvironmentVariable);
                if (!string.IsNullOrWhiteSpace(overrideUrl))
                {
                    opts.CdnBaseUrl = new Uri(overrideUrl);
                }
            });

        services.AddHttpClient<RemoteProfileSource>((sp, client) =>
        {
            RemoteProfileOptions opts = sp.GetRequiredService<IOptions<RemoteProfileOptions>>().Value;
            client.BaseAddress = opts.CdnBaseUrl;
            client.Timeout = opts.HttpTimeout;
        });
        services.AddSingleton<IProfileSource>(sp => sp.GetRequiredService<RemoteProfileSource>());

        services.AddSingleton<IProfileSource, BuiltInProfileSource>();

        services.AddSingleton<IConfigureOptions<ProjectProfileOptions>, ProjectProfileOptionsSetup>();
        services.AddSingleton<IConfigureOptions<UserProfilePreferenceOptions>, UserProfilePreferenceOptionsSetup>();
        services.AddSingleton<IProjectProfileConfigStore, ProjectProfileConfigStore>();
        services.AddSingleton<IProfileCatalog, ProfileCatalog>();
        services.AddSingleton<IProfileResolver, ProfileResolver>();

        return services;
    }
}
