using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestFramework.Helpers
{
    public static class LogWatcher
    {
        public static async Task WaitForLogOutput(StreamReader stdout, string expectedOutput, TimeSpan timeout)
        {
            var tcs = new TaskCompletionSource<bool>();
            using var cancellationTokenSource = new CancellationTokenSource(timeout);

            // First check current content
            string currentContent = stdout.ReadToEnd();
            stdout.BaseStream.Position = 0; // Reset position to start

            if (currentContent.Contains(expectedOutput))
            {
                tcs.SetResult(true);
            }
            else
            {
                var timer = new Timer(state =>
                {
                    try
                    {
                        // Save the current position
                        long currentPosition = stdout.BaseStream.Position;

                        // Check if there's new content
                        if (stdout.Peek() > -1)
                        {
                            string newContent = stdout.ReadToEnd();
                            if (newContent.Contains(expectedOutput))
                            {
                                tcs.TrySetResult(true);
                                return;
                            }

                            // Reset position back to the end
                            stdout.BaseStream.Position = stdout.BaseStream.Length;
                        }
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
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
