using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Text;

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
