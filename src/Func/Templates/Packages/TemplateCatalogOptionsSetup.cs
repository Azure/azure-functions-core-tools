// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Templates.Packages;

/// <summary>
/// Reads template catalog settings without inheriting workload settings.
/// </summary>
internal sealed class TemplateCatalogOptionsSetup(IProcessEnvironment environment, ICliVersionProvider version)
    : IConfigureOptions<TemplateCatalogOptions>
{
    internal const string SourceEnvironmentVariable = "FUNC_CLI_TEMPLATES_SOURCE";

    private readonly IProcessEnvironment _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    private readonly ICliVersionProvider _version = version ?? throw new ArgumentNullException(nameof(version));

    public void Configure(TemplateCatalogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Source = _environment.Get(SourceEnvironmentVariable);
        options.IncludePrerelease = _version.IsPrerelease;
    }
}
