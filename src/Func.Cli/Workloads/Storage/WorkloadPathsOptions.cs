// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel.DataAnnotations;

namespace Azure.Functions.Cli.Workloads.Storage;

/// <summary>
/// Pure configuration data for workload storage. Bound from the
/// <c>Workloads</c> configuration section at startup; the corresponding
/// environment variable is <c>FUNC_CLI_Workloads__Home</c>.
/// </summary>
/// <remarks>
/// This type intentionally carries no behavior — path composition lives on
/// <see cref="IWorkloadPaths"/>. Tests inject this via <c>Options.Create(...)</c>
/// without touching process-global state.
/// </remarks>
internal sealed class WorkloadPathsOptions
{
    /// <summary>
    /// Root directory the func CLI persists workloads under. Defaults to
    /// <c>~/.azure-functions</c>.
    /// </summary>
    [Required]
    [MinLength(1)]
    public string Home { get; set; } = DefaultHome();

    private static string DefaultHome()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".azure-functions");
}
