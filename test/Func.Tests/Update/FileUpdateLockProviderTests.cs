// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Update;

namespace Azure.Functions.Cli.Tests.Update;

public sealed class FileUpdateLockProviderTests
{
    [Fact]
    public void Acquire_LockAlreadyHeld_ThrowsGraceful()
    {
        string installDirectory = CreateInstallDirectory();
        try
        {
            var provider = new FileUpdateLockProvider();
            using IDisposable first = provider.Acquire(installDirectory);

            GracefulException exception = Assert.Throws<GracefulException>(
                () => provider.Acquire(installDirectory));

            Assert.True(exception.IsUserError);
            Assert.Contains("already running", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(installDirectory, recursive: true);
        }
    }

    [Fact]
    public void Acquire_PreviousLockDisposed_AllowsNextUpdate()
    {
        string installDirectory = CreateInstallDirectory();
        try
        {
            var provider = new FileUpdateLockProvider();
            provider.Acquire(installDirectory).Dispose();

            using IDisposable second = provider.Acquire(installDirectory);

            Assert.True(File.Exists(Path.Combine(installDirectory, FileUpdateLockProvider.LockFileName)));
        }
        finally
        {
            Directory.Delete(installDirectory, recursive: true);
        }
    }

    [Fact]
    public void Acquire_MissingInstallDirectory_PreservesDirectoryNotFoundError()
    {
        string installDirectory = CreateInstallDirectory();
        try
        {
            var provider = new FileUpdateLockProvider();

            Assert.Throws<DirectoryNotFoundException>(
                () => provider.Acquire(Path.Combine(installDirectory, "missing")));
        }
        finally
        {
            Directory.Delete(installDirectory, recursive: true);
        }
    }

    [Fact]
    public void Acquire_LockPathIsDirectory_PreservesAccessError()
    {
        string installDirectory = CreateInstallDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(installDirectory, FileUpdateLockProvider.LockFileName));
            var provider = new FileUpdateLockProvider();

            Assert.Throws<UnauthorizedAccessException>(() => provider.Acquire(installDirectory));
        }
        finally
        {
            Directory.Delete(installDirectory, recursive: true);
        }
    }

    private static string CreateInstallDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"func-update-lock-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
