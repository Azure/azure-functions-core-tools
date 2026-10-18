// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Storage;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.Tests.Commands.Workload;

public sealed class WorkloadProgramSmokeTests(ITestOutputHelper output)
{
    [Theory]
    [Trait("Category", "Integration")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WorkloadListJson_EmptyIsolatedHome_EmitsEmptyArray(bool explicitTarget)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"func-workload-smoke-{Guid.NewGuid():N}");
        CliConfigurationPathsOptions paths = new(Path.Combine(directory, "home"));
        WorkloadPathsOptions workloads = new(Path.Combine(directory, "workload-home"));
        string project = Path.Combine(directory, "project");
        string source = Path.Combine(project, "source.txt");
        const string SourceContent = "Project source must remain unchanged.";
        using CancellationTokenSource cancellation = new(CliProcessTestRunner.ExecutionTimeout);
        RecordingOutput diagnostics = new(output);
        CliProcessTestRunner runner = new(diagnostics);
        List<(string Path, byte[] Bytes, DateTime Timestamp)> targetSnapshots = [];
        string? artifactDirectory = null;
        string[]? artifactInventory = null;

        try
        {
            Directory.CreateDirectory(paths.Home);
            Directory.CreateDirectory(workloads.Home);
            Directory.CreateDirectory(project);
            Directory.EnumerateFileSystemEntries(workloads.Home).Should().BeEmpty();
            await File.WriteAllTextAsync(source, SourceContent, cancellation.Token);
            DateTime sourceTimestamp = File.GetLastWriteTimeUtc(source);

            // A fresh, non-newer cache prevents the global advisory without suppressing product behavior.
            string cachedVersion = AssemblyCliVersionProvider.Instance.Version.Split('-')[0];
            Version.TryParse(cachedVersion, out _).Should().BeTrue();
            await File.WriteAllTextAsync(paths.VersionCachePath, cachedVersion, cancellation.Token);
            DateTime cacheTimestamp = File.GetLastWriteTimeUtc(paths.VersionCachePath);

            CliProcessTestRunner.CliResult result;
            if (explicitTarget)
            {
                string productAssembly = typeof(CliProcessTestRunner).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                    .Single(attribute => attribute.Key == "CliProductAssemblyPath").Value
                    ?? throw new InvalidOperationException("The test build did not record the CLI product output.");
                string executable = Path.ChangeExtension(productAssembly, OperatingSystem.IsWindows() ? ".exe" : null);
                artifactDirectory = Path.GetDirectoryName(executable)
                    ?? throw new InvalidOperationException("The CLI product output has no containing directory.");
                artifactInventory = Directory.GetFileSystemEntries(artifactDirectory, "*", SearchOption.AllDirectories);
                string[] targetFiles =
                [
                    executable, productAssembly, Path.ChangeExtension(productAssembly, ".runtimeconfig.json"),
                    Path.ChangeExtension(productAssembly, ".deps.json"),
                ];
                foreach (string file in targetFiles)
                {
                    targetSnapshots.Add((file, await File.ReadAllBytesAsync(file, cancellation.Token), File.GetLastWriteTimeUtc(file)));
                }

                result = await runner.RunExecutableAsync(
                    executable, directory, paths.Home, workloads.Home, project, ["workload", "list", "--json"], cancellation.Token);

                diagnostics.Lines.Should().Contain($"CLI explicit executable: {executable}");
                diagnostics.Lines.Should().Contain($"CLI executable SHA-256: {Convert.ToHexString(SHA256.HashData(targetSnapshots[0].Bytes))}");
                diagnostics.Lines.Should().Contain(
                    $"Command: func workload list --json; host: {executable}; working directory: {project}");
                diagnostics.Lines.Should().ContainSingle(line => line.StartsWith("CLI artifact directory: ", StringComparison.Ordinal))
                    .Which.Should().Contain(artifactDirectory).And.MatchRegex("SHA-256: [0-9A-F]{64}$");
                diagnostics.Lines.Should().NotContain(line => line.StartsWith("CLI assembly: ", StringComparison.Ordinal)
                    || line.StartsWith("CLI version/source: ", StringComparison.Ordinal));
            }
            else
            {
                result = await runner.RunAsync(
                    directory, paths.Home, workloads.Home, project, ["workload", "list", "--json"], cancellation.Token);
            }

            result.ExitCode.Should().Be(0);
            result.StandardError.Should().BeEmpty();
            result.Duration.Should().BeLessThan(CliProcessTestRunner.ExecutionTimeout);
            using var document = JsonDocument.Parse(result.StandardOutput);
            document.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
            document.RootElement.GetArrayLength().Should().Be(0);

            Directory.EnumerateFileSystemEntries(workloads.Home, "*", SearchOption.AllDirectories).Should().BeEmpty();
            Directory.EnumerateFileSystemEntries(paths.Home, "*", SearchOption.AllDirectories)
                .Should().ContainSingle().Which.Should().Be(paths.VersionCachePath);
            (await File.ReadAllTextAsync(paths.VersionCachePath, cancellation.Token)).Should().Be(cachedVersion);
            File.GetLastWriteTimeUtc(paths.VersionCachePath).Should().Be(cacheTimestamp);
            Directory.EnumerateFileSystemEntries(project, "*", SearchOption.AllDirectories)
                .Should().ContainSingle().Which.Should().Be(source);
            (await File.ReadAllTextAsync(source, cancellation.Token)).Should().Be(SourceContent);
            File.GetLastWriteTimeUtc(source).Should().Be(sourceTimestamp);

            string[] permittedPaths = [paths.Home, workloads.Home, project, Path.Combine(directory, CliProcessTestRunner.StateDirectoryName)];
            Directory.EnumerateFileSystemEntries(directory).Should().BeEquivalentTo(permittedPaths);
            foreach ((string file, byte[] bytes, DateTime timestamp) in targetSnapshots)
            {
                (await File.ReadAllBytesAsync(file, cancellation.Token)).Should().Equal(bytes, "execution must not rewrite {0}", file);
                File.GetLastWriteTimeUtc(file).Should().Be(timestamp, "execution must not touch {0}", file);
            }

            if (artifactDirectory is not null && artifactInventory is not null)
            {
                Directory.GetFileSystemEntries(artifactDirectory, "*", SearchOption.AllDirectories).Should().BeEquivalentTo(artifactInventory);
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunExecutableAsync_InvalidTarget_FailsBeforeCreatingProcessState(bool absolutePath)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"func-invalid-target-{Guid.NewGuid():N}");
        string executable = absolutePath ? Path.Combine(directory, "missing-func.exe") : "missing-func.exe";
        CliProcessTestRunner runner = new(output);

        try
        {
            Func<Task> act = () => runner.RunExecutableAsync(
                executable, directory, Path.Combine(directory, "home"), Path.Combine(directory, "workload-home"),
                Path.Combine(directory, "project"), ["workload", "list", "--json"], CancellationToken.None);

            if (absolutePath)
            {
                FileNotFoundException failure = (await act.Should().ThrowExactlyAsync<FileNotFoundException>()).Which;
                failure.FileName.Should().Be(executable);
                failure.Message.Should().Contain(executable);
            }
            else
            {
                (await act.Should().ThrowExactlyAsync<ArgumentException>()).Which.ParamName.Should().Be("executablePath");
            }

            Directory.Exists(directory).Should().BeFalse("invalid explicit targets must not fall back to the default CLI");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task WorkloadListJson_KnownInstalledContentWorkload_EmitsExpectedInventory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"func-workload-smoke-{Guid.NewGuid():N}");
        CliConfigurationPathsOptions paths = new(Path.Combine(directory, "home"));
        WorkloadPathsOptions workloads = new(Path.Combine(directory, "workload-home"));
        string project = Path.Combine(directory, "project");
        string source = Path.Combine(project, "source.txt");
        const string SourceContent = "Project source must remain unchanged.";
        const string PayloadContent = "Q1 content payload must remain unchanged.";
        WorkloadEntry workload = new()
        {
            PackageId = "Test.Content.Q1",
            PackageVersion = "1.2.3",
            Kind = WorkloadKind.Content,
            Aliases = ["q1-content"],
            DisplayName = "Q1 inventory fixture",
            Description = "Deterministic content-only workload inventory.",
        };
        using CancellationTokenSource cancellation = new(CliProcessTestRunner.ExecutionTimeout);
        CliProcessTestRunner runner = new(output);

        try
        {
            Directory.CreateDirectory(paths.Home);
            Directory.CreateDirectory(workloads.Home);
            Directory.CreateDirectory(project);
            Directory.EnumerateFileSystemEntries(workloads.Home).Should().BeEmpty();
            await File.WriteAllTextAsync(source, SourceContent, cancellation.Token);

            string cachedVersion = AssemblyCliVersionProvider.Instance.Version.Split('-')[0];
            Version.TryParse(cachedVersion, out _).Should().BeTrue();
            await File.WriteAllTextAsync(paths.VersionCachePath, cachedVersion, cancellation.Token);

            string installDirectory = workloads.GetInstallDirectory(workload.PackageId, workload.PackageVersion);
            string contentRoot = WorkloadPackageLayout.GetContentRoot(installDirectory, workload.RuntimeIdentifier);
            string manifest = Path.Combine(installDirectory, WorkloadMetadataReader.MetadataFileName);
            string payload = Path.Combine(contentRoot, "payload.txt");
            Directory.CreateDirectory(contentRoot);
            WorkloadMetadata metadata = new()
            {
                Schema = WorkloadManifestSchema.CurrentPackageManifestSchema,
                Kind = workload.Kind,
                DisplayName = workload.DisplayName,
                Description = workload.Description,
            };
            await File.WriteAllTextAsync(
                manifest, JsonSerializer.Serialize(metadata, WorkloadJsonContext.Default.WorkloadMetadata), cancellation.Token);
            await File.WriteAllTextAsync(payload, PayloadContent, cancellation.Token);
            WorkloadStore store = new(workloads);
            await store.SaveWorkloadAsync(workload, cancellation.Token);

            string[] preservedFiles = [workloads.WorkloadRegistryPath, manifest, payload, paths.VersionCachePath, source];
            List<(string Path, byte[] Bytes, DateTime Timestamp)> snapshots = [];
            foreach (string file in preservedFiles)
            {
                snapshots.Add((file, await File.ReadAllBytesAsync(file, cancellation.Token), File.GetLastWriteTimeUtc(file)));
            }

            string[] inventory = Directory.GetFileSystemEntries(workloads.Home, "*", SearchOption.AllDirectories);

            CliProcessTestRunner.CliResult result = await runner.RunAsync(
                directory, paths.Home, workloads.Home, project, ["workload", "list", "--json"], cancellation.Token);

            result.ExitCode.Should().Be(0);
            result.StandardError.Should().BeEmpty();
            result.Duration.Should().BeLessThan(CliProcessTestRunner.ExecutionTimeout);
            using var document = JsonDocument.Parse(result.StandardOutput);
            document.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
            document.RootElement.GetArrayLength().Should().Be(1);
            JsonElement row = document.RootElement[0];
            row.GetProperty("packageId").GetString().Should().Be(workload.PackageId);
            row.GetProperty("packageVersion").GetString().Should().Be(workload.PackageVersion);
            row.GetProperty("aliases").EnumerateArray().Select(alias => alias.GetString()).Should().Equal(workload.Aliases);
            row.GetProperty("displayName").GetString().Should().Be(workload.DisplayName);
            row.GetProperty("description").GetString().Should().Be(workload.Description);
            row.GetProperty("physicalPackageId").GetString().Should().Be(workload.PackageId);
            row.GetProperty("physicalPackageVersion").GetString().Should().Be(workload.PackageVersion);
            row.GetProperty("isExplicitlyInstalled").GetBoolean().Should().BeTrue();
            row.GetProperty("installRefCount").GetInt32().Should().Be(1);
            row.GetProperty("ownership").GetString().Should().Be("explicit");
            row.TryGetProperty("loaded", out _).Should().BeFalse();

            foreach ((string file, byte[] bytes, DateTime timestamp) in snapshots)
            {
                (await File.ReadAllBytesAsync(file, cancellation.Token)).Should().Equal(bytes, "listing must not rewrite {0}", file);
                File.GetLastWriteTimeUtc(file).Should().Be(timestamp, "listing must not touch {0}", file);
            }

            Directory.EnumerateFileSystemEntries(workloads.Home, "*", SearchOption.AllDirectories).Should().BeEquivalentTo(inventory);
            Directory.EnumerateFileSystemEntries(paths.Home, "*", SearchOption.AllDirectories)
                .Should().ContainSingle().Which.Should().Be(paths.VersionCachePath);
            Directory.EnumerateFileSystemEntries(project, "*", SearchOption.AllDirectories)
                .Should().ContainSingle().Which.Should().Be(source);
            string[] permittedPaths = [paths.Home, workloads.Home, project, Path.Combine(directory, CliProcessTestRunner.StateDirectoryName)];
            Directory.EnumerateFileSystemEntries(directory).Should().BeEquivalentTo(permittedPaths);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private sealed class RecordingOutput(ITestOutputHelper output) : ITestOutputHelper
    {
        private readonly ITestOutputHelper _output = output ?? throw new ArgumentNullException(nameof(output));

        internal List<string> Lines { get; } = [];

        public void WriteLine(string message)
        {
            Lines.Add(message);
            _output.WriteLine(message);
        }

        public void WriteLine(string format, params object[] args)
            => WriteLine(string.Format(CultureInfo.InvariantCulture, format, args));
    }
}
