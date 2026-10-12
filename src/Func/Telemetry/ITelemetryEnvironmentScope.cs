// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Telemetry;

/// <summary>
/// Scopes telemetry-only environment variable overrides and restores them on disposal.
/// </summary>
/// <remarks>
/// Deliberately not an <see cref="Azure.Functions.Cli.Common.IProcessEnvironment"/>: that
/// interface is a general-purpose env var reader shared by unrelated consumers
/// (workload resolution, Azurite, host init steps), none of which need the
/// telemetry-only overrides this type tracks. Keeping the two separate avoids
/// mixing a general reader with telemetry-specific lifecycle, and avoids the
/// built-in DI container tracking this scope for disposal as a side effect of
/// another registration resolving it (see <see cref="Hosting.CliHostFactory"/>).
/// </remarks>
internal interface ITelemetryEnvironmentScope : IDisposable
{
    /// <summary>Applies the telemetry-only environment variable overrides.</summary>
    public void Apply();

    /// <summary>Removes the overrides from <paramref name="environment"/> so a spawned process doesn't inherit them.</summary>
    public void RestoreInheritedVariables(IDictionary<string, string?> environment);
}
