// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Host.Logging;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Workloads.Host.Tests;

public sealed class HostStructuredScriptLoggingBuilderTests
{
    private const string AzureCoreCategory = "Azure.Core";
    private const string AppInsightsExtensionWarningCategory = "Microsoft.Azure.WebJobs.Script.DependencyInjection.ScriptStartupTypeLocator";
    private const string SharedMemoryWarningCategory =
        "Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer.MemoryMappedFileAccessor";
    private const string SystemTraceMiddlewareCategory = "Microsoft.Azure.WebJobs.Script.WebHost.Middleware.SystemTraceMiddleware";
    private const string OptionsLoggingCategory = "Microsoft.Azure.WebJobs.Hosting.OptionsLoggingService";

    [Fact]
    public void Configure_RegistersStructuredLoggerProvider()
    {
        var services = new ServiceCollection();
        var configureBuilder = new HostStructuredScriptLoggingBuilder();

        services.AddLogging(configureBuilder.Configure);

        using ServiceProvider provider = services.BuildServiceProvider();
        IEnumerable<ILoggerProvider> loggerProviders = provider.GetRequiredService<IEnumerable<ILoggerProvider>>();
        loggerProviders.Should().Contain(static provider => provider is HostStructuredLoggerProvider);
    }

    [Theory]
    [InlineData(AzureCoreCategory, LogLevel.Information, false)]
    [InlineData(AzureCoreCategory, LogLevel.Warning, false)]
    [InlineData(AzureCoreCategory, LogLevel.Error, true)]
    [InlineData(AppInsightsExtensionWarningCategory, LogLevel.Warning, false)]
    [InlineData(AppInsightsExtensionWarningCategory, LogLevel.Error, true)]
    [InlineData(SharedMemoryWarningCategory, LogLevel.Warning, false)]
    [InlineData(SharedMemoryWarningCategory, LogLevel.Error, true)]
    [InlineData(OptionsLoggingCategory, LogLevel.Warning, false)]
    [InlineData(OptionsLoggingCategory, LogLevel.Error, false)]
    [InlineData(OptionsLoggingCategory, LogLevel.Critical, false)]
    public void Configure_AppliesCategoryLogLevelFilters(string category, LogLevel logLevel, bool expected)
    {
        var services = new ServiceCollection();
        var configureBuilder = new HostStructuredScriptLoggingBuilder();

        services.AddLogging(configureBuilder.Configure);

        using ServiceProvider provider = services.BuildServiceProvider();
        LoggerFilterOptions filterOptions = provider.GetRequiredService<IOptions<LoggerFilterOptions>>().Value;

        AssertStructuredLogFilterResult(filterOptions, category, logLevel, expected);
    }

    [Theory]
    [InlineData(SystemTraceMiddlewareCategory, LogLevel.Information, false)]
    [InlineData(SystemTraceMiddlewareCategory, LogLevel.Warning, true)]
    [InlineData("Host.Startup", LogLevel.Information, false)]
    [InlineData("Host.Startup", LogLevel.Warning, true)]
    [InlineData("Function.HttpTrigger1.User", LogLevel.Debug, false)]
    [InlineData("Function.HttpTrigger1.User", LogLevel.Information, true)]
    [InlineData("Custom.Category", LogLevel.Debug, false)]
    [InlineData("Custom.Category", LogLevel.Information, true)]
    public void Configure_AppliesDefaultStructuredLogFilters(string category, LogLevel logLevel, bool expected)
    {
        var services = new ServiceCollection();
        var configureBuilder = new HostStructuredScriptLoggingBuilder();

        services.AddLogging(configureBuilder.Configure);

        using ServiceProvider provider = services.BuildServiceProvider();
        LoggerFilterOptions filterOptions = provider.GetRequiredService<IOptions<LoggerFilterOptions>>().Value;

        AssertStructuredLogFilterResult(filterOptions, category, logLevel, expected);
    }

    [Fact]
    public void Configure_AppliesUserLogLevelToWorkerConsoleLogs()
    {
        var services = new ServiceCollection();
        var configureBuilder = new HostStructuredScriptLoggingBuilder();

        services.AddLogging(configureBuilder.Configure);

        using ServiceProvider provider = services.BuildServiceProvider();
        LoggerFilterOptions filterOptions = provider.GetRequiredService<IOptions<LoggerFilterOptions>>().Value;

        AssertStructuredLogFilterResult(filterOptions, WorkerConstants.ConsoleLogCategoryName, LogLevel.Debug, false);
        AssertStructuredLogFilterResult(filterOptions, WorkerConstants.ConsoleLogCategoryName, LogLevel.Information, true);
    }

    private static void AssertStructuredLogFilterResult(LoggerFilterOptions filterOptions, string category, LogLevel logLevel, bool expected)
        => filterOptions.Rules.Any(rule =>
            string.Equals(rule.ProviderName, typeof(HostStructuredLoggerProvider).FullName, StringComparison.Ordinal)
            && rule.Filter?.Invoke(rule.ProviderName, category, logLevel) == expected).Should().BeTrue();
}
