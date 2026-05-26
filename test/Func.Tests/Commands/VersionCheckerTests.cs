// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands;

public class VersionCheckerTests
{
    [Fact]
    public async Task CheckForUpdateAsync_ReturnsNullOnCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Should not throw — returns null on cancellation
        var result = await VersionChecker.CheckForUpdateAsync(cts.Token);
        Assert.Null(result);
    }

    [Fact]
    public async Task CheckForUpdateAsync_DoesNotThrow()
    {
        // Even if network is unavailable, should return null gracefully
        var result = await VersionChecker.CheckForUpdateAsync();

        // Result can be null (offline/current) or a version string — both are valid
        if (result is not null)
        {
            Assert.True(Version.TryParse(result, out _), $"Expected valid version, got: {result}");
        }
    }

    [Fact]
    public async Task CheckForUpdateAsync_UsesUserConfigurationPathsForCache()
    {
        string userHome = Path.Combine(Path.GetTempPath(), "func-cli-tests", Guid.NewGuid().ToString("N"));
        var paths = new UserConfigurationPathsOptions(userHome);

        try
        {
            Directory.CreateDirectory(paths.Home);
            File.WriteAllText(paths.VersionCachePath, "9999.0.0");

            string? result = await VersionChecker.CheckForUpdateAsync(paths, CancellationToken.None);

            Assert.Equal("9999.0.0", result);
        }
        finally
        {
            if (Directory.Exists(userHome))
            {
                Directory.Delete(userHome, recursive: true);
            }
        }
    }
}
