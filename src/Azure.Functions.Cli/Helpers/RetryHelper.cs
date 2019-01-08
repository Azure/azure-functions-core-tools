using System;
using System.Threading.Tasks;
using Colors.Net;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Helpers
{
    internal class RetryHelper
    {
        public static Task Retry(Func<Task> func, int retryCount, bool displayError = false)
            => Retry(func, retryCount, TimeSpan.FromSeconds(1), displayError);

        public static async Task Retry(Func<Task> func, int retryCount, TimeSpan retryDelay, bool displayError = false)
        {
            var totalRetries = retryCount;
            while (true)
            {
                try
                {
                    await func();
                    return;
                }
                catch (Exception e)
                {
                    if (retryCount <= 0)
                    {
                        throw e;
                    }
                    retryCount--;
                    if (displayError)
                    {
                        ColoredConsole.Error.WriteLine(ErrorColor(e.Message));
                        ColoredConsole.Error.WriteLine(ErrorColor($"Retry: {totalRetries - retryCount} of {totalRetries}"));
                    }
                }
                await Task.Delay(retryDelay);
            }
        }

        public static async Task<T> Retry<T>(Func<Task<T>> func, int retryCount, TimeSpan retryDelay, bool displayError = false)
        {
            var totalRetries = retryCount;
            while (true)
            {
                try
                {
                    return await func();
                }
                catch (Exception e)
                {
                    if (retryCount <= 0)
                    {
                        throw e;
                    }
                    retryCount--;
                    if (displayError)
                    {
                        ColoredConsole.Error.WriteLine(ErrorColor(e.Message));
                        ColoredConsole.Error.WriteLine(ErrorColor($"Retry: {totalRetries - retryCount} of {totalRetries}"));
                    }
                }
                await Task.Delay(retryDelay);
            }
        }
    }
}
