// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workload.Tests.Fixtures.Second;
using Azure.Functions.Cli.Workloads;

[assembly: CliWorkload<SecondStubWorkload>]

namespace Azure.Functions.Cli.Workload.Tests.Fixtures.Second;

public sealed class SecondStubWorkload : IWorkload
{
    public string PackageId => "Azure.Functions.Cli.Workload.Tests.Fixtures.Second";

    public string PackageVersion => "1.0.0";

    public string DisplayName => "Stub2";

    public string Description => "Second test fixture workload (used to assert duplicate-attribute detection).";

    public void Configure(FunctionsCliBuilder builder)
    {
    }
}
