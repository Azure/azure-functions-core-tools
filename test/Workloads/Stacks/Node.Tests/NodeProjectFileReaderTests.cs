// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net.Sockets;
using System.Runtime.InteropServices;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads.Node.Tests;

public partial class NodeProjectFileReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "func-node-evidence-" + Guid.NewGuid().ToString("N"));
    private readonly NodeProjectFileReader _reader = new();

    public NodeProjectFileReaderTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task ReadAsync_RegularFile_ReturnsExactBytesWithoutWriting()
    {
        byte[] bytes = [0, 1, 255, 13, 10];
        string path = Path.Combine(_root, "package.json");
        await File.WriteAllBytesAsync(path, bytes);
        DateTime written = File.GetLastWriteTimeUtc(path);

        (NodeProjectFileKind Kind, byte[] Bytes) observed = await _reader.ReadAsync(_root, "package.json", CancellationToken.None);

        observed.Kind.Should().Be(NodeProjectFileKind.RegularFile);
        observed.Bytes.Should().Equal(bytes);
        File.GetLastWriteTimeUtc(path).Should().Be(written);
        Directory.GetFileSystemEntries(_root).Should().Equal(path);
    }

    [Theory]
    [InlineData("package.json")]
    [InlineData("src/package.json")]
    [InlineData("src/functions/package.json")]
    public async Task ReadAsync_MissingLeafOrParent_IsEstablishedAbsent(string path)
    {
        (NodeProjectFileKind Kind, byte[] Bytes) observation = await _reader.ReadAsync(_root, path, CancellationToken.None);

        observation.Kind.Should().Be(NodeProjectFileKind.Absent);
        observation.Bytes.Should().BeEmpty();
        Directory.GetFileSystemEntries(_root).Should().BeEmpty();
    }

    [Theory]
    [InlineData("package.json")]
    [InlineData("src/package.json")]
    [InlineData("src/functions/package.json")]
    public async Task ReadAsync_LeafDirectory_IsNotAbsence(string logicalPath)
    {
        Directory.CreateDirectory(NativePath(logicalPath));

        (NodeProjectFileKind Kind, byte[] Bytes) observation = await _reader.ReadAsync(_root, logicalPath, CancellationToken.None);

        observation.Kind.Should().Be(NodeProjectFileKind.Directory);
    }

    [Theory]
    [InlineData("src", "src/package.json")]
    [InlineData("src/functions", "src/functions/package.json")]
    public async Task ReadAsync_EnclosingFile_PreventsAnAbsenceClaim(string enclosingPath, string logicalPath)
    {
        Write(enclosingPath, "{}");

        (NodeProjectFileKind Kind, byte[] Bytes) observation = await _reader.ReadAsync(_root, logicalPath, CancellationToken.None);

        observation.Kind.Should().Be(NodeProjectFileKind.Unsupported);
    }

    [Theory]
    [InlineData("src/package.json")]
    [InlineData("src/functions/package.json")]
    public async Task Capture_NestedBoundaryChanges_AffectOnlyNewProjects(string logicalPath)
    {
        Write("package.json", """{"main":"src/functions/*.js","dependencies":{"@azure/functions":"^4.0.0"}}""");
        Write("host.json", """{"version":"2.0"}""");
        var capabilities = new NodeProjectCapabilityReader(_reader);
        var absent = new NodeFunctionsProject(WorkingDirectory.FromExplicit(_root), "JavaScript",
            await capabilities.ReadAsync(_root, CancellationToken.None));
        Write(logicalPath, "not-json-1");
        string path = NativePath(logicalPath);
        DateTime timestamp = File.GetLastWriteTimeUtc(path);
        IReadOnlyList<ProjectCapability> present = await capabilities.ReadAsync(_root, CancellationToken.None);

        Write(logicalPath, "not-json-2");
        File.SetLastWriteTimeUtc(path, timestamp);
        File.GetLastWriteTimeUtc(path).Should().Be(timestamp);
        new FileInfo(path).Length.Should().Be(10);
        IReadOnlyList<ProjectCapability> changed = await capabilities.ReadAsync(_root, CancellationToken.None);
        File.Delete(path);
        IReadOnlyList<ProjectCapability> removed = await capabilities.ReadAsync(_root, CancellationToken.None);

        File.Exists(path).Should().BeFalse();
        absent.Capabilities[0].Support.Should().Be(ProjectCapabilitySupport.Supported);
        present.Should().BeEmpty();
        changed.Should().BeEmpty();
        removed.Should().ContainSingle().Which.Support.Should().Be(ProjectCapabilitySupport.Supported);
    }

    [Fact]
    public async Task Capture_RealProject_ReadsWithoutCreatingOrChangingProjectContent()
    {
        Write("package.json", """{"main":"src/functions/*.js","dependencies":{"@azure/functions":"^4.0.0"}}""");
        Write("host.json", """{"version":"2.0"}""");
        Write("unrelated.txt", "preserve me");
        string[] before = Snapshot();
        IReadOnlyList<ProjectCapability> capabilities = await new NodeProjectCapabilityReader(_reader).ReadAsync(_root, CancellationToken.None);
        var project = new NodeFunctionsProject(WorkingDirectory.FromExplicit(_root), "JavaScript", capabilities);
        project.RunNpm = (_, _, _, _, _) => throw new InvalidOperationException("Capability inspection must not invoke npm.");

        project.Capabilities[0].Support.Should().Be(ProjectCapabilitySupport.Supported);
        Snapshot().Should().Equal(before);
    }

    [Theory]
    [InlineData(null, "package.json")]
    [InlineData("", "package.json")]
    [InlineData("relative-root", "package.json")]
    [InlineData("root", null)]
    [InlineData("root", "")]
    [InlineData("root", "../package.json")]
    [InlineData("root", "src//package.json")]
    [InlineData("root", "/package.json")]
    [InlineData("root", "src\\package.json")]
    public async Task ReadAsync_InvalidArguments_Throw(string? root, string? path)
    {
        await FluentActions.Awaiting(() => _reader.ReadAsync(root == "root" ? _root : root!, path!, CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ReadAsync_PreCanceled_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await FluentActions.Awaiting(() => _reader.ReadAsync(_root, "package.json", cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [UnixFact]
    public async Task ReadAsync_Fifo_IsRejectedBeforeADataRead()
    {
        string path = NativePath("package.json");
        int result = OperatingSystem.IsMacOS() ? MacMakeFifo(path, 0x180) : LinuxMakeFifo(path, 0x180);
        result.Should().Be(0, "the controlled FIFO fixture must be created");

        (NodeProjectFileKind Kind, byte[] Bytes) observed = await _reader.ReadAsync(_root, "package.json", CancellationToken.None);

        observed.Kind.Should().Be(NodeProjectFileKind.Unsupported);
        observed.Bytes.Should().BeEmpty();
    }

    [UnixFact]
    public async Task ReadAsync_UnixSocket_IsNotARegularFile()
    {
        string path = NativePath("package.json");
        using Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        socket.Bind(new UnixDomainSocketEndPoint(path));

        (NodeProjectFileKind Kind, byte[] Bytes) observed = await _reader.ReadAsync(_root, "package.json", CancellationToken.None);

        observed.Kind.Should().Be(NodeProjectFileKind.Unsupported);
    }

    [UnixFact]
    public async Task ReadAsync_KnownLeafAndEnclosingLinks_AreNotFollowed()
    {
        Write("target.json", "{}");
        File.CreateSymbolicLink(NativePath("package.json"), NativePath("target.json"));
        File.CreateSymbolicLink(NativePath("host.json"), NativePath("missing.json"));
        Directory.CreateDirectory(NativePath("target"));
        Directory.CreateSymbolicLink(NativePath("src"), NativePath("target"));
        string[] paths = ["package.json", "host.json", "src/package.json", "src/functions/package.json"];

        foreach (string path in paths)
        {
            (NodeProjectFileKind Kind, byte[] Bytes) observed = await _reader.ReadAsync(_root, path, CancellationToken.None);

            observed.Kind.Should().Be(NodeProjectFileKind.Unsupported);
        }
    }

    private string NativePath(string logicalPath) => Path.Combine(_root, logicalPath.Replace('/', Path.DirectorySeparatorChar));

    private void Write(string logicalPath, string content)
    {
        string path = NativePath(logicalPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private string[] Snapshot()
        => [.. Directory.GetFileSystemEntries(_root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(path =>
            Directory.Exists(path)
                ? $"directory:{Path.GetRelativePath(_root, path)}"
                : $"{Path.GetRelativePath(_root, path)}:{Convert.ToHexString(File.ReadAllBytes(path))}:{File.GetLastWriteTimeUtc(path).Ticks}")];

    [LibraryImport("libc", EntryPoint = "mkfifo", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int LinuxMakeFifo(string path, uint mode);

    [LibraryImport("libSystem.B.dylib", EntryPoint = "mkfifo", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int MacMakeFifo(string path, uint mode);

    private sealed class UnixFactAttribute : FactAttribute
    {
        public UnixFactAttribute()
        {
            if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            {
                Skip = "Requires Linux or macOS to validate native file-kind observations.";
            }
        }
    }
}
