// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Bootstrap surface passed to <see cref="IFunctionsCliStartup.Configure"/>.
/// Workloads register their services — project initializers, command
/// providers, and any other supporting types — through <see cref="Services"/>.
///
/// Modeled after WebJobs' <c>IWebJobsBuilder</c>: the builder exposes the DI
/// container so workloads can use any standard .NET DI extension method, and
/// the host picks up the registered services after all startups have run.
/// </summary>
public interface IFunctionsCliBuilder
{
    /// <summary>The DI service collection workloads contribute to.</summary>
    public IServiceCollection Services { get; }
}
