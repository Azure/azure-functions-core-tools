// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Default <see cref="IFunctionsCliBuilder"/> implementation. Internal so
/// workloads only see the abstraction.
/// </summary>
internal sealed class FunctionsCliBuilder : IFunctionsCliBuilder
{
    public FunctionsCliBuilder(IServiceCollection services)
    {
        Services = services;
    }

    public IServiceCollection Services { get; }
}
