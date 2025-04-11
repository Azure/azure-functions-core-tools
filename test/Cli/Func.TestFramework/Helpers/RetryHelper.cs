// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;
using Xunit.Abstractions;


namespace Func.TestFramework.Helpers
{
    public static class RetryHelper
    {
        public static async Task RetryAsync(Func<Task<bool>> condition, StreamWriter? fileWriter = null, int timeout = 120 * 1000, int pollingInterval = 2 * 1000, bool throwWhenDebugging = false, Func<string> userMessageCallback = null, ITestOutputHelper logger = null)
        {
            DateTime start = DateTime.Now;
            int attempt = 1;
            while (!await condition())
            {
                await Task.Delay(pollingInterval);
                attempt += 1;
                logger?.WriteLine($"Attempt: {attempt}");

                bool shouldThrow = !Debugger.IsAttached || (Debugger.IsAttached && throwWhenDebugging);

                if (shouldThrow && (DateTime.Now - start).TotalMilliseconds > timeout)
                {
                    string error = "Condition not reached within timeout.";
                    if (userMessageCallback != null)
                    {
                        error += " " + userMessageCallback();
                    }
                    throw new ApplicationException(error);
                }
                logger?.WriteLine($"Trying again");
            }
        }

        public static async Task Retry(Func<bool> condition, StreamWriter? fileWriter = null, int timeout = 120 * 1000, int pollingInterval = 2 * 1000, bool throwWhenDebugging = false, Func<string> userMessageCallback = null, ITestOutputHelper logger = null)
        {
            DateTime start = DateTime.Now;
            int attempt = 1;
            while (!condition())
            {
                await Task.Delay(pollingInterval);
                attempt += 1;
                logger?.WriteLine($"Attempt within retry: {attempt}");

                bool shouldThrow = !Debugger.IsAttached || (Debugger.IsAttached && throwWhenDebugging);

                if (shouldThrow && (DateTime.Now - start).TotalMilliseconds > timeout)
                {
                    logger?.WriteLine($"Throwing condition not reached within timeout");
                    string error = "Condition not reached within timeout.";
                    if (userMessageCallback != null)
                    {
                        error += " " + userMessageCallback();
                    }
                    throw new ApplicationException(error);
                }
                logger?.WriteLine($"Trying again");
            }
        }
    }
}
