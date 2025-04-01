
using System.Diagnostics;


namespace Func.TestFramework.Helpers
{
    public static class RetryHelper
    {
        /*
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
        */

        public static async Task RetryAsync(Func<Task<bool>> condition, int timeout = 120 * 1000,
    int pollingInterval = 2 * 1000, CancellationToken cancellationToken = default, bool throwWhenDebugging = false, Func<string> userMessageCallback = null)
        {
            DateTime start = DateTime.Now;
            // Create a timeout-based cancellation token
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                timeoutCts.Token, cancellationToken);

            while (true)
            {
                // Check for cancellation first
                if (linkedCts.Token.IsCancellationRequested)
                {
                    throw new OperationCanceledException("Operation was canceled", linkedCts.Token);
                }

                // Try the condition
                bool result = false;
                try
                {
                    result = await condition();
                }
                catch (Exception ex)
                {
                    if (ex is OperationCanceledException)
                        throw;

                    // Log but continue with retries
                    Console.WriteLine($"Error in retry condition: {ex.Message}");
                }

                if (result)
                    return; // Success!

                // Check timeout explicitly
                if ((DateTime.Now - start).TotalMilliseconds > timeout)
                {
                    throw new TimeoutException($"Condition not reached within timeout of {timeout}ms.");
                }

                // Wait for next retry with cancellation support
                try
                {
                    await Task.Delay(pollingInterval, linkedCts.Token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
            }
        }
    }
}
