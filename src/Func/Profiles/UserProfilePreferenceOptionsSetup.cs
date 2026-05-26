// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Profiles;

/// <summary>
/// Binds profile preferences from user-level CLI configuration.
/// </summary>
internal sealed class UserProfilePreferenceOptionsSetup(IConfiguration configuration, ICliConfigurationProvider? configurationProvider = null)
    : IConfigureOptions<UserProfilePreferenceOptions>
{
    private readonly IConfiguration _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    private readonly ICliConfigurationProvider? _configurationProvider = configurationProvider;

    public void Configure(UserProfilePreferenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        GetConfiguration().Bind(options);
        options.DefaultProfile = string.IsNullOrWhiteSpace(options.DefaultProfile) ? null : options.DefaultProfile.Trim();
    }

    private IConfiguration GetConfiguration()
        => _configurationProvider?.GetUserConfiguration() ?? _configuration;
}
