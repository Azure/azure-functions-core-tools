using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Extensions.Configuration;

namespace Azure.Functions.Cli.Diagnostics
{
    internal class DisableConsoleConfigurationBuilder : IConfigureBuilder<IConfigurationBuilder>
    {
        public void Configure(IConfigurationBuilder builder)
        {
            // The CLI runs in debug (SelfHost) mode, which means Functions automatically add
            // the ConsoleLogger. We don't want that since this uses the ColoredConsoleLogger,
            // so we explicitly turn it off here.
            builder.AddInMemoryCollection(new Dictionary<string, string>
            {
                { "AzureFunctionsJobHost:logging:console:isEnabled", "false" }
            });
        }
    }
}
