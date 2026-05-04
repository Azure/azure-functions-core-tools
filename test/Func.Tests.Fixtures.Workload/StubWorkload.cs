// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Tests.Fixtures.Workload;
using Azure.Functions.Cli.Workloads;

[assembly: CliWorkload<StubWorkload>]

namespace Azure.Functions.Cli.Tests.Fixtures.Workload;

public sealed class StubWorkload : IWorkload
{
    public string PackageId => "Azure.Functions.Cli.Tests.Fixtures.Workload";

    public string PackageVersion => "1.0.0";

    public string DisplayName => "Stub";

    public string Description => "Test fixture workload.";

    public void Configure(FunctionsCliBuilder builder)
    {
    }
}
