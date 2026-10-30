// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Workloads.Storage;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.Tests.Commands;

public sealed class NewCommandProgramTests(ITestOutputHelper output)
{
    [Theory]
    [Trait("Category", "Integration")]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task Program_New_Unavailable_LeavesTargetUnchanged(bool exists, bool force, bool list)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"func-new-process-{Guid.NewGuid():N}");
        string project = Path.Combine(directory, "project");
        string configDirectory = Path.Combine(project, ".func");
        CliConfigurationPathsOptions paths = new(Path.Combine(directory, "home"));
        WorkloadPathsOptions workloads = new(Path.Combine(directory, "workloads"));
        CliProcessTestRunner runner = new(output);
        using CancellationTokenSource cancellation = new(CliProcessTestRunner.ExecutionTimeout);

        try
        {
            Directory.CreateDirectory(paths.Home);
            await File.WriteAllTextAsync(paths.VersionCachePath, "1.0.0", cancellation.Token);
            if (exists)
            {
                Directory.CreateDirectory(configDirectory);
                Directory.CreateDirectory(Path.Combine(project, "empty"));
                await File.WriteAllTextAsync(Path.Combine(configDirectory, "config.json"),
                    """{"stack":{"runtime":"node"},"custom":"keep"}""", cancellation.Token);
                await File.WriteAllTextAsync(Path.Combine(project, "host.json"), "{}", cancellation.Token);
                await File.WriteAllTextAsync(Path.Combine(project, "function.js"), "// keep", cancellation.Token);
            }

            string[] entriesBefore = exists ? Directory.GetFileSystemEntries(project, "*", SearchOption.AllDirectories) : [];
            Dictionary<string, byte[]> filesBefore = exists
                ? Directory.GetFiles(project, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes)
                : [];
            List<string> args = ["new", project, "--non-interactive"];
            if (force)
            {
                args.Add("--force");
            }

            if (list)
            {
                args.AddRange(["--list", "--format", "json"]);
            }
            else
            {
                args.AddRange(["--template", "HttpTrigger", "--name", "Example"]);
            }

            CliProcessTestRunner.CliResult result = await runner.RunAsync(
                directory, paths.Home, workloads.Home, directory, args, cancellation.Token);

            result.ExitCode.Should().Be(1, "stdout: {0}; stderr: {1}", result.StandardOutput, result.StandardError);
            result.StandardError.Should().Contain("temporarily unavailable").And.Contain("new templating engine");
            Directory.Exists(project).Should().Be(exists);
            if (exists)
            {
                Directory.GetFileSystemEntries(project, "*", SearchOption.AllDirectories).Should().BeEquivalentTo(entriesBefore);
                foreach ((string path, byte[] bytes) in filesBefore)
                {
                    (await File.ReadAllBytesAsync(path, cancellation.Token)).Should().Equal(bytes);
                }
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("new", "--help")]
    [InlineData("init", "--help")]
    [InlineData("workload", "list")]
    [InlineData("version")]
    public async Task Program_CommandTree_InitializesWithoutLegacyServices(params string[] args)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"func-command-startup-{Guid.NewGuid():N}");
        CliConfigurationPathsOptions paths = new(Path.Combine(directory, "home"));
        WorkloadPathsOptions workloads = new(Path.Combine(directory, "workloads"));
        CliProcessTestRunner runner = new(output);
        using CancellationTokenSource cancellation = new(CliProcessTestRunner.ExecutionTimeout);

        try
        {
            Directory.CreateDirectory(paths.Home);
            await File.WriteAllTextAsync(paths.VersionCachePath, "1.0.0", cancellation.Token);

            CliProcessTestRunner.CliResult result = await runner.RunAsync(
                directory, paths.Home, workloads.Home, directory, args, cancellation.Token);

            result.ExitCode.Should().Be(0, "stdout: {0}; stderr: {1}", result.StandardOutput, result.StandardError);
            result.StandardOutput.Should().NotBeNullOrWhiteSpace();
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
