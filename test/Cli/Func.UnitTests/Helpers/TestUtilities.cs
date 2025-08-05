// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.Configuration;

namespace Azure.Functions.Cli.UnitTests
{
    public static class TestUtilities
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

        public static async Task<bool> WaitForConditionAsync(Func<bool> condition, TimeSpan timeout, int pollIntervalMs = 50)
        {
            var start = DateTime.UtcNow;
            while (DateTime.UtcNow - start < timeout)
            {
                if (condition())
                {
                    return true;
                }

                await Task.Delay(pollIntervalMs);
            }

            return condition();
        }
    }
}
