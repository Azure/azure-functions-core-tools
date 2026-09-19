// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using System.Text.Json;
using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Storage;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.Tests.Commands.Setup;

public sealed class SetupProgramAdvisoryTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Program_CheckJsonWithCachedUpdate_WritesOnlyNdjson()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"func-setup-advisory-{Guid.NewGuid():N}");
        var paths = new CliConfigurationPathsOptions(Path.Combine(directory, "home"));
        var workloads = new WorkloadPathsOptions(paths.Home);
        string project = Path.Combine(directory, "project");
        const string source = "https://unreachable.invalid/v3/index.json";
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(60));

        try
        {
            Directory.CreateDirectory(paths.Home);
            Directory.CreateDirectory(project);
            await File.WriteAllTextAsync(paths.VersionCachePath, "9999.0.0", cancellation.Token);

            // Satisfy inventory-based host detection without loading a workload assembly.
            var store = new WorkloadStore(workloads);
            await store.SaveWorkloadAsync(new WorkloadEntry
            {
                PackageId = SetupDependency.Host(null).PackageId,
                PackageVersion = "1.0.0",
                Kind = WorkloadKind.Content,
            }, cancellation.Token);
            string cacheBefore = await File.ReadAllTextAsync(paths.VersionCachePath, cancellation.Token);
            DateTime cacheTimestamp = File.GetLastWriteTimeUtc(paths.VersionCachePath);

            // A control invocation proves the seeded cache actually produces an advisory.
            var control = await RunCliAsync(paths.Home, project, ["--help"], cancellation.Token);
            control.ExitCode.Should().Be(0, "stdout: {0}{1}stderr: {2}", control.StandardOutput, Environment.NewLine, control.StandardError);
            control.StandardOutput.Should().Contain("9999.0.0");

            var result = await RunCliAsync(paths.Home, project,
                ["setup", "--check", "--features=host", "--install-policy=if-needed", "--output=JSON",
                    "--non-interactive", "--prerelease=false", "--source", source], cancellation.Token);
            output.WriteLine("stdout: {0}{1}stderr: {2}", result.StandardOutput, Environment.NewLine, result.StandardError);

            result.ExitCode.Should().Be(0, "stdout: {0}{1}stderr: {2}", result.StandardOutput, Environment.NewLine, result.StandardError);
            result.StandardError.Should().BeEmpty();
            List<JsonElement> events = [];
            using StringReader reader = new(result.StandardOutput);
            while (reader.ReadLine() is { } line)
            {
                // Do not discard blank or non-JSON lines, including trailing advisories.
                using var document = JsonDocument.Parse(line);
                document.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
                document.RootElement.GetProperty("type").GetString().Should().NotBeNullOrEmpty();
                events.Add(document.RootElement.Clone());
            }

            events.Should().NotBeEmpty();
            events[0].GetProperty("type").GetString().Should().Be("setup.started");
            events[0].GetProperty("check").GetBoolean().Should().BeTrue();
            events.Should().ContainSingle(item => item.GetProperty("type").GetString() == "dependency.result")
                .Which.GetProperty("status").GetString().Should().Be("satisfied");
            events[^1].GetProperty("type").GetString().Should().Be("setup.completed");
            events[^1].GetProperty("success").GetBoolean().Should().BeTrue();
            (await File.ReadAllTextAsync(paths.VersionCachePath, cancellation.Token)).Should().Be(cacheBefore);
            File.GetLastWriteTimeUtc(paths.VersionCachePath).Should().Be(cacheTimestamp);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static async Task<CliResult> RunCliAsync(string home, string project, string[] args, CancellationToken cancellationToken)
    {
        string testAssembly = typeof(SetupProgramAdvisoryTests).Assembly.Location;
        ProcessStartInfo startInfo = new()
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            WorkingDirectory = project,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("exec");
        startInfo.ArgumentList.Add("--runtimeconfig");
        startInfo.ArgumentList.Add(Path.ChangeExtension(testAssembly, ".runtimeconfig.json"));
        startInfo.ArgumentList.Add("--depsfile");
        startInfo.ArgumentList.Add(Path.ChangeExtension(testAssembly, ".deps.json"));
        startInfo.ArgumentList.Add(typeof(SetupCommand).Assembly.Location);
        foreach (string arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        foreach (string key in startInfo.Environment.Keys
            .Where(key => key.StartsWith(Constants.EnvironmentVariablePrefix, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            startInfo.Environment.Remove(key);
        }

        startInfo.Environment[Abstractions.Common.Constants.FuncHomeEnvironmentVariable] = home;
        startInfo.Environment[Constants.WorkloadsHomeEnvironmentVariable] = home;
        startInfo.Environment[Constants.TelemetryOptOutEnvVar] = "1";
        startInfo.Environment["NO_COLOR"] = "1";

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the CLI test process.");
        process.StandardInput.Close();
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            return new CliResult(process.ExitCode, await stdout, await stderr);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    private sealed record CliResult(int ExitCode, string StandardOutput, string StandardError);
}