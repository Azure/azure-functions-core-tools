// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Azure.WebJobs.Script;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Workloads.Host.Logging;

internal static class HostStructuredLoggingBuilderExtensions
{
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
        builder.AddFilter(static (category, logLevel) =>
        {
            bool isSharedMemoryWarning = logLevel == LogLevel.Warning
                && string.Equals(
                    category,
                    "Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer.MemoryMappedFileAccessor",
                    StringComparison.Ordinal);

            bool isAppInsightsExtensionWarning = logLevel == LogLevel.Warning
                && string.Equals(
                    category,
                    "Microsoft.Azure.WebJobs.Script.DependencyInjection.ScriptStartupTypeLocator",
                    StringComparison.Ordinal);

            return !isSharedMemoryWarning && !isAppInsightsExtensionWarning;
        });

        return builder;
    }
}
