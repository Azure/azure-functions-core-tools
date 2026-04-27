// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.DependencyInjection;
using AbstractionsBuilder = Azure.Functions.Cli.Workloads.FunctionsCliBuilder;

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Default <see cref="AbstractionsBuilder"/> implementation. Internal so
/// workloads only see the abstract base type.
/// </summary>
internal sealed class DefaultFunctionsCliBuilder(IServiceCollection services) : AbstractionsBuilder
{
    public override IServiceCollection Services { get; } = services ?? throw new ArgumentNullException(nameof(services));
}
