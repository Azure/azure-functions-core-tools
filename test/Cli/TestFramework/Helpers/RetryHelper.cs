// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;

namespace Azure.Functions.Cli.TestFramework.Helpers
{
    public static class RetryHelper
    {
        public static async Task RetryAsync(Func<Task<bool>> condition, int timeout = 60 * 1000, int pollingInterval = 2 * 1000, bool throwWhenDebugging = false, Func<string>? userMessageCallback = null)
        {
            DateTime start = DateTime.Now;
            int attempt = 1;
            while (!await condition())
            {
                // Use exponential backoff after first few attempts: 2s, 2s, 2s, 4s, 8s, then cap at 10s
                int currentDelay = attempt <= 3 ? pollingInterval
                    : Math.Min(pollingInterval * (int)Math.Pow(2, attempt - 3), 10000);

                await Task.Delay(currentDelay);
                attempt += 1;

                bool shouldThrow = !Debugger.IsAttached || (Debugger.IsAttached && throwWhenDebugging);

                if (shouldThrow && (DateTime.Now - start).TotalMilliseconds > timeout)
                {
                    string error = "Condition not reached within timeout.";
                    if (userMessageCallback is not null)
                    {
                        error += " " + userMessageCallback();
                    }

                    throw new ApplicationException(error);
                }
            }
        }
    }
}
