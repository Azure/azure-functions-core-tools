// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Workloads.DotNet;

public sealed class DotNetWorkload : Workloads.Workload
{
    public string PackageId => "Azure.Functions.Cli.Workload.DotNet";

    public string PackageVersion => "1.0.0";

    public override string DisplayName { get; } = ".NET";

    public override string Description { get; } = "func init / func new support for C#";

    public override void Configure(FunctionsCliBuilder builder)
    {
        builder.Services.AddSingleton<IProjectInitializer, DotNetProjectInitializer>();
    }
}
