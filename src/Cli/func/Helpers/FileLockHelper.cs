// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Helpers
{
    public static class FileLockHelper
    {
        private const string DefaultLockFileName = "func.lock";
        private static readonly string _templatesLockFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".azurefunctions");

        public static async Task<T> WithFileLockAsync<T>(string lockFileName, Func<Task<T>> action)
        {
            lockFileName ??= DefaultLockFileName;
            var lockFile = Path.Combine(_templatesLockFilePath, lockFileName);

            for (int attempt = 0; attempt < 20; attempt++)
            {
                FileStream stream = null;
                try
                {
                    stream = new FileStream(
                        lockFile,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.Delete);

                    return await action();
                }
                catch (IOException)
                {
                    stream?.Dispose();
                    await Task.Delay(1000);
                }
                finally
                {
                    stream?.Dispose();
                }
            }

            throw new IOException($"Could not acquire file lock on {lockFile} after multiple attempts.");
        }

        public static Task WithFileLockAsync(string lockFileName, Func<Task> action)
        {
            return WithFileLockAsync(lockFileName, async () =>
            {
                await action();
                return true;
            });
        }
    }
}
