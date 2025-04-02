
using System.Diagnostics;


namespace Func.TestFramework.Helpers
{
    public static class RetryHelper
    {
        public static async Task RetryAsync(Func<Task<bool>> condition, int timeout = 120 * 1000, int pollingInterval = 2 * 1000, bool throwWhenDebugging = false, Func<string> userMessageCallback = null)
        {
            DateTime start = DateTime.Now;
            while (!await condition())
            {
                await Task.Delay(pollingInterval);

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
            }
        }

        public static async Task RetryUntilTimeoutAsync(Func<bool> operation, StreamWriter fileWriter, int pollingInterval = 2000)
        {
            DateTime startTime = DateTime.Now;
            int attemptCount = 0;

            // Define timeout as 2 minutes (120 seconds)
            TimeSpan timeout = TimeSpan.FromMinutes(2);

            while (true)
            {
                attemptCount++;

                try
                {
                    fileWriter.WriteLine($"Attempt {attemptCount}");
                    // Try the operation
                    if (operation())
                    {
                        // Success! We're done
                        return;
                    }
                }
                catch (Exception ex)
                {
                    // Log the exception but continue retrying
                    fileWriter.WriteLine($"Attempt {attemptCount} failed with error: {ex.Message}");
                    fileWriter.Flush();
                }

                // Check if we've timed out
                TimeSpan elapsed = DateTime.Now - startTime;
                if (elapsed >= timeout)
                {
                    fileWriter.WriteLine($"Operation timed out after {elapsed.TotalSeconds:F1} seconds and {attemptCount} attempts");
                    fileWriter.Flush();
                    throw new TimeoutException(
                        $"Operation timed out after {elapsed.TotalSeconds:F1} seconds and {attemptCount} attempts");
                }

                // Wait before the next attempt
                await Task.Delay(pollingInterval);
            }
        }
    }
}
