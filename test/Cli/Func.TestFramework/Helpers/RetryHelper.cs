// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;

namespace Func.TestFramework.Helpers
{
    public static class RetryHelper
    {
        public static async Task RetryAsync(Func<Task<bool>> condition, int timeout = 120 * 1000, int pollingInterval = 2 * 1000, bool throwWhenDebugging = false, Func<string>? userMessageCallback = null)
        {
            DateTime start = DateTime.Now;
            int attempt = 1;
            while (!await condition())
            {
                await Task.Delay(pollingInterval);
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
