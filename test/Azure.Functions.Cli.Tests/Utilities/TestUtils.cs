using Microsoft.Extensions.Configuration;
using System.Collections.Generic;

namespace Azure.Functions.Cli.Tests
{
    public static class TestUtils
    {
        public static IConfigurationRoot CreateSetupWithConfiguration(Dictionary<string, string> settings = null)
        {
            var builder = new ConfigurationBuilder();
            if (settings != null)
            {
                builder.AddInMemoryCollection(settings);
            }

            return builder.Build();
        }
    }
}
