// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Configuration;

internal sealed class HostStartupOptionsSetup(
    IConfiguration configuration,
    CliConfigurationSourceBuilder? configurationSourceBuilder = null) : IConfigureNamedOptions<HostStartupOptions>
{
    private readonly IConfiguration _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    private readonly CliConfigurationSourceBuilder? _configurationSourceBuilder = configurationSourceBuilder;

    public void Configure(HostStartupOptions options)
        => Configure(Options.DefaultName, options);

    public void Configure(string? name, HostStartupOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        GetConfiguration(name).GetSection(HostStartupOptions.SectionName).Bind(options);
    }

    private IConfiguration GetConfiguration(string? name)
        => string.IsNullOrEmpty(name) || _configurationSourceBuilder is null
            ? _configuration
            : _configurationSourceBuilder.Build(new DirectoryInfo(name));
}
