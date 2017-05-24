using System;
using System.Threading.Tasks;
using Colors.Net;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Helpers
{
    internal class RetryHelper
    {
        public static async Task Retry(Func<Task> func, int retryCount)
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
                    ColoredConsole.Error.WriteLine(ErrorColor(e.Message));
                    ColoredConsole.Error.WriteLine(ErrorColor($"Retry: {totalRetries - retryCount} of {totalRetries}"));
                }
                await Task.Delay(1000);
            }
        }
    }
}
