// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Default <see cref="FunctionsCliBuilder"/> implementation. Internal so
/// workloads only see the abstract base type.
/// </summary>
internal sealed class DefaultFunctionsCliBuilder(IServiceCollection services) : FunctionsCliBuilder
{
    public override IServiceCollection Services { get; } = services ?? throw new ArgumentNullException(nameof(services));
}
