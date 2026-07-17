// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Build.Framework;
using NSubstitute;

namespace Azure.Functions.Cli.Workloads.Sdk.Tasks.Tests;

public class ResolveWorkloadCopyLocalTests
{
    [Fact]
    public void Execute_NoUnifiedItems_ReturnsAllItems()
    {
        ITaskItem[] items =
        [
            CreateCopyLocalItem("lib/MyLib.dll", nuGetPackageId: "MyLib"),
            CreateCopyLocalItem("lib/Other.dll", nuGetPackageId: "Other"),
        ];

        ResolveWorkloadCopyLocal task = CreateTask(items, unifiedItems: []);

        bool result = task.Execute();

        result.Should().BeTrue();
        task.WorkloadCopyLocal.Length.Should().Be(2);
    }

    [Fact]
    public void Execute_ExcludesItemFromUnifiedPackage()
    {
        ITaskItem[] items =
        [
            CreateCopyLocalItem("lib/Keep.dll", nuGetPackageId: "Keep.Package"),
            CreateCopyLocalItem("lib/Exclude.dll", nuGetPackageId: "Unified.Package"),
        ];

        ITaskItem[] unified = [CreateUnifiedItem("Unified.Package", "Package")];

        ResolveWorkloadCopyLocal task = CreateTask(items, unified);

        task.Execute();

        task.WorkloadCopyLocal.Should().ContainSingle();
        task.WorkloadCopyLocal.Should().Contain(i => i.ItemSpec == "lib/Keep.dll");
    }

    [Fact]
    public void Execute_ExcludesItemFromUnifiedProject()
    {
        string projectPath = Path.GetFullPath("src/Shared/Shared.csproj");

        ITaskItem[] items =
        [
            CreateCopyLocalItem("lib/Keep.dll", projectFile: "src/Other/Other.csproj"),
            CreateCopyLocalItem("lib/Shared.dll", projectFile: projectPath),
        ];

        ITaskItem[] unified = [CreateUnifiedItem(projectPath, "Project")];

        ResolveWorkloadCopyLocal task = CreateTask(items, unified);

        task.Execute();

        task.WorkloadCopyLocal.Should().ContainSingle();
        task.WorkloadCopyLocal.Should().Contain(i => i.ItemSpec == "lib/Keep.dll");
    }

    [Fact]
    public void Execute_PackageExclusion_IsCaseInsensitive()
    {
        ITaskItem[] items =
        [
            CreateCopyLocalItem("lib/Foo.dll", nuGetPackageId: "MY.PACKAGE"),
        ];

        ITaskItem[] unified = [CreateUnifiedItem("my.package", "Package")];

        ResolveWorkloadCopyLocal task = CreateTask(items, unified);

        task.Execute();

        task.WorkloadCopyLocal.Should().BeEmpty();
    }

    [Fact]
    public void Execute_SetsTargetPathFromDestinationSubPath()
    {
        ITaskItem item = CreateCopyLocalItem("lib/MyLib.dll", nuGetPackageId: "MyLib");
        item.GetMetadata("DestinationSubPath").Returns("runtimes/win/MyLib.dll");

        ResolveWorkloadCopyLocal task = CreateTask([item], unifiedItems: []);

        task.Execute();

        task.WorkloadCopyLocal.Should().ContainSingle();
        task.WorkloadCopyLocal[0].Received().SetMetadata("TargetPath", "runtimes/win/MyLib.dll");
    }

    [Fact]
    public void Execute_NoDestinationSubPath_SetsTargetPathToFileName()
    {
        ITaskItem item = CreateCopyLocalItem("path/to/MyLib.dll", nuGetPackageId: "MyLib");
        item.GetMetadata("DestinationSubPath").Returns(string.Empty);

        ResolveWorkloadCopyLocal task = CreateTask([item], unifiedItems: []);

        task.Execute();

        task.WorkloadCopyLocal.Should().ContainSingle();
        task.WorkloadCopyLocal[0].Received().SetMetadata("TargetPath", "MyLib.dll");
    }

    [Fact]
    public void Execute_EmptyItems_ReturnsEmpty()
    {
        ResolveWorkloadCopyLocal task = CreateTask([], unifiedItems: []);

        bool result = task.Execute();

        result.Should().BeTrue();
        task.WorkloadCopyLocal.Should().BeEmpty();
    }

    [Fact]
    public void Execute_ItemWithNoPackageOrProject_IsIncluded()
    {
        ITaskItem item = CreateCopyLocalItem("lib/Standalone.dll");

        ResolveWorkloadCopyLocal task = CreateTask([item], unifiedItems: []);

        task.Execute();

        task.WorkloadCopyLocal.Should().ContainSingle();
    }

    [Fact]
    public void Execute_MultipleUnifiedPackages_ExcludesAll()
    {
        ITaskItem[] items =
        [
            CreateCopyLocalItem("lib/A.dll", nuGetPackageId: "PkgA"),
            CreateCopyLocalItem("lib/B.dll", nuGetPackageId: "PkgB"),
            CreateCopyLocalItem("lib/C.dll", nuGetPackageId: "PkgC"),
        ];

        ITaskItem[] unified =
        [
            CreateUnifiedItem("PkgA", "Package"),
            CreateUnifiedItem("PkgB", "Package"),
        ];

        ResolveWorkloadCopyLocal task = CreateTask(items, unified);

        task.Execute();

        task.WorkloadCopyLocal.Should().ContainSingle();
        task.WorkloadCopyLocal.Should().Contain(i => i.ItemSpec == "lib/C.dll");
    }

    private static ResolveWorkloadCopyLocal CreateTask(ITaskItem[] items, ITaskItem[] unifiedItems)
    {
        return new ResolveWorkloadCopyLocal
        {
            BuildEngine = Substitute.For<IBuildEngine>(),
            Items = items,
            UnifiedItems = unifiedItems,
        };
    }

    private static ITaskItem CreateCopyLocalItem(
        string itemSpec,
        string nuGetPackageId = "",
        string projectFile = "")
    {
        ITaskItem item = Substitute.For<ITaskItem>();
        item.ItemSpec.Returns(itemSpec);
        item.GetMetadata("NuGetPackageId").Returns(nuGetPackageId);
        item.GetMetadata("MSBuildSourceProjectFile").Returns(projectFile);
        item.GetMetadata("DestinationSubPath").Returns(string.Empty);
        return item;
    }

    private static ITaskItem CreateUnifiedItem(string itemSpec, string kind)
    {
        ITaskItem item = Substitute.For<ITaskItem>();
        item.ItemSpec.Returns(itemSpec);
        item.GetMetadata("Kind").Returns(kind);
        return item;
    }
}
