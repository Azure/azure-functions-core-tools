// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Configuration;

internal sealed class StackOptionsSetup(
    IConfiguration configuration,
    ICliConfigurationProvider? configurationProvider = null) : IConfigureNamedOptions<StackOptions>
{
    private readonly IConfiguration _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    private readonly ICliConfigurationProvider? _configurationProvider = configurationProvider;

    public void Configure(StackOptions options)
        => Configure(Options.DefaultName, options);

    public void Configure(string? name, StackOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        GetConfiguration(name).GetSection(StackOptions.SectionName).Bind(options);

        options.Runtime = Normalize(options.Runtime);
        options.Language = Normalize(options.Language);
    }

    private IConfiguration GetConfiguration(string? name)
        => string.IsNullOrEmpty(name) || _configurationProvider is null
            ? _configuration
            : _configurationProvider.GetEffectiveConfiguration(new DirectoryInfo(name));

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
