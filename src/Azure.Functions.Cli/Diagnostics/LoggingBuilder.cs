using System;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Diagnostics
{
    internal class LoggingBuilder : IConfigureBuilder<ILoggingBuilder>
    {
        public void Configure(ILoggingBuilder builder)
        {
            builder.AddProvider(new ColoredConsoleLoggerProvider((cat, level) => level >= LogLevel.Information));
        }
    }
}
