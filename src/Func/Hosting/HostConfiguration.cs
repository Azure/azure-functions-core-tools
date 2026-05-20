// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Marker for the CLI's host-level <see cref="IConfiguration"/>: the subset
/// of process inputs that bootstrap the host (workload home, future bundle
/// home, etc.). Kept distinct from the broader app
/// <see cref="IConfiguration"/> exposed by
/// <see cref="Microsoft.Extensions.Hosting.HostApplicationBuilder.Configuration"/>
/// so workload assembly loading and other host-bootstrap reads can't be
/// redirected by per-project config files (<c>.func/config.json</c>,
/// <c>local.settings.json</c>) or anything else layered into the app config.
/// </summary>
/// <remarks>
/// Populated by <see cref="HostConfigurationBuilder.Build"/> from a fixed
/// allowlist of process environment variables. Tests substitute their own
/// <see cref="IHostConfiguration"/> instance (typically wrapping an in-memory
/// <see cref="IConfiguration"/>) instead of mutating the real process env.
/// </remarks>
internal interface IHostConfiguration : IConfiguration
{
}

/// <summary>
/// Forwarding wrapper that brands an inner <see cref="IConfiguration"/> as
/// <see cref="IHostConfiguration"/>. The marker interface (and not raw
/// <see cref="IConfiguration"/>) is what host-bootstrap code depends on so
/// the app-config <see cref="IConfiguration"/> can never accidentally satisfy
/// the dependency.
/// </summary>
internal sealed class HostConfiguration(IConfiguration inner) : IHostConfiguration
{
    private readonly IConfiguration _inner =
        inner ?? throw new ArgumentNullException(nameof(inner));

    public string? this[string key]
    {
        get => _inner[key];
        set => _inner[key] = value;
    }

    public IEnumerable<IConfigurationSection> GetChildren() => _inner.GetChildren();

    public IChangeToken GetReloadToken() => _inner.GetReloadToken();

    public IConfigurationSection GetSection(string key) => _inner.GetSection(key);
}
