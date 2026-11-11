// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Helpers
{
    public static class FileLockHelper
    {
        private const string DefaultLockFileName = "func.lock";
        private static readonly string _templatesLockFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".azurefunctions");

        public static async Task<T> WithFileLockAsync<T>(string lockFileName, Func<Task<T>> action, int maxAttempts = 20)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAttempts);
            lockFileName ??= DefaultLockFileName;
            var lockFile = Path.Combine(_templatesLockFilePath, lockFileName);
            IOException lastException = null;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                FileStream stream = null;
                try
                {
                    stream = new FileStream(
                        lockFile,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None);
                }
                catch (IOException ex)
                {
                    lastException = ex;
                }

                if (stream is not null)
                {
                    using (stream)
                    {
                        return await action();
                    }
                }

                if (attempt < maxAttempts - 1)
                {
                    await Task.Delay(1000);
                }
            }

            throw new IOException($"Could not acquire file lock on {lockFile} after {maxAttempts} attempts.", lastException);
        }

        public static Task WithFileLockAsync(string lockFileName, Func<Task> action, int maxAttempts = 20)
        {
            return WithFileLockAsync(
                lockFileName,
                async () =>
                {
                    await action();
                    return true;
                },
                maxAttempts);
        }
    }
}
