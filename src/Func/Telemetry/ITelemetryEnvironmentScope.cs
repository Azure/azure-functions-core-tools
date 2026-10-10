// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Telemetry;

/// <summary>
/// Scopes telemetry-only environment variable overrides and restores them on disposal.
/// </summary>
internal interface ITelemetryEnvironmentScope : IProcessEnvironment, IDisposable
{
    /// <summary>Applies the telemetry-only environment variable overrides.</summary>
    public void Apply();

    /// <summary>Removes the overrides from <paramref name="environment"/> so a spawned process doesn't inherit them.</summary>
    public void RestoreInheritedVariables(IDictionary<string, string?> environment);
}
