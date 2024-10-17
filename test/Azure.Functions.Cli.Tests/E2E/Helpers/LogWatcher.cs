using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.Tests.E2E.Helpers
{
    public static class LogWatcher
    {
        public static async Task WaitForLogOutput(StringBuilder stdout, string expectedOutput, TimeSpan timeout)
        {
            var tcs = new TaskCompletionSource<bool>();
            using var cancellationTokenSource = new CancellationTokenSource(timeout);

            if (stdout.ToString().Contains(expectedOutput))
            {
                tcs.SetResult(true);
            }
            else
            {
                var timer = new Timer(state =>
                {
                    if (stdout.ToString().Contains(expectedOutput))
                    {
                        tcs.TrySetResult(true);
                    }
                }, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));

                // Cancel the waiting task if the timeout is reached
                cancellationTokenSource.Token.Register(() =>
                {
                    tcs.TrySetCanceled();
                    timer.Dispose();
                });
            }

            await tcs.Task;
        }
    }
}
