// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Workloads.Discovery;

/// <summary>
/// Locates the <see cref="CliWorkloadAttribute{T}"/> declaration in an
/// installed workload's directory and projects it into an
/// <see cref="EntryPointSpec"/> the manifest can persist. Runs at install
/// time so the load path never has to crack open assemblies just to find
/// the entry point.
/// </summary>
internal interface IWorkloadEntryPointScanner
{
    /// <summary>
    /// Scans <paramref name="installDirectory"/> for the single assembly that
    /// declares <c>[assembly: CliWorkload&lt;T&gt;]</c>.
    /// </summary>
    /// <param name="installDirectory">
    /// Absolute path to the workload's install directory (the same path
    /// later persisted as <see cref="GlobalManifestEntry.InstallPath"/>).
    /// </param>
    /// <returns>
    /// An <see cref="EntryPointSpec"/> whose <see cref="EntryPointSpec.Assembly"/>
    /// is the file name of the matching assembly relative to
    /// <paramref name="installDirectory"/> and whose
    /// <see cref="EntryPointSpec.Type"/> is the full name of the
    /// <see cref="IWorkload"/> implementation.
    /// </returns>
    /// <exception cref="Common.GracefulException">
    /// Thrown when no assembly declares the attribute, or when more than one
    /// does (a workload package may export at most one entry point).
    /// </exception>
    public EntryPointSpec Scan(string installDirectory);
}
