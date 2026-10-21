// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Profiles;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Tests.Templates.Acquisition;
using Azure.Functions.Cli.Workloads.Catalog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    [Fact]
    public async Task NativeSetupRunner_TransfersActualSelectedStack_ReportsMissingThenAcquiredThenVerifiedExternalRole()
    {
        var entry = await InstallAssociationStackAsync();
        using var external = new BasicAcquisitionFixture();
        var (acquisition, _) = NativeAssociationRunner(external, entry);
        var support = new NativeTemplateSetupSupport(acquisition, AssemblyCliVersionProvider.Instance);
        var legacyCatalog = Substitute.For<IWorkloadCatalog>();
        var dependencies = new SetupDependencyInstaller(_interaction, _workloads, legacyCatalog, _installer, support);
        var runner = NativeSetupRunner(dependencies);

        (await runner.RunAsync(NativeSetupOptions(check: true), CancellationToken.None)).ExitCode.Should().Be(1);
        var missing = NativeSetupResult();
        missing.GetProperty("status").GetString().Should().Be("failed");
        missing.GetProperty("template_delivery").GetString().Should().Be("external-native");
        missing.TryGetProperty("package_id", out _).Should().BeFalse();
        missing.GetProperty("completed").GetArrayLength().Should().Be(1);
        missing.GetProperty("completed")[0].GetProperty("role").GetString().Should().Be("selected-stack");
        external.Payload.Opened.Should().BeEmpty();

        (await runner.RunAsync(NativeSetupOptions(check: false), CancellationToken.None)).ExitCode.Should().Be(0);
        var installed = NativeSetupResult();
        installed.GetProperty("status").GetString().Should().Be("installed");
        installed.GetProperty("package_id").GetString().Should().Be(BasicAcquisitionFixture.BasicId);
        installed.GetProperty("version").GetString().Should().Be("1.0.0");
        installed.GetProperty("completed")[1].GetProperty("role").GetString().Should().Be("external-native");
        installed.GetProperty("completed")[1].GetProperty("reused").GetBoolean().Should().BeFalse();

        (await runner.RunAsync(NativeSetupOptions(check: true), CancellationToken.None)).ExitCode.Should().Be(0);
        NativeSetupResult().GetProperty("status").GetString().Should().Be("satisfied");
        external.Payload.Opened.Should().Equal("1.0.0");
        legacyCatalog.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task NativeSetupRunner_ActualEmptyProductionAuthority_FailsWithoutLegacyCatalogOrSuccess()
    {
        var entry = await InstallAssociationStackAsync();
        using var external = new BasicAcquisitionFixture();
        var (acquisition, _) = NativeAssociationRunner(external, entry,
            authority: new BundledBasicCompanionCatalog(new BasicCompanionManifestReader()));
        var support = new NativeTemplateSetupSupport(acquisition, AssemblyCliVersionProvider.Instance);
        var legacyCatalog = Substitute.For<IWorkloadCatalog>();
        var dependencies = new SetupDependencyInstaller(_interaction, _workloads, legacyCatalog, _installer, support);

        var result = await NativeSetupRunner(dependencies).RunAsync(NativeSetupOptions(check: false), CancellationToken.None);

        result.ExitCode.Should().Be(1);
        var output = NativeSetupResult();
        output.GetProperty("status").GetString().Should().Be("failed");
        output.GetProperty("native_failure").GetString().Should().Be(nameof(BasicAcquisitionFailure.MissingMapping));
        output.GetProperty("association_revision").GetString().Should().Be("unqualified");
        output.GetProperty("completed").GetArrayLength().Should().Be(0);
        legacyCatalog.ReceivedCalls().Should().BeEmpty();
        external.Payload.Opened.Should().BeEmpty();
        _interaction.Lines.Should().NotContain(line => line.Contains("\"type\":\"setup.completed\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NativeSetupDependency_MissingSupport_DoesNotUseOptionalVersionlessLegacyFallback()
    {
        var legacyCatalog = Substitute.For<IWorkloadCatalog>();
        var dependencies = new SetupDependencyInstaller(_interaction, _workloads, legacyCatalog, _installer);

        var result = await dependencies.EnsureDependencyAsync(NativeSetupOptions(check: true),
            SetupDependency.NativeTemplates("node", BundleChannel.Stable), CancellationToken.None);

        result.Status.Should().Be(SetupDependencyStatus.Failed);
        result.Message.Should().Contain("No optional or versionless legacy");
        result.PackageId.Should().BeNull();
        legacyCatalog.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task NativeSetupRegistration_BindsActualNativeHostVersion_NotVersionCommandOverride()
    {
        var entry = await InstallAssociationStackAsync();
        using var external = new BasicAcquisitionFixture();
        var (acquisition, _) = NativeAssociationRunner(external, entry);
        var fakeVersion = Substitute.For<ICliVersionProvider>();
        fakeVersion.Version.Returns("unknown");
        ServiceCollection services = new();
        services.AddNativeTemplateSetup();
        services.AddSingleton(acquisition);
        services.AddSingleton(fakeVersion);
        using var provider = services.BuildServiceProvider();
        var support = provider.GetRequiredService<NativeTemplateSetupSupport>();

        var result = await support.EnsureAsync(NativeSetupOptions(check: true),
            SetupDependency.NativeTemplates("node", BundleChannel.Stable) with { SelectedStack = entry }, CancellationToken.None);

        result.Status.Should().Be(BasicAcquisitionStatus.Incomplete);
        result.Failure.Should().BeNull();
        external.Payload.Opened.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeSetupPlan_ExplicitComposition_ProducesRequiredNativeDependencyWithoutChangingLegacyDefault(bool enabled)
    {
        using var external = new BasicAcquisitionFixture();
        var support = new NativeTemplateSetupSupport(external.Runner(), AssemblyCliVersionProvider.Instance);
        var bundleReader = Substitute.For<IHostJsonBundleSectionReader>();
        bundleReader.ReadAsync(Arg.Any<DirectoryInfo>(), Arg.Any<CancellationToken>()).Returns((HostJsonBundleSection?)null);
        var builder = new SetupDependencyPlanBuilder(bundleReader, enabled ? support : null);

        var plan = await builder.BuildDependencyPlanAsync(new(_root), new(["node"], [new("node", "node", false)], [], false),
            SetupProfileScope.Unconstrained, CancellationToken.None);

        var templates = plan.Dependencies.Single(dependency => dependency.Kind == SetupDependencyKind.Templates);
        templates.TemplateDelivery.Should().Be(enabled ? SetupTemplateDelivery.ExternalNative : SetupTemplateDelivery.LegacyWorkload);
        templates.Optional.Should().Be(!enabled);
        templates.PackageId.Should().Be(enabled ? string.Empty : "Azure.Functions.Cli.Workloads.Templates.Node");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeSetupRunner_CancellationAfterVerifiedResult_ReportsActualCompletionWithoutStartingNextDependency(bool afterNative)
    {
        var entry = await InstallAssociationStackAsync();
        using var external = new BasicAcquisitionFixture();
        var (acquisition, _) = NativeAssociationRunner(external, entry);
        var support = new NativeTemplateSetupSupport(acquisition, AssemblyCliVersionProvider.Instance);
        var legacyCatalog = Substitute.For<IWorkloadCatalog>();
        var dependencies = new SetupDependencyInstaller(_interaction, _workloads, legacyCatalog, _installer, support);
        using var cancellation = new CancellationTokenSource();
        var runner = NativeSetupRunner(new NativeSetupCancelBoundary(dependencies, cancellation, afterNative), appendSentinel: true);

        Func<Task> run = () => runner.RunAsync(NativeSetupOptions(check: false), cancellation.Token);

        await run.Should().ThrowAsync<OperationCanceledException>();
        var events = _interaction.Lines.Where(line => line.StartsWith('{')).Select(ParseNativeSetupEvent).ToArray();
        events.Should().Contain(item => item.GetProperty("type").GetString() == "dependency.result"
            && item.GetProperty("dependency_type").GetString() == (afterNative ? "templates" : "stack"));
        events.Should().NotContain(item => item.GetProperty("type").GetString() == "setup.completed");
        external.Payload.Opened.Should().HaveCount(afterNative ? 1 : 0);
        legacyCatalog.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task NativeSetupSupport_NullOrMissingBindings_FailBeforeAcquisition()
    {
        using var external = new BasicAcquisitionFixture();
        Action missingAcquisition = () => new NativeTemplateSetupSupport(null!, AssemblyCliVersionProvider.Instance);
        Action missingHost = () => new NativeTemplateSetupSupport(external.Runner(), null!);
        Action missingServices = () => NativeTemplateSetupRegistration.AddNativeTemplateSetup(null!);
        Action missingStackName = () => SetupDependency.NativeTemplates("", BundleChannel.Stable);
        missingAcquisition.Should().Throw<ArgumentNullException>();
        missingHost.Should().Throw<ArgumentNullException>();
        missingServices.Should().Throw<ArgumentNullException>();
        missingStackName.Should().Throw<ArgumentException>();
        var support = new NativeTemplateSetupSupport(external.Runner(), AssemblyCliVersionProvider.Instance);

        var result = await support.EnsureAsync(NativeSetupOptions(check: true),
            SetupDependency.NativeTemplates("node", BundleChannel.Stable), CancellationToken.None);

        result.Failure.Should().Be(BasicAcquisitionFailure.MissingMapping);
        external.Payload.Opened.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeSetupPlan_ProfileMayNarrowSupportedRuntimesWithoutGrantingTemplateAuthority(bool allowsNode)
    {
        using var external = new BasicAcquisitionFixture();
        var support = new NativeTemplateSetupSupport(external.Runner(), AssemblyCliVersionProvider.Instance);
        var reader = Substitute.For<IHostJsonBundleSectionReader>();
        reader.ReadAsync(Arg.Any<DirectoryInfo>(), Arg.Any<CancellationToken>()).Returns((HostJsonBundleSection?)null);
        var profile = new ResolvedProfile("scoped", new(ProfileSourceKind.User, "test-only"), null, ProfileStatus.Stable,
            null, VersionRange.Parse("[4.0.0,5.0.0)"), new Dictionary<string, VersionRange>(), null,
            [allowsNode ? "node" : "python"], null);
        var builder = new SetupDependencyPlanBuilder(reader, support);

        var plan = await builder.BuildDependencyPlanAsync(new(_root), new(["node"], [new("node", "node", false)], [], false),
            new(profile), CancellationToken.None);

        plan.Dependencies.Single(dependency => dependency.Kind == SetupDependencyKind.Host).VersionRange.Should().BeSameAs(profile.HostVersionRange);
        if (allowsNode)
        {
            plan.Failures.Should().BeEmpty();
            plan.Dependencies.Should().Contain(dependency => dependency.TemplateDelivery == SetupTemplateDelivery.ExternalNative && !dependency.Optional);
        }
        else
        {
            plan.Failures.Should().ContainSingle().Which.Message.Should().Contain("does not support runtime 'node'");
            plan.Dependencies.Should().NotContain(dependency => dependency.Kind == SetupDependencyKind.Stack
                || dependency.TemplateDelivery == SetupTemplateDelivery.ExternalNative);
        }
    }

    [Fact]
    public void NativeSetupComposition_IsNotEnabledByDefaultBuiltInRegistration()
    {
        ServiceCollection services = new();

        services.AddBuiltInCommands();
        services.AddWorkloadInstaller();

        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(NativeTemplateSetupSupport)
            || descriptor.ServiceType == typeof(IBasicCompanionCatalog) || descriptor.ServiceType == typeof(INativeTemplateOwnership));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeSetupSupport_EffectiveConfiguredSource_IsNotSilentlyReplacedByStoredOrigin(bool explicitSource)
    {
        var entry = await InstallAssociationStackAsync();
        using var external = new BasicAcquisitionFixture();
        var sources = new PackageSourceProvider(Options.Create(new WorkloadCatalogOptions { Source = "https://other.invalid/v3/index.json" }));
        var (acquisition, _) = NativeAssociationRunner(external, entry, sources: sources);
        var support = new NativeTemplateSetupSupport(acquisition, AssemblyCliVersionProvider.Instance);
        var options = NativeSetupOptions(check: true) with { Source = explicitSource ? BasicAcquisitionFixture.Source : null };

        var result = await support.EnsureAsync(options,
            SetupDependency.NativeTemplates("node", BundleChannel.Stable) with { SelectedStack = entry }, CancellationToken.None);

        if (explicitSource)
        {
            result.Status.Should().Be(BasicAcquisitionStatus.Incomplete);
            result.Failure.Should().BeNull();
            result.Source.Should().Be(BasicAcquisitionFixture.Source);
        }
        else
        {
            result.Status.Should().Be(BasicAcquisitionStatus.Rejected);
            result.Failure.Should().Be(BasicAcquisitionFailure.SourceConflict);
            result.Source.Should().Be("https://other.invalid/v3/index.json");
        }
        external.Payload.Opened.Should().BeEmpty();
    }

    [Fact]
    public async Task NativeSetupRunner_LaterCatalogProvenanceFailure_ReportsOnlyActuallyCompletedExternalArtifacts()
    {
        var entry = await InstallAssociationStackAsync();
        using var external = new BasicAcquisitionFixture();
        var additional = AddNativeProjectFixture(external);
        var receiptFiles = Substitute.For<IFileSystem>();
        receiptFiles.ReadAllTextIfExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => _files.ReadAllTextIfExistsAsync(call.Arg<string>(), call.Arg<CancellationToken>()));
        receiptFiles.WriteAllTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<string>(0).EndsWith("fixture.node.additionalproject.json", StringComparison.Ordinal)
                ? Task.FromException(new IOException("Owned later catalog provenance failure."))
                : _files.WriteAllTextAsync(call.ArgAt<string>(0), call.ArgAt<string>(1), call.Arg<CancellationToken>()));
        var ownership = new NativeTemplateOwnership(receiptFiles, new(Home));
        var (acquisition, _) = NativeAssociationRunner(external, entry, additionalProject: additional, ownership: ownership);
        var support = new NativeTemplateSetupSupport(acquisition, AssemblyCliVersionProvider.Instance);
        var legacyCatalog = Substitute.For<IWorkloadCatalog>();
        var installer = new SetupDependencyInstaller(_interaction, _workloads, legacyCatalog, _installer, support);
        string registry = File.ReadAllText(_paths.WorkloadRegistryPath);

        var result = await NativeSetupRunner(installer).RunAsync(NativeSetupOptions(check: false), CancellationToken.None);

        result.ExitCode.Should().Be(1);
        var output = NativeSetupResult();
        output.GetProperty("status").GetString().Should().Be("failed");
        output.GetProperty("completed").GetArrayLength().Should().Be(2);
        output.GetProperty("completed")[1].GetProperty("package_id").GetString().Should().Be(BasicAcquisitionFixture.BasicId);
        output.GetProperty("completed").EnumerateArray().Should()
            .NotContain(item => item.GetProperty("package_id").GetString() == additional.PackageId);
        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(registry);
        legacyCatalog.ReceivedCalls().Should().BeEmpty();
        using var reader = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        var packages = await reader.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None);
        packages.Should().HaveCount(2).And.NotContain(package => package.Identifier == additional.PackageId);
    }

    private SetupCommandOptions NativeSetupOptions(bool check)
        => new(new(_root), ["node"], [], null, SetupInstallPolicy.IfNeeded, true, true, true, check, SetupOutputMode.Json);

    private SetupRunner NativeSetupRunner(ISetupDependencyInstaller installer, bool appendSentinel = false)
    {
        var features = Substitute.For<ISetupFeatureResolver>();
        features.ResolveFeaturesAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<CancellationToken>())
            .Returns(new SetupFeaturePlan(["node"], [], [], false));
        var profiles = Substitute.For<ISetupProfileScopeResolver>();
        profiles.ResolveProfileScopesAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<SetupRenderer>(), Arg.Any<CancellationToken>())
            .Returns([SetupProfileScope.Unconstrained]);
        var plan = Substitute.For<ISetupDependencyPlanBuilder>();
        IReadOnlyList<SetupDependency> dependencies = appendSentinel
            ? [SetupDependency.Stack("node"), SetupDependency.NativeTemplates("node", BundleChannel.Stable), SetupDependency.Host(null)]
            : [SetupDependency.Stack("node"), SetupDependency.NativeTemplates("node", BundleChannel.Stable)];
        plan.BuildDependencyPlanAsync(Arg.Any<DirectoryInfo>(), Arg.Any<SetupFeaturePlan>(), Arg.Any<SetupProfileScope>(), Arg.Any<CancellationToken>())
            .Returns(new SetupDependencyPlan(dependencies, []));
        return new(_interaction, features, profiles, plan, installer);
    }

    private JsonElement NativeSetupResult()
        => _interaction.Lines.Where(line => line.StartsWith('{'))
            .Select(ParseNativeSetupEvent)
            .Last(item => item.GetProperty("type").GetString() == "dependency.result"
                && item.GetProperty("dependency_type").GetString() == "templates");

    private static JsonElement ParseNativeSetupEvent(string line)
    {
        using var document = JsonDocument.Parse(line);
        return document.RootElement.Clone();
    }

    private sealed class NativeSetupCancelBoundary(ISetupDependencyInstaller inner, CancellationTokenSource cancellation, bool afterNative)
        : ISetupDependencyInstaller
    {
        public async Task<SetupDependencyResult> EnsureDependencyAsync(SetupCommandOptions options, SetupDependency dependency,
            CancellationToken cancellationToken)
        {
            var result = await inner.EnsureDependencyAsync(options, dependency, cancellationToken);
            if (afterNative ? dependency.TemplateDelivery == SetupTemplateDelivery.ExternalNative : dependency.Kind == SetupDependencyKind.Stack)
            {
                cancellation.Cancel();
            }
            return result;
        }
    }
}
