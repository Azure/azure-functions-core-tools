// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Storage;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class InstalledTemplatesWorkloadsAuditTests : IDisposable
{
    private readonly IWorkloadStore _store = Substitute.For<IWorkloadStore>();
    private readonly IWorkloadPaths _paths = Substitute.For<IWorkloadPaths>();
    private readonly CancellationTokenSource _cancellation = new();

    public InstalledTemplatesWorkloadsAuditTests()
    {
        _paths.GetInstallDirectory(Arg.Any<string>(), Arg.Any<string>())
            .Returns(call => Path.Combine("installed", call.ArgAt<string>(0), call.ArgAt<string>(1)));
    }

    public void Dispose() => _cancellation.Dispose();

    [Theory]
    [InlineData(true, "store")]
    [InlineData(false, "paths")]
    public void Constructor_NullDependency_ThrowsArgumentNullException(bool nullStore, string parameterName)
    {
        Action act = () => _ = new InstalledTemplatesWorkloads(nullStore ? null! : _store, nullStore ? _paths : null!);

        act.Should().ThrowExactly<ArgumentNullException>().WithParameterName(parameterName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public async Task ListInstalledAsync_InvalidStack_ThrowsBeforeReadingStore(string? stack)
    {
        InstalledTemplatesWorkloads workloads = new(_store, _paths);

        Func<Task> act = () => workloads.ListInstalledAsync(stack!, _cancellation.Token);

        await act.Should().ThrowExactlyAsync<ArgumentException>().WithParameterName("stack");
        await _store.DidNotReceive().GetWorkloadsAsync(Arg.Any<CancellationToken>());
        _paths.DidNotReceive().GetInstallDirectory(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task ListInstalledAsync_EmptyStore_ReturnsEmptyAndForwardsCancellationToken()
    {
        SetEntries();

        IReadOnlyList<InstalledTemplatesWorkload> result =
            await new InstalledTemplatesWorkloads(_store, _paths).ListInstalledAsync("node", _cancellation.Token);

        result.Should().BeEmpty();
        await _store.Received(1).GetWorkloadsAsync(_cancellation.Token);
        _paths.DidNotReceive().GetInstallDirectory(Arg.Any<string>(), Arg.Any<string>());
    }

    [Theory]
    [InlineData("node")]
    [InlineData("NODE")]
    [InlineData(" NoDe ")]
    public async Task ListInstalledAsync_DeclaredAlias_FindsNonConventionalPackageCaseInsensitively(string stack)
    {
        SetEntries(Content("Contoso.FunctionTemplates", aliases: ["unrelated", "NoDe-TeMpLaTeS"]));

        IReadOnlyList<InstalledTemplatesWorkload> result =
            await new InstalledTemplatesWorkloads(_store, _paths).ListInstalledAsync(stack, _cancellation.Token);

        result.Should().ContainSingle().Which.Should().Be(new InstalledTemplatesWorkload(
            "node", "1.0.0", Path.Combine("installed", "Contoso.FunctionTemplates", "1.0.0")));
        await _store.Received(1).GetWorkloadsAsync(_cancellation.Token);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ListInstalledAsync_ConventionalIdWithoutAliases_RemainsCompatible(bool hasLogicalOwner, bool implicitInstall)
    {
        const string conventionalId = "azure.functions.cli.workloads.templates.NODE";
        string physicalId = hasLogicalOwner ? "Contoso.TemplatePayload.win-x64" : conventionalId;
        SetEntries(Content(
            physicalId,
            aliases: hasLogicalOwner ? ["python-templates"] : [],
            logicalPackage: hasLogicalOwner ? Logical(conventionalId) : null,
            implicitInstall: implicitInstall));

        IReadOnlyList<InstalledTemplatesWorkload> result =
            await new InstalledTemplatesWorkloads(_store, _paths).ListInstalledAsync(" NODE ", _cancellation.Token);

        result.Should().ContainSingle().Which.Should().Be(new InstalledTemplatesWorkload(
            "node", "1.0.0", Path.Combine("installed", physicalId, "1.0.0")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ListInstalledAsync_ConventionalIdWithWrongAlias_PreservesConventionalOwner(bool hasLogicalOwner)
    {
        const string conventionalId = "Azure.Functions.Cli.Workloads.Templates.Node";
        SetEntries(Content(
            conventionalId,
            aliases: hasLogicalOwner ? ["node-templates"] : ["python-templates"],
            logicalPackage: hasLogicalOwner ? Logical(conventionalId, aliases: ["python-templates"]) : null));

        IReadOnlyList<InstalledTemplatesWorkload> result =
            await new InstalledTemplatesWorkloads(_store, _paths).ListInstalledAsync("node", _cancellation.Token);

        result.Should().ContainSingle().Which.InstallDirectory.Should().Be(Path.Combine("installed", conventionalId, "1.0.0"));
    }

    [Theory]
    [InlineData("Contoso.Templates", "node")]
    [InlineData("Contoso.Templates", "node-worker")]
    [InlineData("Contoso.Templates", "node-templates-preview")]
    [InlineData("Contoso.Templates", "prefix-node-templates")]
    [InlineData("Contoso.Templates", " node-templates ")]
    [InlineData("Contoso.Templates", "python-templates")]
    [InlineData("Contoso.Templates", null)]
    [InlineData("Azure.Functions.Cli.Workloads.Templates.Node.Extra", null)]
    [InlineData("Azure.Functions.Cli.Workloads.Templates.Python", null)]
    [InlineData("Azure.Functions.Cli.Workloads.Node", null)]
    public async Task ListInstalledAsync_UnrelatedContent_DoesNotMatchPartialAliasesOrPackageIds(string packageId, string? alias)
    {
        SetEntries(Content(packageId, aliases: alias is null ? [] : [alias]));

        IReadOnlyList<InstalledTemplatesWorkload> result =
            await new InstalledTemplatesWorkloads(_store, _paths).ListInstalledAsync("node", _cancellation.Token);

        result.Should().BeEmpty();
        _paths.DidNotReceive().GetInstallDirectory(Arg.Any<string>(), Arg.Any<string>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ListInstalledAsync_AllNonContentKinds_AreIgnoredEvenWhenIdentityAndAliasMatch(bool hasLogicalOwner)
    {
        WorkloadEntry[] entries = [.. Enum.GetValues<WorkloadKind>()
            .Where(kind => kind != WorkloadKind.Content)
            .Select(kind => new WorkloadEntry
            {
                PackageId = "Azure.Functions.Cli.Workloads.Templates.Node",
                PackageVersion = "1.0.0",
                Kind = kind,
                Aliases = ["node-templates"],
                LogicalPackage = hasLogicalOwner ? Logical("Contoso.Templates", aliases: ["node-templates"]) : null,
            })];
        SetEntries(entries);

        IReadOnlyList<InstalledTemplatesWorkload> result =
            await new InstalledTemplatesWorkloads(_store, _paths).ListInstalledAsync("node", _cancellation.Token);

        result.Should().BeEmpty();
        _paths.DidNotReceive().GetInstallDirectory(Arg.Any<string>(), Arg.Any<string>());
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(false, "")]
    [InlineData(true, "")]
    [InlineData(false, "any")]
    [InlineData(true, "any")]
    [InlineData(false, "AnY")]
    [InlineData(true, "AnY")]
    public async Task ListInstalledAsync_LogicalAlias_UsesPhysicalInstallDirectoryRegardlessOfOwnership(
        bool implicitInstall, string? runtimeIdentifier)
    {
        SetEntries(Content(
            "Contoso.TemplatePayload.win-x64",
            aliases: ["python-templates"],
            logicalPackage: Logical("Contoso.FunctionTemplates", aliases: ["NODE-TEMPLATES"]),
            implicitInstall: implicitInstall,
            runtimeIdentifier: runtimeIdentifier));

        IReadOnlyList<InstalledTemplatesWorkload> result =
            await new InstalledTemplatesWorkloads(_store, _paths).ListInstalledAsync("node", _cancellation.Token);

        result.Should().ContainSingle().Which.Should().Be(new InstalledTemplatesWorkload(
            "node", "1.0.0", Path.Combine("installed", "Contoso.TemplatePayload.win-x64", "1.0.0")));
        _paths.Received(1).GetInstallDirectory("Contoso.TemplatePayload.win-x64", "1.0.0");
        _paths.DidNotReceive().GetInstallDirectory("Contoso.FunctionTemplates", Arg.Any<string>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("python-templates")]
    [InlineData("node-worker")]
    public async Task ListInstalledAsync_LogicalOwnerDoesNotClaimStack_IgnoresPhysicalIdAndAliases(string? logicalAlias)
    {
        SetEntries(Content(
            "Azure.Functions.Cli.Workloads.Templates.Node",
            aliases: ["node-templates"],
            logicalPackage: Logical("Contoso.UnrelatedContent", aliases: logicalAlias is null ? [] : [logicalAlias])));

        IReadOnlyList<InstalledTemplatesWorkload> result =
            await new InstalledTemplatesWorkloads(_store, _paths).ListInstalledAsync("node", _cancellation.Token);

        result.Should().BeEmpty();
        _paths.DidNotReceive().GetInstallDirectory(Arg.Any<string>(), Arg.Any<string>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ListInstalledAsync_DifferentLogicalIdsClaimStack_ThrowsWithAllIdsRegardlessOfOrder(bool reverseOrder)
    {
        WorkloadEntry[] entries =
        [
            Content("Payload.First.win-x64", logicalPackage: Logical("Contoso.Templates", aliases: ["node-templates"])),
            Content("Payload.Second.win-x64", logicalPackage: Logical("Fabrikam.Templates", aliases: ["NODE-TEMPLATES"])),
        ];
        SetEntries(reverseOrder ? [.. entries.Reverse()] : entries);
        InstalledTemplatesWorkloads workloads = new(_store, _paths);

        Func<Task> act = () => workloads.ListInstalledAsync("node", _cancellation.Token);

        InvalidOperationException exception = (await act.Should().ThrowExactlyAsync<InvalidOperationException>()).Which;
        exception.Message.Should().Contain("node-templates")
            .And.Contain("Contoso.Templates")
            .And.Contain("Fabrikam.Templates")
            .And.NotContain("Payload.First.win-x64")
            .And.NotContain("Payload.Second.win-x64");
        _paths.DidNotReceive().GetInstallDirectory(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task ListInstalledAsync_SameLogicalIdAcrossVersionsAndPhysicalIds_PreservesChannelSelection()
    {
        SetEntries(
            Content("Payload.Older", "1.9.0", logicalPackage: Logical("Contoso.Templates", "1.9.0", ["node-templates"])),
            Content("Payload.Newer", "1.10.0", logicalPackage: Logical("CONTOSO.TEMPLATES", "1.10.0", ["node-templates"])),
            Content("Payload.Preview", "2.0.0-preview.1", logicalPackage: Logical("Contoso.Templates", "2.0.0-preview.1", ["node-templates"])),
            Content("Payload.Experimental", "3.0.0-experimental.1", logicalPackage: Logical("Contoso.Templates", "3.0.0-experimental.1", ["node-templates"])),
            Content("Contoso.Templates", "1.8.0", aliases: ["node-templates", "NODE-TEMPLATES"]));

        IReadOnlyList<InstalledTemplatesWorkload> result =
            await new InstalledTemplatesWorkloads(_store, _paths).ListInstalledAsync("node", _cancellation.Token);

        result.Select(row => row.PackageVersion).Should().Equal(
            "1.9.0", "1.10.0", "2.0.0-preview.1", "3.0.0-experimental.1", "1.8.0");
        TemplatesChannelMapper.PickChannelMatched(result, BundleChannel.Stable).Should().Be(new InstalledTemplatesWorkload(
            "node", "1.10.0", Path.Combine("installed", "Payload.Newer", "1.10.0")));
        TemplatesChannelMapper.PickChannelMatched(result, BundleChannel.Preview).Should().Be(new InstalledTemplatesWorkload(
            "node", "2.0.0-preview.1", Path.Combine("installed", "Payload.Preview", "2.0.0-preview.1")));
        TemplatesChannelMapper.PickChannelMatched(result, BundleChannel.Experimental).Should().Be(new InstalledTemplatesWorkload(
            "node", "3.0.0-experimental.1", Path.Combine("installed", "Payload.Experimental", "3.0.0-experimental.1")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("any")]
    [InlineData("AnY")]
    public async Task ListInstalledAsync_PortableContent_PreservesExtractedRootInsteadOfReturningToolsRoot(string? runtimeIdentifier)
    {
        const string installDirectory = "/workloads/templates/1.0.0";
        const string packageId = "Azure.Functions.Cli.Workloads.Templates.Node";
        SetEntries(Content(packageId, runtimeIdentifier: runtimeIdentifier));
        _paths.GetInstallDirectory(packageId, "1.0.0").Returns(installDirectory);

        IReadOnlyList<InstalledTemplatesWorkload> result =
            await new InstalledTemplatesWorkloads(_store, _paths).ListInstalledAsync("node", _cancellation.Token);

        InstalledTemplatesWorkload row = result.Should().ContainSingle().Which;
        row.InstallDirectory.Should().Be(installDirectory)
            .And.NotBe(WorkloadPackageLayout.GetContentRoot(installDirectory, runtimeIdentifier));
    }

    [Theory]
    [InlineData("win-x64", false, false)]
    [InlineData("win-x64", false, true)]
    [InlineData("win-x64", true, false)]
    [InlineData("win-x64", true, true)]
    [InlineData("linux-x64", false, false)]
    [InlineData("linux-x64", false, true)]
    [InlineData("linux-x64", true, false)]
    [InlineData("linux-x64", true, true)]
    [InlineData("osx-arm64", false, false)]
    [InlineData("osx-arm64", false, true)]
    [InlineData("osx-arm64", true, false)]
    [InlineData("osx-arm64", true, true)]
    [InlineData("unknown-rid", false, false)]
    [InlineData(" ", true, false)]
    [InlineData("\t\r\n", false, true)]
    [InlineData("any ", true, true)]
    [InlineData(" any", false, false)]
    public async Task ListInstalledAsync_MatchingNonPortableContent_ThrowsBeforeResolvingInstallDirectory(
        string runtimeIdentifier, bool hasLogicalOwner, bool useLegacyId)
    {
        string packageId = useLegacyId ? TemplatesWorkloadConstants.GetPackageId("node") : "Contoso.FunctionTemplates";
        string physicalId = hasLogicalOwner ? "Contoso.TemplatePayload" : packageId;
        IReadOnlyList<string> aliases = useLegacyId ? [] : ["node-templates"];
        SetEntries(Content(
            physicalId,
            aliases: hasLogicalOwner ? ["python-templates"] : aliases,
            logicalPackage: hasLogicalOwner ? Logical(packageId, aliases: aliases) : null,
            runtimeIdentifier: runtimeIdentifier));
        InstalledTemplatesWorkloads workloads = new(_store, _paths);

        Func<Task> act = () => workloads.ListInstalledAsync("node", _cancellation.Token);

        InvalidOperationException exception = (await act.Should().ThrowExactlyAsync<InvalidOperationException>()).Which;
        exception.Message.Should().Contain(physicalId)
            .And.Contain("node-templates")
            .And.Contain($"unsupported runtime identifier '{runtimeIdentifier}'")
            .And.Contain("tools/any/content");
        await _store.Received(1).GetWorkloadsAsync(_cancellation.Token);
        _paths.DidNotReceive().GetInstallDirectory(Arg.Any<string>(), Arg.Any<string>());
    }

    [Theory]
    [InlineData("win-x64", false)]
    [InlineData("win-x64", true)]
    [InlineData("linux-x64", false)]
    [InlineData("linux-x64", true)]
    [InlineData("osx-arm64", false)]
    [InlineData("osx-arm64", true)]
    public async Task ListInstalledAsync_NonPortableMatchingVersionWithPortableVersion_SelectsPortableRegardlessOfOrder(
        string runtimeIdentifier, bool reverseOrder)
    {
        WorkloadEntry[] entries =
        [
            Content("Contoso.Templates", "2.0.0", aliases: ["node-templates"]),
            Content("Payload.Experimental", "1.0.0-experimental.1",
                logicalPackage: Logical("Contoso.Templates", "1.0.0-experimental.1", ["node-templates"]),
                runtimeIdentifier: runtimeIdentifier),
        ];
        SetEntries(reverseOrder ? [.. entries.Reverse()] : entries);
        InstalledTemplatesWorkloads workloads = new(_store, _paths);

        IReadOnlyList<InstalledTemplatesWorkload> result = await workloads.ListInstalledAsync("node", _cancellation.Token);

        result.Should().ContainSingle().Which.PackageVersion.Should().Be("2.0.0");
        _paths.Received(1).GetInstallDirectory("Contoso.Templates", "2.0.0");
    }

    [Theory]
    [InlineData("win-x64")]
    [InlineData("linux-x64")]
    [InlineData("osx-arm64")]
    public async Task ListInstalledAsync_UnrelatedNonPortableRows_DoNotBlockPortableTemplates(string runtimeIdentifier)
    {
        WorkloadEntry[] nonContentEntries = [.. Enum.GetValues<WorkloadKind>()
            .Where(kind => kind != WorkloadKind.Content)
            .Select(kind => new WorkloadEntry
            {
                PackageId = "Azure.Functions.Cli.Workloads.Templates.Node",
                PackageVersion = "1.0.0",
                Kind = kind,
                Aliases = ["node-templates"],
                RuntimeIdentifier = runtimeIdentifier,
            })];
        SetEntries(
        [
            .. nonContentEntries,
            Content("Unrelated.Templates", aliases: ["python-templates"], runtimeIdentifier: runtimeIdentifier),
            Content("Azure.Functions.Cli.Workloads.Templates.Node", aliases: ["node-templates"],
                logicalPackage: Logical("Unrelated.Owner", aliases: ["python-templates"]),
                runtimeIdentifier: runtimeIdentifier),
            Content("Contoso.Templates", aliases: ["node-templates"]),
        ]);

        IReadOnlyList<InstalledTemplatesWorkload> result =
            await new InstalledTemplatesWorkloads(_store, _paths).ListInstalledAsync("node", _cancellation.Token);

        result.Should().ContainSingle().Which.Should().Be(new InstalledTemplatesWorkload(
            "node", "1.0.0", Path.Combine("installed", "Contoso.Templates", "1.0.0")));
        _paths.Received(1).GetInstallDirectory("Contoso.Templates", "1.0.0");
        _paths.Received(1).GetInstallDirectory(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task ListInstalledAsync_AlreadyCanceled_DoesNotReadStore()
    {
        SetEntries();
        _cancellation.Cancel();
        InstalledTemplatesWorkloads workloads = new(_store, _paths);

        Func<Task> act = () => workloads.ListInstalledAsync("node", _cancellation.Token);

        OperationCanceledException exception = (await act.Should().ThrowAsync<OperationCanceledException>()).Which;
        exception.CancellationToken.Should().Be(_cancellation.Token);
        await _store.DidNotReceive().GetWorkloadsAsync(Arg.Any<CancellationToken>());
        _paths.DidNotReceive().GetInstallDirectory(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task ListInstalledAsync_CanceledWhileStoreReturnsEmpty_ThrowsCancellation()
    {
        _store.GetWorkloadsAsync(_cancellation.Token).Returns(_ =>
        {
            _cancellation.Cancel();
            return Task.FromResult<IReadOnlyList<WorkloadEntry>>([]);
        });
        InstalledTemplatesWorkloads workloads = new(_store, _paths);

        Func<Task> act = () => workloads.ListInstalledAsync("node", _cancellation.Token);

        OperationCanceledException exception = (await act.Should().ThrowAsync<OperationCanceledException>()).Which;
        exception.CancellationToken.Should().Be(_cancellation.Token);
        await _store.Received(1).GetWorkloadsAsync(_cancellation.Token);
        _paths.DidNotReceive().GetInstallDirectory(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task ListInstalledAsync_StoreFailure_PropagatesUnchanged()
    {
        IOException failure = new("Registry unavailable.");
        _store.GetWorkloadsAsync(_cancellation.Token).Returns(Task.FromException<IReadOnlyList<WorkloadEntry>>(failure));
        InstalledTemplatesWorkloads workloads = new(_store, _paths);

        Func<Task> act = () => workloads.ListInstalledAsync("node", _cancellation.Token);

        (await act.Should().ThrowExactlyAsync<IOException>()).Which.Should().BeSameAs(failure);
        _paths.DidNotReceive().GetInstallDirectory(Arg.Any<string>(), Arg.Any<string>());
    }

    private void SetEntries(params WorkloadEntry[] entries)
        => _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyList<WorkloadEntry>>(entries);

    private static WorkloadEntry Content(
        string packageId,
        string version = "1.0.0",
        IReadOnlyList<string>? aliases = null,
        LogicalPackage? logicalPackage = null,
        bool implicitInstall = false,
        string? runtimeIdentifier = null)
        => new()
        {
            PackageId = packageId,
            PackageVersion = version,
            Kind = WorkloadKind.Content,
            Aliases = aliases ?? [],
            LogicalPackage = logicalPackage,
            IsImplicitlyInstalled = implicitInstall,
            RuntimeIdentifier = runtimeIdentifier,
        };

    private static LogicalPackage Logical(string packageId, string version = "1.0.0", IReadOnlyList<string>? aliases = null)
        => new()
        {
            PackageId = packageId,
            PackageVersion = version,
            Aliases = aliases ?? [],
        };
}
