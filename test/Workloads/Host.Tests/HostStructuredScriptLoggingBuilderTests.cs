// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Host.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Azure.Functions.Cli.Workloads.Host.Tests;

public sealed class HostStructuredScriptLoggingBuilderTests
{
    [Fact]
    public void Configure_RegistersStructuredLoggerProvider()
    {
        var services = new ServiceCollection();
        var configureBuilder = new HostStructuredScriptLoggingBuilder();

        services.AddLogging(configureBuilder.Configure);

        using ServiceProvider provider = services.BuildServiceProvider();
        IEnumerable<ILoggerProvider> loggerProviders = provider.GetRequiredService<IEnumerable<ILoggerProvider>>();
        Assert.Contains(loggerProviders, static provider => provider is HostStructuredLoggerProvider);
    }

    [Fact]
    public void Configure_AppliesStructuredLogSuppressionFilters()
    {
        var services = new ServiceCollection();
        var configureBuilder = new HostStructuredScriptLoggingBuilder();

        services.AddLogging(configureBuilder.Configure);

        using ServiceProvider provider = services.BuildServiceProvider();
        LoggerFilterOptions filterOptions = provider.GetRequiredService<IOptions<LoggerFilterOptions>>().Value;

        Assert.Contains(filterOptions.Rules, static rule => rule.Filter?.Invoke(
            null,
            "Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer.MemoryMappedFileAccessor",
            LogLevel.Warning) == false);
        Assert.Contains(filterOptions.Rules, static rule => rule.Filter?.Invoke(
            null,
            "Microsoft.Azure.WebJobs.Script.DependencyInjection.ScriptStartupTypeLocator",
            LogLevel.Warning) == false);
    }
}
