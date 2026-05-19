// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Workloads.Tests.Fixtures.Sdk;

/// <summary>
/// SDK-shaped test fixture: behaves identically to the sibling
/// <c>Workloads.Tests.Fixtures.Default</c> stub, but its csproj follows the
/// future workload SDK convention (<c>Private="false"</c>,
/// <c>ExcludeAssets="runtime"</c>) so the contract assembly is excluded from
/// the fixture's runtime closure. This exercises the loader's natural-
/// resolution path: <see cref="System.Runtime.Loader.AssemblyDependencyResolver"/>
/// returns null for <c>Azure.Functions.Cli.Abstractions</c> and the default
/// context resolves it from the host's bin folder.
/// </summary>
public sealed class StubWorkload : Workloads.Workload
{
    public override string DisplayName => "Stub (SDK-shaped)";

    public override string Description => "SDK-convention test fixture workload.";

    public override void Configure(FunctionsCliBuilder builder)
    {
    }
}
