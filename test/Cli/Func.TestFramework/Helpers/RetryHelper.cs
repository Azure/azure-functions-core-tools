
using System.Diagnostics;


namespace Func.TestFramework.Helpers
{
    public static class RetryHelper
    {
        public static async Task RetryAsync(Func<Task<bool>> condition, StreamWriter? fileWriter = null, int timeout = 180 * 1000, int pollingInterval = 2 * 1000, bool throwWhenDebugging = false, Func<string> userMessageCallback = null)
        {
            DateTime start = DateTime.Now;
            int attempt = 1;
            while (!await condition())
            {
                await Task.Delay(pollingInterval);
                attempt += 1;
                fileWriter?.WriteLine($"Attempt: {attempt}");
                fileWriter?.Flush();

                bool shouldThrow = !Debugger.IsAttached || (Debugger.IsAttached && throwWhenDebugging);
                fileWriter?.WriteLine($"Should throw: {shouldThrow}");
                fileWriter?.Flush();
                if (shouldThrow && (DateTime.Now - start).TotalMilliseconds > timeout)
                {
                    fileWriter?.WriteLine($"Condition not reached within timeout");
                    fileWriter?.Flush();
                    string error = "Condition not reached within timeout.";
                    if (userMessageCallback != null)
                    {
                        error += " " + userMessageCallback();
                    }
                    throw new ApplicationException(error);
                }
                fileWriter?.WriteLine($"Aight trying again");
                fileWriter?.Flush();
            }
        }

        public static async Task RetryUntilTimeoutAsync(Func<Task<bool>> operation, StreamWriter fileWriter, int pollingInterval = 2000)
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
                    fileWriter.Flush();
                    // Try the operation
                    if (await operation())
                    {
                        fileWriter.WriteLine("actually succeeded!");
                        fileWriter.Flush();
                        // Success! We're done
                        return;
                    }

                    fileWriter.WriteLine($"Retry until timeout return false");
                    fileWriter.Flush();
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

                fileWriter.WriteLine($"Gonna hit polling interval");
                fileWriter.Flush();

                // Wait before the next attempt
                //await Task.Delay(pollingInterval);

                fileWriter.WriteLine($"Done with polling interval");
                fileWriter.Flush();
            }
        }

        public static IEnumerable<TimeSpan> TestingIntervals
        {
            get
            {
                while (true)
                {
                    yield return TimeSpan.FromSeconds(0);
                }
            }
        }

        public static async Task ExecuteAsyncWithRetry(Func<Task<bool>> action,
            Func<bool, bool> shouldStopRetry,
            int maxRetryCount,
            Func<IEnumerable<Task>> timer,
            StreamWriter fileWriter,
            string taskDescription = "")
        {
            var count = 0;
            foreach (var t in timer())
            {
                fileWriter.WriteLine($"Starting timer");
                fileWriter.Flush();
                await t;
                count++;

                fileWriter.WriteLine($"Value of count: {count}");
                fileWriter.Flush();

                bool result = await action();

                if (shouldStopRetry(result))
                {
                    fileWriter.WriteLine($"Success! Returning");
                    fileWriter.Flush();
                    return;
                }
                fileWriter.WriteLine($"Failed; trying again");
                fileWriter.Flush();

                if (count == maxRetryCount)
                {
                    throw new TimeoutException("Reached max retry count");
                }
            }
            throw new Exception("Timer should not be exhausted");
        }

    }
}
