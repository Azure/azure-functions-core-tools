// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Workloads.ExtensionBundles;

/// <summary>
/// Entry point for the extension bundles workload. Resolves bundle payloads
/// for the func CLI and owns the on-disk bundle cache. Today's primary
/// consumer is <c>func start</c>; the contribution point is open to other
/// commands.
/// </summary>
/// <remarks>
/// Scaffolding only in this release. <see cref="Configure"/> registers no
/// services yet; the <c>IExtensionBundleProvider</c> contribution point and
/// the resolver implementation land in a follow-up PR (see the
/// <a href="https://github.com/Azure/azure-functions-core-tools/blob/docs/proposed/bundles-workload-spec.md">bundles
/// workload spec</a>).
/// </remarks>
internal sealed class ExtensionBundlesWorkload : Workload
{
    public override string DisplayName => "Extension Bundles";

    public override string Description =>
        "Resolves and caches Azure Functions extension bundles for the func CLI.";

    public override void Configure(FunctionsCliBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // TODO: register IExtensionBundleProvider once the abstraction lands
        // (see proposed/bundles-workload-spec.md §4.2 and §5).
    }
}
