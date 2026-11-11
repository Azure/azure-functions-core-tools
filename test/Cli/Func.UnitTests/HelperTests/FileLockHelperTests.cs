// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Helpers;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.HelperTests
{
    public class FileLockHelperTests : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"func-lock-tests-{Guid.NewGuid():N}");

        public FileLockHelperTests()
        {
            Directory.CreateDirectory(_directory);
        }

        public void Dispose() => Directory.Delete(_directory, recursive: true);

        [Fact]
        public async Task WithFileLockAsync_ActionIOExceptionIsNotRetriedAndReleasesLock()
        {
            string path = Path.Combine(_directory, "test.lock");
            var expected = new IOException("Action failed.");
            int calls = 0;

            var actual = await Assert.ThrowsAsync<IOException>(() =>
                FileLockHelper.WithFileLockAsync(path, () =>
                {
                    calls++;
                    throw expected;
                }));

            Assert.Same(expected, actual);
            Assert.Equal(1, calls);
            Assert.Equal(42, await FileLockHelper.WithFileLockAsync(path, () => Task.FromResult(42)));
        }

        [Fact]
        public async Task WithFileLockAsync_LockedFileReportsAcquisitionFailure()
        {
            string path = Path.Combine(_directory, "test.lock");
            using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            bool actionCalled = false;

            var exception = await Assert.ThrowsAsync<IOException>(() =>
                FileLockHelper.WithFileLockAsync(
                    path,
                    () =>
                    {
                        actionCalled = true;
                        return Task.CompletedTask;
                    },
                    maxAttempts: 1));

            Assert.False(actionCalled);
            Assert.Contains(path, exception.Message);
            Assert.IsType<IOException>(exception.InnerException);
        }
    }
}
