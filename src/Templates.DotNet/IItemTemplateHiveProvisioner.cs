// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.DotNet;

/// <summary>
/// On-demand <c>dotnet new install &lt;pkg&gt;::&lt;ver&gt; --debug:custom-hive
/// &lt;path&gt;</c> orchestrator for the templates content workload. Mirrors
/// the pattern <see cref="Azure.Functions.Cli.Workloads.DotNet"/>'s
/// project initializer uses: install the upstream NuGet item-templates
/// package into a CLI-managed custom hive, then drive <c>dotnet new</c>
/// against that hive — so we never pollute the user's machine-global
/// templating store, and the install survives across <c>func new</c> calls.
/// </summary>
/// <remarks>
/// Why a per-(pkg-id + version) sentinel: a single hive can hold templates
/// from multiple item-templates packages (different versions, future
/// channel splits). Each <c>source.json</c> pin gets its own sentinel
/// file inside the hive so re-installing one version doesn't trigger a
/// re-install of every other.
/// </remarks>
internal interface IItemTemplateHiveProvisioner
{
    /// <summary>
    /// Verifies the item-templates package described by
    /// <paramref name="installDirectory"/>'s <c>source.json</c> is present
    /// in the CLI's custom hive; installs it via
    /// <c>dotnet new install</c> if not. Idempotent.
    /// </summary>
    /// <returns>
    /// The custom-hive directory to pass to <c>dotnet new</c> via
    /// <c>--debug:custom-hive</c>.
    /// </returns>
    public Task<string> EnsureProvisionedAsync(string installDirectory, CancellationToken cancellationToken);
}
