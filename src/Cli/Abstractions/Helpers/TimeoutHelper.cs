// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Abstractions.Helpers
{
    public static class TimeoutHelper
    {
        /// <summary>
        /// Executes a task with a timeout. Throws TimeoutException if the task doesn't complete within the specified time.
        /// </summary>
        public static async Task RunWithTimeoutAsync(Task task, TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            try
            {
                await task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
            {
                throw new TimeoutException($"Task timed out after {timeout.TotalSeconds} seconds");
            }
        }
    }
}
