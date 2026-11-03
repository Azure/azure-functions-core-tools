// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Hosting.FirstRun;
using Azure.Functions.Cli.Workloads.Storage;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.Tests.Commands;

public class VersionProgramTests(ITestOutputHelper output)
{
    private readonly CliProcessTestRunner _runner = new(output);

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("--version", false)]
    [InlineData("version", false)]
    [InlineData("--verbose", true)]
    public async Task Program_VersionInspection_PreservesVersionAndBuildDetailsOnRepeat(string command, bool detailed)
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
            await File.WriteAllTextAsync(paths.VersionCachePath, "0.0.0", cancellation.Token);

            CliProcessTestRunner.CliResult first = await _runner.RunAsync(directory, paths.Home, workloads.Home, project, [command], cancellation.Token);
            CliProcessTestRunner.CliResult repeated = await _runner.RunAsync(directory, paths.Home, workloads.Home, project, [command], cancellation.Token);
            CliProcessTestRunner.CliResult[] results = [first, repeated];

            foreach (CliProcessTestRunner.CliResult result in results)
            {
                result.ExitCode.Should().Be(0);
                result.StandardError.Should().BeEmpty();
                if (detailed)
                {
                    string[] lines = result.StandardOutput.Split(Environment.NewLine);
                    lines.Should().ContainSingle(line => line.Contains("Version", StringComparison.Ordinal))
                        .Which.Should().Contain(version).And.NotContain("+");
                    lines.Should().ContainSingle(line => line.Contains("Build", StringComparison.Ordinal))
                        .Which.Should().Contain(informationalVersion);
                }
                else
                {
                    result.StandardOutput.TrimEnd().Should().Be(version).And.NotContain("+");
                }
            }

            File.Exists(Path.Combine(paths.Home, FileFirstRunStateStore.MarkerFileName)).Should().BeFalse();
            File.Exists(workloads.WorkloadRegistryPath).Should().BeFalse();
            (await File.ReadAllTextAsync(paths.VersionCachePath, cancellation.Token)).Should().Be("0.0.0");
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
