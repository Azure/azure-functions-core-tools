// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography.X509Certificates;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using NSubstitute;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Workloads.Install;

public sealed partial class WorkloadPackageTrustTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Certificate_WithWrongSigningPurpose_IsRejected(bool wrongTimestampPurpose)
    {
        using X509Certificate2 root = CreateRoot();
        using X509Certificate2 author = CreateAuthor(root, MicrosoftSubject, timestamping: !wrongTimestampPurpose);
        using X509Certificate2 timestamper = CreateAuthor(root, "CN=Test Timestamp Authority");
        string signed = await SignAsync(BuildNupkg(id: OfficialId), root, author,
            wrongTimestampPurpose ? timestamper : null, wrongTimestampPurpose ? root : null);
        Func<Task> act = async () =>
        {
            using var snapshot = await new TestRootTrust(root).AcquireAsync(signed, false, TestContextToken());
        };

        await act.Should().ThrowExactlyAsync<InvalidWorkloadException>();
    }

    [Fact]
    public async Task NotYetValidAuthor_WithoutTimestamp_IsRejected()
    {
        using X509Certificate2 root = CreateRoot();
        using X509Certificate2 author = CreateAuthor(root, MicrosoftSubject, notBefore: DateTimeOffset.UtcNow.AddHours(2));
        string signed = await SignAsync(BuildNupkg(id: OfficialId), root, author);
        Func<Task> act = async () =>
        {
            using var snapshot = await new TestRootTrust(root).AcquireAsync(signed, false, TestContextToken());
        };

        await act.Should().ThrowExactlyAsync<InvalidWorkloadException>();
    }

    [Theory]
    [InlineData("local", false)]
    [InlineData("catalog", false)]
    [InlineData("update", false)]
    [InlineData("local", true)]
    [InlineData("catalog", true)]
    [InlineData("update", true)]
    public async Task PointerAcquisitions_TrustChoiceAppliesToBothArchives(string route, bool signed)
    {
        const string id = "azure.functions.cli.workloads.test";
        const string implementationId = id + ".win-x64";
        using X509Certificate2 root = CreateRoot();
        using X509Certificate2 author = CreateAuthor(root, MicrosoftSubject);
        string pointer = BuildPointerNupkg(id, "1.1.0", $$"""{"win-x64":"{{implementationId}}"}""");
        string implementation = BuildRidImplementationNupkg(implementationId, "1.1.0", "win-x64");
        if (signed)
        {
            pointer = await SignAsync(pointer, root, author);
            implementation = await SignAsync(implementation, root, author);
        }

        string sibling = Path.Combine(Root, $"{implementationId}.1.1.0.nupkg");
        File.Copy(implementation, sibling);
        byte[] pointerBytes = await File.ReadAllBytesAsync(pointer);
        byte[] implementationBytes = await File.ReadAllBytesAsync(sibling);
        ConfigurePointerCatalog(id, "1.1.0", pointer, sibling);
        WorkloadEntry current = new()
        {
            PackageId = implementationId,
            PackageVersion = "1.0.0",
            InstallRefCount = 1,
            LogicalPackage = new LogicalPackage { PackageId = id, PackageVersion = "1.0.0", Source = "https://example/v3/index.json" },
            RuntimeIdentifier = "win-x64",
        };
        string oldPath = Paths.GetInstallDirectory(implementationId, "1.0.0");
        if (route == "update")
        {
            Store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([current]);
            Directory.CreateDirectory(oldPath);
            File.WriteAllText(Path.Combine(oldPath, "existing.txt"), "old version");
        }

        List<WorkloadPackageSnapshot> snapshots = [];
        WorkloadInstaller installer = NewInstaller(metadataReader: new WorkloadMetadataReader(),
            packageTrust: TrackSnapshots(new TestRootTrust(root), snapshots));

        WorkloadEntry entry = route switch
        {
            "local" => (await installer.InstallFromPackageAsync(pointer, allowUntrusted: !signed)).Entry,
            "catalog" => (await installer.InstallFromCatalogAsync(id, null, null, false, true, false, allowUntrusted: !signed)).Entry,
            _ => (await installer.UpdateAsync(id, null, null, false, false, WorkloadOwnershipKind.Logical, allowUntrusted: !signed)).Entry,
        };

        entry.PackageId.Should().Be(implementationId);
        entry.PackageVersion.Should().Be("1.1.0");
        entry.RuntimeIdentifier.Should().Be("win-x64");
        entry.LogicalPackage!.PackageId.Should().Be(id);
        entry.LogicalPackage.PackageVersion.Should().Be("1.1.0");
        entry.LogicalPackage.Source.Should().Be(route == "local" ? Path.GetFullPath(pointer) : "https://example/v3/index.json");
        File.ReadAllBytes(Path.Combine(Paths.GetInstallDirectory(implementationId, "1.1.0"), "tools", "any", "Test.dll"))
            .Should().Equal([0x4D, 0x5A]);
        (await File.ReadAllBytesAsync(pointer)).Should().Equal(pointerBytes);
        (await File.ReadAllBytesAsync(sibling)).Should().Equal(implementationBytes);
        snapshots.Should().HaveCount(2);
        foreach (WorkloadPackageSnapshot snapshot in snapshots)
        {
            File.Exists(snapshot.Path).Should().BeFalse();
            Directory.Exists(Path.GetDirectoryName(snapshot.Path)).Should().BeFalse();
        }

        if (route == "update")
        {
            Directory.Exists(oldPath).Should().BeFalse();
            await Store.Received(1).MoveLogicalOwnershipAsync(implementationId, "1.0.0", entry, Arg.Any<CancellationToken>());
        }
        else
        {
            await Store.Received(1).SaveWorkloadAsync(entry, Arg.Any<CancellationToken>());
        }
    }

    [Theory]
    [InlineData("inspection")]
    [InlineData("deployment")]
    [InlineData("pointer-cancellation")]
    [InlineData("implementation-cancellation")]
    [InlineData("implementation-trust")]
    public async Task PointerUpdate_FailureOrCancellation_DisposesSnapshotsAndPreservesOldVersion(string failureStage)
    {
        const string id = "azure.functions.cli.workloads.test";
        const string implementationId = id + ".win-x64";
        using X509Certificate2 root = CreateRoot();
        using X509Certificate2 author = CreateAuthor(root, MicrosoftSubject);
        string pointer = await SignAsync(BuildPointerNupkg(id, "1.1.0", $$"""{"win-x64":"{{implementationId}}"}"""), root, author);
        string implementation = BuildRidImplementationNupkg(implementationId, "1.1.0", "win-x64",
            failureStage == "inspection" ? WorkloadPackageTypes.Workload : WorkloadPackageTypes.RuntimeIdentifierPackage);
        if (failureStage != "implementation-trust")
        {
            implementation = await SignAsync(implementation, root, author);
        }

        ConfigurePointerCatalog(id, "1.1.0", pointer, implementation);
        WorkloadEntry current = new()
        {
            PackageId = implementationId,
            PackageVersion = "1.0.0",
            InstallRefCount = 1,
            LogicalPackage = new LogicalPackage { PackageId = id, PackageVersion = "1.0.0", Source = "https://example/v3/index.json" },
            RuntimeIdentifier = "win-x64",
        };
        Store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([current]);
        string oldPath = Paths.GetInstallDirectory(implementationId, "1.0.0");
        Directory.CreateDirectory(oldPath);
        File.WriteAllText(Path.Combine(oldPath, "existing.txt"), "keep installed version");
        using CancellationTokenSource cancellation = new();
        List<WorkloadPackageSnapshot> snapshots = [];
        IWorkloadPackageTrust trust = TrackSnapshots(new TestRootTrust(root), snapshots, count =>
        {
            if ((failureStage == "pointer-cancellation" && count == 1)
                || (failureStage == "implementation-cancellation" && count == 2))
            {
                cancellation.Cancel();
            }
        });
        InvalidOperationException failure = new("test deployment failure");
        WorkloadMetadataReader realReader = new();
        IWorkloadMetadataReader reader = Substitute.For<IWorkloadMetadataReader>();
        reader.Read(Arg.Any<string>()).Returns(call =>
        {
            string path = call.Arg<string>();
            if (failureStage == "deployment" && path.Contains(".staging-", StringComparison.Ordinal))
            {
                throw failure;
            }

            return realReader.Read(path);
        });
        WorkloadInstaller installer = NewInstaller(metadataReader: reader, packageTrust: trust);
        Func<Task> act = () => installer.UpdateAsync(id, null, null, false, false, WorkloadOwnershipKind.Logical,
            cancellationToken: cancellation.Token);

        try
        {
            if (failureStage.EndsWith("cancellation", StringComparison.Ordinal))
            {
                (await act.Should().ThrowAsync<OperationCanceledException>()).Which.CancellationToken.Should().Be(cancellation.Token);
            }
            else if (failureStage == "deployment")
            {
                (await act.Should().ThrowExactlyAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);
            }
            else
            {
                await act.Should().ThrowExactlyAsync<InvalidWorkloadException>();
            }

            snapshots.Should().HaveCount(failureStage is "pointer-cancellation" or "implementation-trust" ? 1 : 2);
            foreach (WorkloadPackageSnapshot snapshot in snapshots)
            {
                File.Exists(snapshot.Path).Should().BeFalse("the installer owns snapshot {0}", snapshot.Path);
                Directory.Exists(Path.GetDirectoryName(snapshot.Path)).Should().BeFalse();
            }

            File.ReadAllText(Path.Combine(oldPath, "existing.txt")).Should().Be("keep installed version");
            Directory.Exists(Paths.GetInstallDirectory(implementationId, "1.1.0")).Should().BeFalse();
            await Store.DidNotReceive().SaveWorkloadAsync(Arg.Any<WorkloadEntry>(), Arg.Any<CancellationToken>());
            await Store.DidNotReceive().MoveLogicalOwnershipAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<WorkloadEntry>(),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            foreach (WorkloadPackageSnapshot snapshot in snapshots)
            {
                snapshot.Dispose();
            }
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PointerReuse_VerifiesPointerWithoutReacquiringHistoricalImplementation(bool update, bool signedPointer)
    {
        const string id = "azure.functions.cli.workloads.test";
        const string implementationId = id + ".win-x64";
        using X509Certificate2 root = CreateRoot();
        using X509Certificate2 author = CreateAuthor(root, MicrosoftSubject);
        string pointer = BuildPointerNupkg(id, "1.1.0", $$"""{"win-x64":"{{implementationId}}"}""");
        if (signedPointer)
        {
            pointer = await SignAsync(pointer, root, author);
        }

        ConfigurePointerCatalog(id, "1.1.0", pointer, null);
        WorkloadEntry historical = new()
        {
            PackageId = implementationId,
            PackageVersion = "1.1.0",
            RuntimeIdentifier = "win-x64",
            Source = "https://other.test/v3/index.json",
        };
        WorkloadEntry current = new()
        {
            PackageId = implementationId,
            PackageVersion = "1.0.0",
            RuntimeIdentifier = "win-x64",
            LogicalPackage = new LogicalPackage { PackageId = id, PackageVersion = "1.0.0", Source = "https://example/v3/index.json" },
        };
        InstallImplementationOnDisk(implementationId, "1.1.0", "win-x64");
        string marker = Path.Combine(Paths.GetInstallDirectory(implementationId, "1.1.0"), "historical.txt");
        File.WriteAllText(marker, "historical unsigned payload");
        Store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(update ? [current, historical] : [historical]);
        List<WorkloadPackageSnapshot> snapshots = [];
        WorkloadInstaller installer = NewInstaller(metadataReader: new WorkloadMetadataReader(),
            packageTrust: TrackSnapshots(new TestRootTrust(root), snapshots));

        Func<Task<WorkloadEntry>> act = async () => update
            ? (await installer.UpdateAsync(id, null, null, false, false, WorkloadOwnershipKind.Logical)).Entry
            : (await installer.InstallFromCatalogAsync(id, null, null, false, true, false)).Entry;

        if (signedPointer)
        {
            WorkloadEntry entry = await act();
            entry.PackageId.Should().Be(implementationId);
            entry.PackageVersion.Should().Be("1.1.0");
            entry.LogicalPackage!.PackageId.Should().Be(id);
            snapshots.Should().ContainSingle();
            File.Exists(snapshots[0].Path).Should().BeFalse();
        }
        else
        {
            await act.Should().ThrowExactlyAsync<InvalidWorkloadException>().WithMessage("*primary NuGet author signature*");
            snapshots.Should().BeEmpty();
            await Store.DidNotReceive().AddOwnershipAsync(Arg.Any<WorkloadEntry>(), Arg.Any<WorkloadOwnershipKind>(),
                Arg.Any<CancellationToken>());
            await Store.DidNotReceive().MoveLogicalOwnershipAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<WorkloadEntry>(),
                Arg.Any<CancellationToken>());
        }

        File.ReadAllText(marker).Should().Be("historical unsigned payload");
        await Catalog.Received(1).DownloadAsync(Arg.Is<ResolvedPackage>(p => p.PackageId == id), Arg.Any<CancellationToken>());
        await Catalog.DidNotReceive().ResolveVersionAsync(Arg.Any<string>(), Arg.Any<NuGetVersion>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
        await Catalog.DidNotReceive().DownloadAsync(Arg.Is<ResolvedPackage>(p => p.PackageId == implementationId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ForcedPointerInstall_RejectsUnsignedReplacementWithoutChangingHistoricalPayload()
    {
        const string id = "azure.functions.cli.workloads.test";
        const string implementationId = id + ".win-x64";
        using X509Certificate2 root = CreateRoot();
        using X509Certificate2 author = CreateAuthor(root, MicrosoftSubject);
        string pointer = await SignAsync(BuildPointerNupkg(id, "1.1.0", $$"""{"win-x64":"{{implementationId}}"}"""), root, author);
        string implementation = BuildRidImplementationNupkg(implementationId, "1.1.0", "win-x64");
        ConfigurePointerCatalog(id, "1.1.0", pointer, implementation);
        InstallImplementationOnDisk(implementationId, "1.1.0", "win-x64");
        string marker = Path.Combine(Paths.GetInstallDirectory(implementationId, "1.1.0"), "historical.txt");
        File.WriteAllText(marker, "preserve");
        Store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(
            [new WorkloadEntry { PackageId = implementationId, PackageVersion = "1.1.0", RuntimeIdentifier = "win-x64" }]);
        WorkloadInstaller installer = NewInstaller(metadataReader: new WorkloadMetadataReader(), packageTrust: new TestRootTrust(root));
        Func<Task> act = () => installer.InstallFromCatalogAsync(id, null, null, false, true, true);

        await act.Should().ThrowExactlyAsync<InvalidWorkloadException>().WithMessage("*primary NuGet author signature*");

        File.ReadAllText(marker).Should().Be("preserve");
        await Catalog.Received(1).DownloadAsync(Arg.Is<ResolvedPackage>(p => p.PackageId == implementationId), Arg.Any<CancellationToken>());
        await Store.DidNotReceive().SaveWorkloadAsync(Arg.Any<WorkloadEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SignedUnapprovedRoot_ProcessRejectsWithoutLeakingSnapshots()
    {
        using X509Certificate2 root = CreateRoot();
        using X509Certificate2 author = CreateAuthor(root, MicrosoftSubject);
        string signed = await SignAsync(BuildNupkg(id: OfficialId), root, author);
        byte[] original = await File.ReadAllBytesAsync(signed);
        CliConfigurationPathsOptions paths = new(Path.Combine(Root, "home"));
        WorkloadPathsOptions workloads = new(Path.Combine(Root, "workload-home"));
        CliProcessTestRunner runner = new(output);
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(60));

        var result = await runner.RunAsync(Root, paths.Home, workloads.Home, Root,
            ["workload", "install", signed], cancellation.Token);

        result.ExitCode.Should().Be(1);
        (result.StandardOutput + result.StandardError).Should().Contain("Workload publisher verification failed");
        File.Exists(workloads.WorkloadRegistryPath).Should().BeFalse();
        Directory.Exists(workloads.GetInstallDirectory(OfficialId.ToLowerInvariant(), "1.0.0")).Should().BeFalse();
        (await File.ReadAllBytesAsync(signed)).Should().Equal(original);
        Directory.GetDirectories(Path.Combine(Root, CliProcessTestRunner.StateDirectoryName), "func-workload-trust-*",
            SearchOption.AllDirectories).Should().BeEmpty();
    }

    private void ConfigurePointerCatalog(string id, string version, string pointer, string? implementation)
    {
        var resolved = NewResolved(id, version);
        var implementationResolved = NewResolved(id + ".win-x64", version);
        Catalog.ResolveLatestVersionAsync(id, Arg.Any<bool?>(), Arg.Any<NuGetVersion?>(), Arg.Any<bool>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(resolved);
        Catalog.DownloadAsync(resolved, Arg.Any<CancellationToken>()).Returns(_ => File.OpenRead(pointer));
        Catalog.ResolveVersionAsync(implementationResolved.PackageId, Arg.Any<NuGetVersion>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>()).Returns(implementationResolved);
        if (implementation is not null)
        {
            Catalog.DownloadAsync(implementationResolved, Arg.Any<CancellationToken>()).Returns(_ => File.OpenRead(implementation));
        }
    }

    private static IWorkloadPackageTrust TrackSnapshots(IWorkloadPackageTrust inner, List<WorkloadPackageSnapshot> snapshots,
        Action<int>? onAcquired = null)
    {
        IWorkloadPackageTrust trust = Substitute.For<IWorkloadPackageTrust>();
        trust.AcquireAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            WorkloadPackageSnapshot snapshot = await inner.AcquireAsync(call.Arg<string>(), call.Arg<bool>(), call.Arg<CancellationToken>());
            snapshots.Add(snapshot);
            onAcquired?.Invoke(snapshots.Count);
            return snapshot;
        });
        return trust;
    }
}
