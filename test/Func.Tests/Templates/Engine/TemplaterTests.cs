// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.TemplateFiltering;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using Microsoft.TemplateEngine.Edge.Settings;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class TemplaterTests : IDisposable
{
    private readonly string _root;
    private readonly string _hive;

    public TemplaterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "func-templater-tests-" + Guid.NewGuid().ToString("N"));
        _hive = Path.Combine(_root, "hive");
        Directory.CreateDirectory(_hive);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best-effort cleanup of the temp hive
        }
    }

    [Fact]
    public async Task ListAsync_ReturnsInstalledTemplates()
    {
        string packageDirectory = TemplateFixtures.WriteBasicPackage(_root);
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using Templater templater = CreateTemplater();

        TemplateResolutionResult result = await templater.ListAsync(CancellationToken.None);

        result.Templates.Select(template => template.Identity).Should().BeEquivalentTo(
        [
            TemplateFixtures.ItemTemplateIdentity,
            TemplateFixtures.ProjectTemplateIdentity,
            TemplateFixtures.OtherTypeTemplateIdentity,
            TemplateFixtures.UntypedTemplateIdentity,
        ]);
        result.Templates.Should().AllSatisfy(template => template.Template.Identity.Should().Be(template.Identity));
    }

    [Fact]
    public async Task ListProjectTemplatesAsync_UsesEngineTypeAndLanguageFilters()
    {
        string packageDirectory = TemplateFixtures.WriteBasicPackage(_root);
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using Templater templater = CreateTemplater();

        TemplateResolutionResult result = await templater.ListProjectTemplatesAsync("python", CancellationToken.None);

        result.Templates.Select(template => template.Identity).Should().Equal(TemplateFixtures.ProjectTemplateIdentity);
    }

    [Fact]
    public async Task ListProjectTemplatesAsync_ReturnsMultipleApplicableTemplates()
    {
        string packageDirectory = TemplateFixtures.WriteInitProjectPackage(_root);
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using Templater templater = CreateTemplater();

        TemplateResolutionResult result = await templater.ListProjectTemplatesAsync("python", CancellationToken.None);

        result.Templates.Select(template => template.Identity).Should().BeEquivalentTo(
            TemplateFixtures.InitProjectTemplateIdentity,
            $"{TemplateFixtures.InitProjectTemplateIdentity}.Alternative");
    }

    [Fact]
    public async Task ListProjectTemplatesAsync_KeepsEachCandidatesNativeMatchDetails()
    {
        string packageDirectory = TemplateFixtures.WriteInitProjectPackage(_root);
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using Templater templater = CreateTemplater();

        TemplateResolutionResult result = await templater.ListProjectTemplatesAsync("python", CancellationToken.None);

        result.Templates.Should().HaveCount(2);
        result.Matches.Should().HaveCount(2);
        result.Matches.Values.Should().OnlyHaveUniqueItems();
        result.Templates.Should().AllSatisfy(template =>
        {
            result.Matches.Should().ContainKey(template);
            ITemplateMatchInfo match = result.Matches[template];
            match.Info.Should().BeSameAs(template.Template);
            match.MatchDisposition.Should().Contain(info => info.Name == MatchInfo.BuiltIn.Type && info.Kind == MatchKind.Exact);
            match.MatchDisposition.Should().Contain(info => info.Name == MatchInfo.BuiltIn.Language && info.Kind == MatchKind.Exact);
        });
    }

    [Fact]
    public async Task ListAsync_UnfilteredCandidatesHaveNoMatchDetails()
    {
        string packageDirectory = TemplateFixtures.WriteBasicPackage(_root);
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using Templater templater = CreateTemplater();

        TemplateResolutionResult result = await templater.ListAsync(CancellationToken.None);

        result.Templates.Should().NotBeEmpty();
        result.Matches.Should().BeEmpty();
    }

    [Fact]
    public async Task ListAsync_NewWorkload_IsDiscoveredByNextTemplater()
    {
        string firstRoot = TemplateFixtures.WriteItemPackage(Path.Combine(_root, "first"), "First.Item", "First");
        string secondRoot = TemplateFixtures.WriteItemPackage(Path.Combine(_root, "second"), "Second.Item", "Second");
        RuntimeWorkloadInfo firstWorkload = CreateWorkload(firstRoot);
        RuntimeWorkloadInfo secondWorkload = CreateWorkload(secondRoot, packageId: "Test.Workload.B");
        var provider = Substitute.For<IWorkloadProvider>();
        provider.GetRuntimeWorkloads().Returns([firstWorkload]);

        using (Templater first = CreateWorkloadTemplater(provider))
        {
            (await first.ListAsync(CancellationToken.None)).Templates.Should().ContainSingle().Which.Identity.Should().Be("First.Item");
            string cachePath = Path.Combine(first.Settings.Paths.HostVersionSettingsDir, "templatecache.json");
            File.Exists(cachePath).Should().BeTrue();

            provider.GetRuntimeWorkloads().Returns([firstWorkload, secondWorkload]);
            (await first.ListAsync(CancellationToken.None)).Templates.Should().ContainSingle().Which.Identity.Should().Be("First.Item");
        }

        using Templater next = CreateWorkloadTemplater(provider);
        TemplateResolutionResult result = await next.ListAsync(CancellationToken.None);

        result.Templates.Select(template => template.Identity).Should().BeEquivalentTo(["First.Item", "Second.Item"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ListAsync_RemovedWorkloadRoot_DropsCachedTemplates(bool deleteDirectory)
    {
        string root = TemplateFixtures.WriteItemPackage(_root, "Test.Item", "Item");
        var provider = Substitute.For<IWorkloadProvider>();
        provider.GetRuntimeWorkloads().Returns([CreateWorkload(root)]);
        using (Templater first = CreateWorkloadTemplater(provider))
        {
            (await first.ListAsync(CancellationToken.None)).Templates.Should().ContainSingle();
        }

        if (deleteDirectory)
        {
            Directory.Delete(root, recursive: true);
        }
        else
        {
            provider.GetRuntimeWorkloads().Returns([]);
        }

        using Templater next = CreateWorkloadTemplater(provider);
        TemplateResolutionResult result = await next.ListAsync(CancellationToken.None);

        result.Templates.Should().BeEmpty();
    }

    [Theory]
    [InlineData(-1, "Original")]
    [InlineData(0, "Original")]
    [InlineData(1, "Updated")]
    public async Task ListAsync_SameVersionWorkload_RefreshesOnlyWhenTimestampAdvances(int timestampOffsetDays, string expectedName)
    {
        string root = TemplateFixtures.WriteItemPackage(_root, "Test.Item", "Original");
        string metadataPath = Path.Combine(_root, "workload.json");
        File.WriteAllText(metadataPath, "{}");
        DateTime timestamp = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Directory.SetLastWriteTimeUtc(root, timestamp);
        File.SetLastWriteTimeUtc(metadataPath, timestamp);
        var provider = Substitute.For<IWorkloadProvider>();
        provider.GetRuntimeWorkloads().Returns([CreateWorkload(root)]);
        using (Templater first = CreateWorkloadTemplater(provider))
        {
            (await first.ListAsync(CancellationToken.None)).Templates.Should().ContainSingle().Which.Name.Should().Be("Original");
        }

        TemplateFixtures.WriteItemPackage(_root, "Test.Item", "Updated");
        Directory.SetLastWriteTimeUtc(root, timestamp.AddDays(Math.Min(timestampOffsetDays, 0)));
        File.SetLastWriteTimeUtc(metadataPath, timestamp.AddDays(timestampOffsetDays));
        using Templater next = CreateWorkloadTemplater(provider);
        TemplateResolutionResult result = await next.ListAsync(CancellationToken.None);

        result.Templates.Should().ContainSingle().Which.Name.Should().Be(expectedName);
    }

    [Fact]
    public async Task ListAsync_ChangedActiveWorkload_UsesNewRootDespiteRetainedOldPayload()
    {
        string oldRoot = TemplateFixtures.WriteItemPackage(Path.Combine(_root, "v1"), "Test.Item", "Old");
        string newRoot = TemplateFixtures.WriteItemPackage(Path.Combine(_root, "v2"), "Test.Item", "New");
        var provider = Substitute.For<IWorkloadProvider>();
        provider.GetRuntimeWorkloads().Returns([CreateWorkload(oldRoot)]);
        using (Templater first = CreateWorkloadTemplater(provider))
        {
            (await first.ListAsync(CancellationToken.None)).Templates.Should().ContainSingle().Which.Name.Should().Be("Old");
        }

        provider.GetRuntimeWorkloads().Returns([CreateWorkload(newRoot, version: "2.0.0")]);
        using Templater next = CreateWorkloadTemplater(provider);
        TemplateResolutionResult result = await next.ListAsync(CancellationToken.None);

        FuncTemplateInfo template = result.Templates.Should().ContainSingle().Subject;
        template.Name.Should().Be("New");
        template.MountPointUri.Should().Be(newRoot);
        Directory.Exists(oldRoot).Should().BeTrue();
    }

    [Fact]
    public async Task ListAsync_DuplicateIdentity_UsesSourceOrderNotPackageVersion()
    {
        string newerRoot = TemplateFixtures.WriteItemPackage(Path.Combine(_root, "newer"), "Test.Item", "Newer");
        string olderRoot = TemplateFixtures.WriteItemPackage(Path.Combine(_root, "older"), "Test.Item", "Older");
        var provider = Substitute.For<IWorkloadProvider>();
        provider.GetRuntimeWorkloads().Returns(
        [
            CreateWorkload(newerRoot, version: "9.0.0"),
            CreateWorkload(olderRoot, packageId: "Test.Workload.B"),
        ]);
        using Templater templater = CreateWorkloadTemplater(provider);

        TemplateResolutionResult result = await templater.ListAsync(CancellationToken.None);

        FuncTemplateInfo template = result.Templates.Should().ContainSingle().Subject;
        template.Name.Should().Be("Older");
        template.MountPointUri.Should().Be(olderRoot);
    }

    [Fact]
    public async Task ListAsync_ManagedTemplates_UseNativePriorityOverWorkloadTemplates()
    {
        string root = TemplateFixtures.WriteItemPackage(
            Path.Combine(_root, "workload"), TemplateFixtures.ItemTemplateIdentity, "Workload item");
        var provider = Substitute.For<IWorkloadProvider>();
        provider.GetRuntimeWorkloads().Returns([CreateWorkload(root)]);
        using (Templater first = CreateWorkloadTemplater(provider))
        {
            (await first.ListAsync(CancellationToken.None)).Templates.Should().ContainSingle().Which.Name.Should().Be("Workload item");
        }

        string managedRoot = TemplateFixtures.WriteBasicPackage(Path.Combine(_root, "managed"));
        await TemplateFixtures.InstallAsync(managedRoot, _hive);
        using Templater next = CreateWorkloadTemplater(provider);
        TemplateResolutionResult result = await next.ListAsync(CancellationToken.None);

        result.Templates.Should().HaveCount(4);
        FuncTemplateInfo item = result.Templates.Should()
            .ContainSingle(template => template.Identity == TemplateFixtures.ItemTemplateIdentity).Subject;
        item.Name.Should().Be("Func Tests Item");
        item.MountPointUri.Should().Be(managedRoot);
    }

    [Fact]
    public async Task ListAsync_LoadsFuncHostDataOnDiscoveryAndCacheReload()
    {
        string packageDirectory = TemplateFixtures.WriteBasicPackage(_root);
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);

        for (int attempt = 0; attempt < 2; attempt++)
        {
            using Templater templater = CreateTemplater();
            TemplateResolutionResult result = await templater.ListAsync(CancellationToken.None);

            FuncTemplateInfo item = result.Templates.Single(template => template.Identity == TemplateFixtures.ItemTemplateIdentity);
            item.HostConfigPlace.Should().EndWith("func.host.json");
            item.Template.Should().BeAssignableTo<ITemplateInfoHostJsonCache>()
                .Which.HostData.Should().NotBeNullOrWhiteSpace();
            item.HostData.SymbolInfo["AuthorizationLevel"].LongName.Should().Be("auth-level");
            item.HostData.SymbolInfo["AuthorizationLevel"].ShortName.Should().Be("a");
            item.HostData.UsageExamples.Should().Equal("func new --auth-level anonymous");
            item.HostData.IsHidden.Should().BeFalse();
            result.Templates.Single(template => template.Identity == TemplateFixtures.ProjectTemplateIdentity)
                .HostData.SymbolInfo.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task ListAsync_InvalidFuncHostFile_ThrowsInsteadOfUsingEmptyMetadata()
    {
        string packageDirectory = TemplateFixtures.WriteBasicPackage(_root, itemHostJson: "{");
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using Templater templater = CreateTemplater();

        Func<Task> act = () => templater.ListAsync(CancellationToken.None);

        await act.Should().ThrowAsync<JsonException>().WithMessage($"*{TemplateFixtures.ItemTemplateIdentity}*func.host.json*");
    }

    [Fact]
    public async Task ListAsync_CanceledToken_ThrowsOperationCanceledException()
    {
        using Templater templater = CreateTemplater();
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Func<Task> act = () => templater.ListAsync(cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ListAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        Templater templater = CreateTemplater();
        templater.Dispose();

        Func<Task> act = () => templater.ListAsync(CancellationToken.None);

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public void Constructor_NullContext_Throws()
    {
        Action act = () => _ = new Templater(null!, _hive, []);

        act.Should().Throw<ArgumentNullException>().WithParameterName("context");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_BlankSettingsLocation_Throws(string settingsLocation)
    {
        Action act = () => _ = new Templater(Context(), settingsLocation, []);

        act.Should().Throw<ArgumentException>().WithParameterName("settingsLocation");
    }

    [Fact]
    public void Constructor_NullComponents_Throws()
    {
        Action act = () => _ = new Templater(Context(), _hive, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("components");
    }

    [Fact]
    public void Settings_UseTheSuppliedSettingsLocation()
    {
        using Templater templater = CreateTemplater();

        templater.Settings.Paths.GlobalSettingsDir.Should().Be(_hive);
    }

    [Fact]
    public void Settings_RegisterRunnableProjectGenerators()
    {
        using Templater templater = CreateTemplater();

        templater.Settings.Components.OfType<IGenerator>().Should().NotBeEmpty();
    }

    [Fact]
    public void Constructor_RegistersSuppliedComponents()
    {
        TestComponent component = new();
        using Templater templater = new(Context(), _hive, [new TestTemplateComponent<ITestComponent>(component)]);

        templater.Settings.Components.OfType<ITestComponent>().Should().ContainSingle().Which.Should().BeSameAs(component);
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        Templater templater = CreateTemplater();
        templater.Dispose();

        Action act = templater.Dispose;

        act.Should().NotThrow();
    }

    private TemplateEngineContext Context() => new(WorkingDirectory.FromExplicit(_root));

    private Templater CreateTemplater() => new(Context(), _hive, []);

    private Templater CreateWorkloadTemplater(IWorkloadProvider provider)
    {
        WorkloadTemplateSource source = new(provider, new PhysicalFileSystem());
        WorkloadTemplatePackageProviderFactory factory = new(source);
        return new(Context(), _hive, [new TestTemplateComponent<ITemplatePackageProviderFactory>(factory)]);
    }

    private static RuntimeWorkloadInfo CreateWorkload(string templateRoot, string packageId = "Test.Workload.A", string version = "1.0.0")
        => TestWorkloads.CreateInfo(packageId, version) with
        {
            InstallDirectory = Path.GetDirectoryName(templateRoot)!,
            TemplateRoot = templateRoot,
        };

    private interface ITestComponent : IIdentifiedComponent
    {
    }

    private sealed class TestComponent : ITestComponent, IIdentifiedComponent
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    private sealed class TestTemplateComponent<TInterface>(TInterface instance) : ITemplateComponent
        where TInterface : IIdentifiedComponent
    {
        public Type InterfaceType => typeof(TInterface);

        public IIdentifiedComponent Instance { get; } = instance;
    }
}
