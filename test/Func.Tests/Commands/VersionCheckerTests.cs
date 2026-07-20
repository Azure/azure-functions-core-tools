// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;

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
        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckForUpdateAsync_DoesNotThrow()
    {
        // Even if network is unavailable, should return null gracefully
        var result = await VersionChecker.CheckForUpdateAsync();

        // Result can be null (offline/current) or a version string — both are valid
        if (result is not null)
        {
            Version.TryParse(result, out _).Should().BeTrue($"Expected valid version, got: {result}");
        }
    }

    [Fact]
    public async Task CheckForUpdateAsync_UsesUserConfigurationPathsForCache()
    {
        string userHome = Path.Combine(Path.GetTempPath(), "func-cli-tests", Guid.NewGuid().ToString("N"));
        var paths = new CliConfigurationPathsOptions(userHome);

        try
        {
            Directory.CreateDirectory(paths.Home);
            File.WriteAllText(paths.VersionCachePath, "9999.0.0");

            string? result = await VersionChecker.CheckForUpdateAsync(paths, CancellationToken.None);

            result.Should().Be("9999.0.0");
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
