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

            logger?.WriteLine($"About to evaluate condition for attempt {attempt}");
            bool conditionResult = condition();
            logger?.WriteLine($"Condition result for attempt {attempt}: {conditionResult}");

            while (!conditionResult)
            {
                await Task.Delay(pollingInterval);
                attempt += 1;
                logger?.WriteLine($"Attempt within retry: {attempt}");

                bool shouldThrow = !Debugger.IsAttached || (Debugger.IsAttached && throwWhenDebugging);
                double elapsedMs = (DateTime.Now - start).TotalMilliseconds;
                logger?.WriteLine($"Elapsed time: {elapsedMs}ms, timeout: {timeout}ms, shouldThrow: {shouldThrow}");

                if (shouldThrow && elapsedMs > timeout)
                {
                    logger?.WriteLine($"Throwing condition not reached within timeout. Current time is {DateTime.Now}");
                    string error = "Condition not reached within timeout.";
                    if (userMessageCallback != null)
                    {
                        error += " " + userMessageCallback();
                    }
                    throw new ApplicationException(error);
                }

                logger?.WriteLine($"Trying again, about to evaluate condition for attempt {attempt}");
                conditionResult = condition();
                logger?.WriteLine($"Condition result for attempt {attempt}: {conditionResult}");
            }
        }
    }
}
