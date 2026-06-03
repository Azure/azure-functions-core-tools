// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Workloads.Catalog;

/// <summary>
/// Configures workload catalog options from supported process environment variables.
/// </summary>
/// <remarks>
/// Resolution order for <see cref="WorkloadCatalogOptions.IncludePrerelease"/>:
/// 1. <c>FUNC_CLI_WORKLOADS_PRERELEASE</c> when set to a recognised true/false token.
/// 2. Auto-detected from the running CLI's informational version: a build whose
///    version contains a <c>-</c> (e.g. <c>5.0.0-preview.1</c>) opts in so customers
///    on prerelease CLI builds can resolve matching prerelease workload packages
///    without setting any environment variable.
/// </remarks>
internal sealed class WorkloadCatalogOptionsSetup(
    IProcessEnvironment processEnvironment,
    ICliVersionProvider cliVersionProvider) : IConfigureOptions<WorkloadCatalogOptions>
{
    private static readonly HashSet<string> _trueEnvironmentValues =
        new(StringComparer.OrdinalIgnoreCase) { "1", "true", "t", "y", "yes", "on" };

    private static readonly HashSet<string> _falseEnvironmentValues =
        new(StringComparer.OrdinalIgnoreCase) { "0", "false", "f", "n", "no", "off" };

    private readonly IProcessEnvironment _processEnvironment = processEnvironment
        ?? throw new ArgumentNullException(nameof(processEnvironment));

    private readonly ICliVersionProvider _cliVersionProvider = cliVersionProvider
        ?? throw new ArgumentNullException(nameof(cliVersionProvider));

    public void Configure(WorkloadCatalogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.Source = ResolveSource();
        options.IncludePrerelease = ResolveIncludePrerelease();
    }

    private string? ResolveSource()
    {
        string? configured = _processEnvironment.Get(Constants.WorkloadsSourceEnvironmentVariable);

        return string.IsNullOrWhiteSpace(configured) ? null : configured;
    }

    private bool ResolveIncludePrerelease()
    {
        string? configured = _processEnvironment.Get(Constants.WorkloadsPrereleaseEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            string trimmed = configured.Trim();
            if (_trueEnvironmentValues.Contains(trimmed))
            {
                return true;
            }

            if (_falseEnvironmentValues.Contains(trimmed))
            {
                return false;
            }
        }

        return _cliVersionProvider.IsPrerelease;
    }
}
