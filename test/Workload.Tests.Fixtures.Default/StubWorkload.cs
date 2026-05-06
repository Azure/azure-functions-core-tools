// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Workload.Tests.Fixtures.Default;

public sealed class StubWorkload : Workloads.Workload
{
    public override string DisplayName => "Stub";

    public override string Description => "Test fixture workload.";

    public override void Configure(FunctionsCliBuilder builder)
    {
    }
}
