// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Default on-disk paths for the out-of-process workload installation.
/// Kept distinct from <c>~/.azure-functions/workloads</c> (in-process branch)
/// so both prototypes can coexist on the same machine.
/// </summary>
public static class WorkloadPaths
{
    public static string DefaultRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".azure-functions",
        "workloads-oop");

    public static string ManifestPath(string root) => Path.Combine(root, "installed.json");

    public static string InstallDirectory(string root, string id, string version) =>
        Path.Combine(root, id, version);
}
