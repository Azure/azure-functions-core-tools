// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Hosting.FirstRun;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Storage;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.Tests.Commands.Setup;

public sealed class SetupProgramAdvisoryTests(ITestOutputHelper output)
{
    private const string CachedVersion = "9999.0.0";
    private const string UnusedSource = "http://127.0.0.1:1/v3/index.json";

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public async Task Program_Setup_CachedUpdateAppearsOnlyInPlainInstallMode(bool json, bool check, bool fail)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"func-setup-advisory-{Guid.NewGuid():N}");
        CliConfigurationPathsOptions paths = new(Path.Combine(directory, "home"));
        WorkloadPathsOptions workloads = new(Path.Combine(directory, "workload-home"));
        string project = Path.Combine(directory, "project");
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(60));

        try
        {
            Directory.CreateDirectory(paths.Home);
            Directory.CreateDirectory(project);
            await File.WriteAllTextAsync(paths.VersionCachePath, CachedVersion, cancellation.Token);
            DateTime cacheTimestamp = File.GetLastWriteTimeUtc(paths.VersionCachePath);

            // If-needed host setup uses inventory without loading an assembly or consulting a feed.
            WorkloadStore store = new(workloads);
            await store.SaveWorkloadAsync(new WorkloadEntry
            {
                PackageId = SetupDependency.Host(null).PackageId,
                PackageVersion = "1.0.0",
                Kind = WorkloadKind.Content,
            }, cancellation.Token);
            byte[] registryBefore = await File.ReadAllBytesAsync(workloads.WorkloadRegistryPath, cancellation.Token);
            List<string> args =
            [
                "setup", fail ? "--features=.net" : "--features=host", "--install-policy=if-needed",
                "--non-interactive", "--prerelease=false", "--source", UnusedSource,
            ];
            if (check)
            {
                args.Add("--check");
            }

            if (json)
            {
                args.Add("--output=JSON");
            }

            // The rejected .net spelling fails inside setup, before any profile or package lookup.
            CliResult result = await RunCliAsync(directory, paths.Home, workloads.Home, project, args, cancellation.Token);

            output.WriteLine("stdout: {0}{1}stderr: {2}", result.StandardOutput, Environment.NewLine, result.StandardError);
            result.ExitCode.Should().Be(fail ? 1 : 0, "stdout: {0}{1}stderr: {2}",
                result.StandardOutput, Environment.NewLine, result.StandardError);
            if (json || check)
            {
                result.StandardOutput.Should().NotContain(CachedVersion).And.NotContain("A newer version");
                result.StandardError.Should().NotContain(CachedVersion).And.NotContain("A newer version");
            }
            else
            {
                // The same cached version must still produce an advisory for normal setup.
                result.StandardOutput.Should().Contain(CachedVersion).And.Contain("A newer version");
            }

            if (json)
            {
                result.StandardError.Should().BeEmpty();
                JsonElement[] events = ReadPhysicalEvents(result.StandardOutput);
                if (fail)
                {
                    events.Should().ContainSingle().Which.GetProperty("type").GetString().Should().Be("setup.failed");
                    events[0].GetProperty("message").GetString().Should().Contain("Use 'dotnet'");
                }
                else
                {
                    events[0].GetProperty("type").GetString().Should().Be("setup.started");
                    events[0].GetProperty("check").GetBoolean().Should().Be(check);
                    events.Should().ContainSingle(item => item.GetProperty("type").GetString() == "dependency.result")
                        .Which.GetProperty("status").GetString().Should().Be("satisfied");
                    events[^1].GetProperty("type").GetString().Should().Be("setup.completed");
                    events[^1].GetProperty("success").GetBoolean().Should().BeTrue();
                    events.Should().NotContain(item => item.GetProperty("type").GetString() == "setup.failed");
                }
            }
            else if (fail)
            {
                result.StandardError.Should().Contain("Use 'dotnet'");
                result.StandardOutput.Should().NotContain("setup is complete");
            }
            else
            {
                result.StandardError.Should().BeEmpty();
                result.StandardOutput.Should().Contain("host is already installed").And.Contain("setup is complete");
            }

            (await File.ReadAllTextAsync(paths.VersionCachePath, cancellation.Token)).Should().Be(CachedVersion);
            File.GetLastWriteTimeUtc(paths.VersionCachePath).Should().Be(cacheTimestamp);
            (await File.ReadAllBytesAsync(workloads.WorkloadRegistryPath, cancellation.Token)).Should().Equal(registryBefore);

            string marker = Path.Combine(paths.Home, FileFirstRunStateStore.MarkerFileName);
            File.Exists(marker).Should().Be(!check && !fail);
            if (check)
            {
                // Check must preserve existing state as well as avoid creating it.
                const string sentinel = "existing first-run completion";
                await File.WriteAllTextAsync(marker, sentinel, cancellation.Token);
                CliResult repeated = await RunCliAsync(directory, paths.Home, workloads.Home, project, args, cancellation.Token);

                repeated.ExitCode.Should().Be(result.ExitCode);
                repeated.StandardOutput.Should().NotContain(CachedVersion);
                repeated.StandardError.Should().NotContain(CachedVersion);
                if (json) ReadPhysicalEvents(repeated.StandardOutput).Should().NotBeEmpty();
                (await File.ReadAllTextAsync(marker, cancellation.Token)).Should().Be(sentinel);
                (await File.ReadAllBytesAsync(workloads.WorkloadRegistryPath, cancellation.Token)).Should().Equal(registryBefore);
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
    [InlineData(0, true, "--version", "setup", "--output")]
    [InlineData(0, true, "--version", "setup", "--check=false", "--output")]
    [InlineData(0, false, "--version", "setup", "--check=true", "--output")]
    [InlineData(0, true, "--help", "setup", "--output")]
    [InlineData(1, true, "setup", "--output")]
    [InlineData(0, false, "--version", "setup", "--output=json")]
    [InlineData(0, true, "--version", "setup", "--install-policy")]
    [InlineData(0, true, "--version")]
    [InlineData(0, true, "--version", "setup", "--output=json", "--output=plain")]
    [InlineData(0, true, "--version", "setup", "--check=true", "--check=false")]
    [InlineData(0, false, "--version", "setup", "--check=true", "--check=false", "--output=json")]
    [InlineData(0, true, "[suggest]", "setup", "--output")]
    public async Task Program_ParserAction_PreservesDispatchAndAdvisoryPolicy(int expectedExit, bool advisory, params string[] args)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"func-setup-parser-{Guid.NewGuid():N}");
        CliConfigurationPathsOptions paths = new(Path.Combine(directory, "home"));
        WorkloadPathsOptions workloads = new(Path.Combine(directory, "workload-home"));
        string project = Path.Combine(directory, "project");
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(60));

        try
        {
            Directory.CreateDirectory(paths.Home);
            Directory.CreateDirectory(project);
            await File.WriteAllTextAsync(paths.VersionCachePath, CachedVersion, cancellation.Token);

            CliResult result = await RunCliAsync(directory, paths.Home, workloads.Home, project, args, cancellation.Token);

            output.WriteLine("stdout: {0}{1}stderr: {2}", result.StandardOutput, Environment.NewLine, result.StandardError);
            result.ExitCode.Should().Be(expectedExit);
            result.StandardError.Should().NotContain("unexpected error");
            if (expectedExit == 0) result.StandardError.Should().BeEmpty();
            else result.StandardError.Should().Contain("Required argument missing for option:").And.Contain("--output");

            if (args.Contains("--version"))
            {
                Assembly cliAssembly = typeof(SetupCommand).Assembly;
                string expectedVersion = cliAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                    ?? cliAssembly.GetName().Version?.ToString() ?? string.Empty;
                using StringReader lines = new(result.StandardOutput);
                lines.ReadLine().Should().Be(expectedVersion);
            }
            else if (!args.Contains("[suggest]")) result.StandardOutput.Should().Contain("func setup").And.Contain("--output");

            result.StandardOutput.Contains("A newer version", StringComparison.Ordinal).Should().Be(advisory);
            result.StandardOutput.Contains(CachedVersion, StringComparison.Ordinal).Should().Be(advisory);
            File.Exists(Path.Combine(paths.Home, FileFirstRunStateStore.MarkerFileName)).Should().BeFalse();
            File.Exists(workloads.WorkloadRegistryPath).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<CliResult> RunCliAsync(
        string directory, string home, string workloadsHome, string project, IReadOnlyList<string> args, CancellationToken cancellationToken)
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

        // Preserve only host/runtime discovery. Do not inherit CLI options, credentials, hooks, or user caches.
        startInfo.Environment.Clear();
        string[] inheritedKeys =
        [
            "PATH", "SystemRoot", "WINDIR", "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432",
            "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT_X86", "DOTNET_ROOT_ARM64", "DOTNET_ROOT(x86)",
            "LD_LIBRARY_PATH", "DYLD_LIBRARY_PATH",
        ];
        foreach (string key in inheritedKeys)
        {
            if (Environment.GetEnvironmentVariable(key) is { } value)
            {
                startInfo.Environment[key] = value;
            }
        }

        string[] isolatedKeys =
        [
            "HOME", "USERPROFILE", "APPDATA", "LOCALAPPDATA", "DOTNET_CLI_HOME",
            "XDG_CONFIG_HOME", "XDG_CACHE_HOME", "XDG_DATA_HOME", "NUGET_PACKAGES",
            "NUGET_HTTP_CACHE_PATH", "NUGET_PLUGINS_CACHE_PATH", "TEMP", "TMP", "TMPDIR",
        ];
        foreach (string key in isolatedKeys)
        {
            string isolatedPath = Path.Combine(directory, key.ToLowerInvariant());
            Directory.CreateDirectory(isolatedPath);
            startInfo.Environment[key] = isolatedPath;
        }

        startInfo.Environment[Abstractions.Common.Constants.FuncHomeEnvironmentVariable] = home;
        startInfo.Environment[Constants.WorkloadsHomeEnvironmentVariable] = workloadsHome;
        startInfo.Environment[Constants.WorkloadsSourceEnvironmentVariable] = UnusedSource;
        startInfo.Environment[Constants.ProfilesCdnBaseUrlEnvironmentVariable] = "http://127.0.0.1:1/";
        startInfo.Environment[Constants.TelemetryOptOutEnvVar] = "1";
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        startInfo.Environment["NO_COLOR"] = "1";

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the CLI test process.");
        process.StandardInput.Close();
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(stdout, stderr).WaitAsync(cancellationToken);
            return new CliResult(process.ExitCode, await stdout, await stderr);
        }
        finally
        {
            using CancellationTokenSource cleanup = new(TimeSpan.FromSeconds(10));
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) when (process.HasExited)
                {
                    // The owned process exited between the check and kill.
                }

                await process.WaitForExitAsync(cleanup.Token);
            }

            await Task.WhenAll(stdout, stderr).WaitAsync(cleanup.Token);
        }
    }

    private static JsonElement[] ReadPhysicalEvents(string stdout)
    {
        List<JsonElement> events = [];
        using StringReader reader = new(stdout);
        while (reader.ReadLine() is { } line)
        {
            // Blank lines and trailing human advisories are failures too.
            using var document = JsonDocument.Parse(line);
            document.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
            document.RootElement.GetProperty("type").GetString().Should().NotBeNullOrEmpty();
            events.Add(document.RootElement.Clone());
        }

        events.Should().NotBeEmpty();
        return [.. events];
    }

    private sealed record CliResult(int ExitCode, string StandardOutput, string StandardError);
}