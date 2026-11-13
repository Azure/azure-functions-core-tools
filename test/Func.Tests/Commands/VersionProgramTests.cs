// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Workloads.Storage;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.Tests.Commands;

public class VersionProgramTests(ITestOutputHelper output)
{
    private readonly CliProcessTestRunner _runner = new(output);

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("--version")]
    [InlineData("version")]
    public async Task Program_VersionInvocation_PrintsExactSemanticVersion(string command)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"func-version-{Guid.NewGuid():N}");
        CliConfigurationPathsOptions paths = new(Path.Combine(directory, "home"));
        WorkloadPathsOptions workloads = new(Path.Combine(directory, "workload-home"));
        string project = Path.Combine(directory, "project");
        string informationalVersion = typeof(VersionCommand).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        string version = informationalVersion.Split('+')[0];
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(60));

        try
        {
            Directory.CreateDirectory(paths.Home);
            Directory.CreateDirectory(project);
            // Keep optional update notices out of the version-output assertion.
            await File.WriteAllTextAsync(paths.VersionCachePath, "0.0.0", cancellation.Token);

            CliProcessTestRunner.CliResult result = await _runner.RunAsync(directory, paths.Home, workloads.Home, project, [command], cancellation.Token);

            result.ExitCode.Should().Be(0);
            result.StandardError.Should().BeEmpty();
            result.StandardOutput.Should().Be(version + Environment.NewLine).And.NotContain("+");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
