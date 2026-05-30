// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Workloads.Host.Logging;

internal static class HostStructuredLoggingBuilderExtensions
{
    private const LogLevel DefaultSystemMinimumLogLevel = LogLevel.Warning;
    private const LogLevel DefaultUserMinimumLogLevel = LogLevel.Information;

    private static readonly CategoryLogLevelFilter[] _categoryLogLevelFilters =
    [
        new("Azure.", LogLevel.Error),
        new("Yarp.", LogLevel.None),
        new("Microsoft.AspNetCore.", LogLevel.None),
        new("Microsoft.Azure.WebJobs.Hosting.OptionsLoggingService", LogLevel.None),
        new("Microsoft.Azure.WebJobs.Script.DependencyInjection.ScriptStartupTypeLocator", LogLevel.Error),
        new("Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer.MemoryMappedFileAccessor", LogLevel.Error),
    ];

    public static ILoggingBuilder AddHostStructuredLogging(this ILoggingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSingleton<ILoggerProvider, HostStructuredLoggerProvider>();
        builder.AddDefaultWebJobsFilters<HostStructuredLoggerProvider>(LogLevel.Trace);
        builder.AddHostStructuredLogFilters();

        return builder;
    }

    private static ILoggingBuilder AddHostStructuredLogFilters(this ILoggingBuilder builder)
    {
        builder.AddFilter<HostStructuredLoggerProvider>(static (category, logLevel) => logLevel >= GetMinimumLogLevel(category));

        return builder;
    }

    private static LogLevel GetMinimumLogLevel(string? category)
    {
        if (string.IsNullOrEmpty(category))
        {
            return DefaultUserMinimumLogLevel;
        }

        foreach (CategoryLogLevelFilter categoryFilter in _categoryLogLevelFilters)
        {
            if (categoryFilter.Matches(category))
            {
                return categoryFilter.MinimumLogLevel;
            }
        }

        if (IsSystemLogCategory(category))
        {
            return DefaultSystemMinimumLogLevel;
        }

        return DefaultUserMinimumLogLevel;
    }

    private static bool IsSystemLogCategory(string category)
    {
        if (IsUserLogCategory(category))
        {
            return false;
        }

        foreach (string prefix in ScriptConstants.SystemLogCategoryPrefixes)
        {
            if (category.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUserLogCategory(string category)
        => LogCategories.IsFunctionUserCategory(category)
            || LogCategories.IsFunctionCategory(category)
            || string.Equals(category, WorkerConstants.ConsoleLogCategoryName, StringComparison.OrdinalIgnoreCase);

    private readonly record struct CategoryLogLevelFilter(string CategoryPrefix, LogLevel MinimumLogLevel)
    {
        public bool Matches(string category) => category.StartsWith(CategoryPrefix, StringComparison.Ordinal);
    }
}
