// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Workloads.Catalog;

/// <summary>
/// Configures workload catalog options from supported process environment variables.
/// </summary>
internal sealed class WorkloadCatalogOptionsSetup(IProcessEnvironment processEnvironment) : IConfigureOptions<WorkloadCatalogOptions>
{
    private static readonly HashSet<string> _falseEnvironmentValues =
        new(StringComparer.OrdinalIgnoreCase) { "0", "false", "n", "no", "off" };

    private readonly IProcessEnvironment _processEnvironment = processEnvironment ?? throw new ArgumentNullException(nameof(processEnvironment));

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

        return !string.IsNullOrWhiteSpace(configured) && !_falseEnvironmentValues.Contains(configured.Trim());
    }
}
