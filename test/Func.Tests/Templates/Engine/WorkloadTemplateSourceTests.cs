// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class WorkloadTemplateSourceTests
{
    private static readonly string _installDirectory = Path.Combine(Path.GetTempPath(), "workload");
    private static readonly string _templateRoot = Path.Combine(_installDirectory, "templates");
    private static readonly string _metadataPath = Path.Combine(_installDirectory, "workload.json");

    private readonly IWorkloadProvider _workloadProvider = Substitute.For<IWorkloadProvider>();
    private readonly IFileSystem _fileSystem = Substitute.For<IFileSystem>();

    [Fact]
    public async Task GetRootsAsync_MapsRuntimeWorkloadTemplateRoots()
    {
        DateTime rootChange = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        SetWorkloads(CreateWorkload(_templateRoot));
        _fileSystem.DirectoryExists(_templateRoot).Returns(true);
        _fileSystem.GetLastWriteTimeUtc(_templateRoot).Returns(rootChange);
        _fileSystem.FileExists(_metadataPath).Returns(true);
        _fileSystem.GetLastWriteTimeUtc(_metadataPath).Returns(rootChange.AddDays(-1));

        IReadOnlyList<WorkloadTemplateRoot> roots = await CreateSource().GetRootsAsync(CancellationToken.None);

        roots.Should().ContainSingle().Which.Should().Be(new WorkloadTemplateRoot(_templateRoot, "Test.Workload.A", "1.2.3", rootChange));
    }

    [Fact]
    public async Task GetRootsAsync_UsesMetadataTime_WhenNewerThanTemplateRoot()
    {
        DateTime rootChange = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime metadataChange = rootChange.AddDays(1);
        SetWorkloads(CreateWorkload(_templateRoot));
        _fileSystem.DirectoryExists(_templateRoot).Returns(true);
        _fileSystem.GetLastWriteTimeUtc(_templateRoot).Returns(rootChange);
        _fileSystem.FileExists(_metadataPath).Returns(true);
        _fileSystem.GetLastWriteTimeUtc(_metadataPath).Returns(metadataChange);

        IReadOnlyList<WorkloadTemplateRoot> roots = await CreateSource().GetRootsAsync(CancellationToken.None);

        roots.Should().ContainSingle().Which.LastChangeTime.Should().Be(metadataChange);
    }

    [Fact]
    public async Task GetRootsAsync_UsesTemplateRootTime_WhenMetadataMissing()
    {
        DateTime rootChange = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        SetWorkloads(CreateWorkload(_templateRoot));
        _fileSystem.DirectoryExists(_templateRoot).Returns(true);
        _fileSystem.GetLastWriteTimeUtc(_templateRoot).Returns(rootChange);

        IReadOnlyList<WorkloadTemplateRoot> roots = await CreateSource().GetRootsAsync(CancellationToken.None);

        roots.Should().ContainSingle().Which.LastChangeTime.Should().Be(rootChange);
        _fileSystem.DidNotReceive().GetLastWriteTimeUtc(_metadataPath);
    }

    [Fact]
    public async Task GetRootsAsync_SkipsWorkloadsWithoutTemplateRoot()
    {
        SetWorkloads(CreateWorkload(templateRoot: null));

        IReadOnlyList<WorkloadTemplateRoot> roots = await CreateSource().GetRootsAsync(CancellationToken.None);

        roots.Should().BeEmpty();
        _fileSystem.DidNotReceiveWithAnyArgs().DirectoryExists(default!);
    }

    [Fact]
    public async Task GetRootsAsync_SkipsMissingTemplateRoot()
    {
        SetWorkloads(CreateWorkload(_templateRoot));
        _fileSystem.DirectoryExists(_templateRoot).Returns(false);

        IReadOnlyList<WorkloadTemplateRoot> roots = await CreateSource().GetRootsAsync(CancellationToken.None);

        roots.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRootsAsync_Throws_WhenCancelled()
    {
        SetWorkloads(CreateWorkload(_templateRoot));

        Func<Task> act = () => CreateSource().GetRootsAsync(new CancellationToken(canceled: true));

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Ctor_NullDependencies_Throw()
    {
        FluentActions.Invoking(() => new WorkloadTemplateSource(null!, _fileSystem)).Should().ThrowExactly<ArgumentNullException>();
        FluentActions.Invoking(() => new WorkloadTemplateSource(_workloadProvider, null!)).Should().ThrowExactly<ArgumentNullException>();
    }

    private WorkloadTemplateSource CreateSource() => new(_workloadProvider, _fileSystem);

    private void SetWorkloads(params RuntimeWorkloadInfo[] workloads) => _workloadProvider.GetRuntimeWorkloads().Returns(workloads);

    private static RuntimeWorkloadInfo CreateWorkload(string? templateRoot)
        => TestWorkloads.CreateInfo(version: "1.2.3") with { InstallDirectory = _installDirectory, TemplateRoot = templateRoot };
}
