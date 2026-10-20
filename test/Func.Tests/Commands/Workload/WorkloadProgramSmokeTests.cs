// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
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
    private const string Preview3ArchiveSetting = "FUNC_CLI_TEST_PREVIEW3_ARCHIVE";
    private const string Preview3ArchiveSha256 = "06fdd97d0e17fff6b7da1e2d2701a01e9fac21f63c6c007cfb79004ec3b5a73b";

    public static TheoryData<bool, string?> EmptyInventoryTargets
    {
        get
        {
            TheoryData<bool, string?> targets = new()
            {
                { false, null },
                { true, null },
            };
            if (Environment.GetEnvironmentVariable(Preview3ArchiveSetting) is { } archivePath)
            {
                targets.Add(true, archivePath);
            }

            return targets;
        }
    }

    [Theory]
    [Trait("Category", "Integration")]
    [MemberData(nameof(EmptyInventoryTargets), DisableDiscoveryEnumeration = true)]
    public async Task WorkloadListJson_EmptyIsolatedHome_EmitsEmptyArray(bool explicitTarget, string? preview3Archive)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"func-workload-smoke-{Guid.NewGuid():N}");
        CliConfigurationPathsOptions paths = new(Path.Combine(directory, "home"));
        WorkloadPathsOptions workloads = new(Path.Combine(directory, "workload-home"));
        string project = Path.Combine(directory, "project");
        string source = Path.Combine(project, "source.txt");
        const string SourceContent = "Project source must remain unchanged.";
        RecordingOutput diagnostics = new(output);
        CliProcessTestRunner runner = new(diagnostics);
        List<(string Path, byte[] Bytes, DateTime Timestamp)> targetSnapshots = [];
        string? artifactDirectory = null;
        string[]? artifactInventory = null;

        diagnostics.WriteLine("CLI target role: {0}", preview3Archive is not null
            ? "Preview 3 baseline"
            : explicitTarget ? "Current product candidate" : "Default test context (control)");

        try
        {
            string? preview3Executable = null;
            if (preview3Archive is not null)
            {
                using CancellationTokenSource intake = new(CliProcessTestRunner.ExecutionTimeout);
                preview3Executable = await PreparePreview3Async(preview3Archive, directory, diagnostics, intake.Token);
            }

            using CancellationTokenSource cancellation = new(CliProcessTestRunner.ExecutionTimeout);
            Directory.CreateDirectory(paths.Home);
            Directory.CreateDirectory(workloads.Home);
            Directory.CreateDirectory(project);
            Directory.EnumerateFileSystemEntries(workloads.Home).Should().BeEmpty();
            await File.WriteAllTextAsync(source, SourceContent, cancellation.Token);
            DateTime sourceTimestamp = File.GetLastWriteTimeUtc(source);

            // Keep the cache at Preview 3's numeric version so newer candidates cannot trigger its update advisory.
            string cachedVersion = "5.0.0";
            Version.TryParse(cachedVersion, out _).Should().BeTrue();
            await File.WriteAllTextAsync(paths.VersionCachePath, cachedVersion, cancellation.Token);
            DateTime cacheTimestamp = File.GetLastWriteTimeUtc(paths.VersionCachePath);

            CliProcessTestRunner.CliResult result;
            if (explicitTarget)
            {
                string executable;
                string[] targetFiles;
                if (preview3Executable is not null)
                {
                    executable = preview3Executable;
                    targetFiles = [executable];
                }
                else
                {
                    string productAssembly = typeof(CliProcessTestRunner).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                        .Single(attribute => attribute.Key == "CliProductAssemblyPath").Value
                        ?? throw new InvalidOperationException("The test build did not record the CLI product output.");
                    executable = Path.ChangeExtension(productAssembly, OperatingSystem.IsWindows() ? ".exe" : null);
                    targetFiles =
                    [
                        executable, productAssembly, Path.ChangeExtension(productAssembly, ".runtimeconfig.json"),
                        Path.ChangeExtension(productAssembly, ".deps.json"),
                    ];
                }

                artifactDirectory = Path.GetDirectoryName(executable)
                    ?? throw new InvalidOperationException("The CLI product output has no containing directory.");
                artifactInventory = Directory.GetFileSystemEntries(artifactDirectory, "*", SearchOption.AllDirectories);
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
                diagnostics.Lines.Should().Contain(
                    $"CLI bundle extraction root: {Path.Combine(directory, CliProcessTestRunner.StateDirectoryName, "dotnet-bundle")}");
                diagnostics.Lines.Should().ContainSingle(line => line.StartsWith("CLI artifact directory: ", StringComparison.Ordinal))
                    .Which.Should().Contain(artifactDirectory).And.MatchRegex("SHA-256: [0-9A-F]{64}$");
                diagnostics.Lines.Should().NotContain(line => line.StartsWith("CLI assembly: ", StringComparison.Ordinal)
                    || line.StartsWith("CLI version/source: ", StringComparison.Ordinal));
            }
            else
            {
                result = await runner.RunAsync(
                    directory, paths.Home, workloads.Home, project, ["workload", "list", "--json"], cancellation.Token);
                diagnostics.Lines.Should().NotContain(line => line.StartsWith("CLI bundle extraction root: ", StringComparison.Ordinal));
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

    [Theory]
    [InlineData("relative")]
    [InlineData("missing")]
    [InlineData("digest")]
    [InlineData("whitespace")]
    public async Task PreparePreview3Async_InvalidArchive_FailsBeforeCreatingProcessState(string invalidInput)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"func-preview3-intake-{Guid.NewGuid():N}");
        string executionDirectory = Path.Combine(directory, "execution");
        string archivePath = invalidInput switch
        {
            "relative" => "func-win-x64.zip",
            "whitespace" => " ",
            _ => Path.Combine(directory, "func-win-x64.zip"),
        };
        RecordingOutput diagnostics = new(output);

        try
        {
            Directory.CreateDirectory(directory);
            if (invalidInput == "digest")
            {
                await File.WriteAllTextAsync(archivePath, "Unqualified archive input.", CancellationToken.None);
            }

            Func<Task> act = () => PreparePreview3Async(archivePath, executionDirectory, diagnostics, CancellationToken.None);
            switch (invalidInput)
            {
                case "relative":
                case "whitespace":
                    (await act.Should().ThrowExactlyAsync<ArgumentException>()).Which.ParamName.Should().Be("archivePath");
                    break;
                case "missing":
                    (await act.Should().ThrowExactlyAsync<FileNotFoundException>()).Which.FileName.Should().Be(archivePath);
                    break;
                case "digest":
                    (await act.Should().ThrowExactlyAsync<InvalidDataException>()).Which.Message.Should().Contain("SHA-256 mismatch");
                    diagnostics.Lines.Should().NotContain(line => line.StartsWith("Preview 3 verified archive SHA-256: ", StringComparison.Ordinal));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(invalidInput));
            }

            Directory.Exists(executionDirectory).Should().BeFalse("invalid archives must not create execution state or select another CLI");
            diagnostics.Lines.Should().NotContain(line => line.StartsWith("Command: ", StringComparison.Ordinal)
                || line.StartsWith("CLI explicit executable: ", StringComparison.Ordinal)
                || line.StartsWith("CLI assembly: ", StringComparison.Ordinal));
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
    [InlineData("func.exe", null, 0)]
    [InlineData(null, null, 0)]
    [InlineData("func.dll", null, 0)]
    [InlineData("../escaped.exe", null, 0)]
    [InlineData("/func.exe", null, 0)]
    [InlineData(@"C:\func.exe", null, 0)]
    [InlineData("nested/func.exe", null, 0)]
    [InlineData("func.exe", "func.exe", 0)]
    [InlineData("func.exe", "unexpected.dll", 0)]
    [InlineData("func.exe", null, 0xA000 << 16)]
    [InlineData("func.exe", null, (int)FileAttributes.ReparsePoint)]
    [InlineData("func.exe", null, (int)FileAttributes.Directory)]
    public async Task ExtractPreview3ExecutableAsync_ArchiveLayout_IsEnforced(string? entryName, string? additionalEntry, int attributes)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"func-preview3-layout-{Guid.NewGuid():N}");
        string extractionDirectory = Path.Combine(directory, "extracted");
        byte[] payload = "Inert apphost fixture; never execute."u8.ToArray();
        using MemoryStream contents = new();
        using (ZipArchive archive = new(contents, ZipArchiveMode.Create, leaveOpen: true))
        {
            string?[] names = [entryName, additionalEntry];
            foreach (string? name in names)
            {
                if (name is not null)
                {
                    ZipArchiveEntry entry = archive.CreateEntry(name);
                    entry.ExternalAttributes = attributes;
                    await using Stream content = entry.Open();
                    await content.WriteAsync(payload, CancellationToken.None);
                }
            }
        }

        contents.Position = 0;
        try
        {
            if (entryName == "func.exe" && additionalEntry is null && attributes == 0)
            {
                string executable = await ExtractPreview3ExecutableAsync(contents, extractionDirectory, CancellationToken.None);
                executable.Should().Be(Path.Combine(extractionDirectory, "func.exe"));
                (await File.ReadAllBytesAsync(executable, CancellationToken.None)).Should().Equal(payload);
                Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories)
                    .Should().BeEquivalentTo([extractionDirectory, executable]);
            }
            else
            {
                Func<Task> act = () => ExtractPreview3ExecutableAsync(contents, extractionDirectory, CancellationToken.None);
                await act.Should().ThrowExactlyAsync<InvalidDataException>();
                Directory.Exists(directory).Should().BeFalse("archive layout must be validated before writing any entry");
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

    [Fact]
    public async Task ExtractPreview3ExecutableAsync_InvalidZip_FailsBeforeExtraction()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"func-preview3-invalid-zip-{Guid.NewGuid():N}");
        using MemoryStream contents = new("Not a ZIP archive."u8.ToArray());

        try
        {
            Func<Task> act = () => ExtractPreview3ExecutableAsync(contents, directory, CancellationToken.None);
            await act.Should().ThrowExactlyAsync<InvalidDataException>();
            Directory.Exists(directory).Should().BeFalse();
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

    private async Task<string> PreparePreview3Async(
        string archivePath, string directory, ITestOutputHelper diagnostics, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        if (!Path.IsPathFullyQualified(archivePath))
        {
            throw new ArgumentException($"{Preview3ArchiveSetting} must name an absolute local archive path.", nameof(archivePath));
        }

        archivePath = Path.GetFullPath(archivePath);
        diagnostics.WriteLine("Preview 3 baseline release: 5.0.0-preview.3; tag: v5.0.0-preview.3; RID: win-x64");
        diagnostics.WriteLine("Preview 3 supplied archive: {0}", archivePath);
        diagnostics.WriteLine("Preview 3 expected archive SHA-256: {0}", Preview3ArchiveSha256);
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException($"The requested Preview 3 archive '{archivePath}' does not exist.", archivePath);
        }

        await using FileStream archive = new(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        string hash = Convert.ToHexString(await SHA256.HashDataAsync(archive, cancellationToken));
        diagnostics.WriteLine("Preview 3 supplied archive SHA-256: {0}", hash);
        if (!string.Equals(hash, Preview3ArchiveSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Preview 3 archive SHA-256 mismatch: expected {Preview3ArchiveSha256}; actual {hash}.");
        }

        diagnostics.WriteLine("Preview 3 verified archive SHA-256: {0}", hash);
        if (!OperatingSystem.IsWindows()
            || RuntimeInformation.OSArchitecture != Architecture.X64
            || RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            throw new PlatformNotSupportedException("Preview 3 paired replay requires Windows x64 and an x64 test process.");
        }

        archive.Position = 0;
        return await ExtractPreview3ExecutableAsync(
            archive, Path.Combine(directory, CliProcessTestRunner.StateDirectoryName, "preview3"), cancellationToken);
    }

    private async Task<string> ExtractPreview3ExecutableAsync(Stream archiveStream, string extractionDirectory, CancellationToken cancellationToken)
    {
        using ZipArchive archive = new(archiveStream, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count != 1 || archive.Entries[0].FullName != "func.exe")
        {
            throw new InvalidDataException("The Preview 3 win-x64 ZIP must contain only a root-level func.exe.");
        }

        ZipArchiveEntry entry = archive.Entries[0];
        int unixFileType = (entry.ExternalAttributes >> 16) & 0xF000;
        if (entry.Length == 0
            || (unixFileType != 0 && unixFileType != 0x8000)
            || ((FileAttributes)entry.ExternalAttributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
        {
            throw new InvalidDataException("The Preview 3 apphost must be a nonempty regular file, not a link or directory.");
        }

        extractionDirectory = Path.GetFullPath(extractionDirectory);
        if (Path.Exists(extractionDirectory))
        {
            throw new IOException($"Preview 3 extraction storage must be fresh: '{extractionDirectory}'.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(extractionDirectory);
        string executable = Path.Combine(extractionDirectory, "func.exe");
        await using FileStream extracted = new(executable, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await using Stream content = entry.Open();
        await content.CopyToAsync(extracted, cancellationToken);
        return executable;
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
