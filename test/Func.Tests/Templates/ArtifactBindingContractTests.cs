// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Quickstart;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads.Storage;
using Azure.Functions.Cli.Workloads.Install;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class ArtifactBindingContractTests
{
    [Theory]
    [InlineData("bundles")]
    [InlineData("workloads")]
    [InlineData("fileSystem")]
    public void BundledVerifier_NullDependency_RejectsProgrammerError(string dependency)
    {
        Action construct = () => _ = new BundledBasicArtifactVerifier(
            dependency == "bundles" ? null! : Substitute.For<IBundledTemplateInstaller>(),
            dependency == "workloads" ? null! : Substitute.For<IWorkloadStore>(),
            dependency == "fileSystem" ? null! : Substitute.For<IFileSystem>());

        construct.Should().Throw<ArgumentNullException>().WithParameterName(dependency);
    }

    [Fact]
    public void BundledVerifier_NullSnapshotInputs_RejectsProgrammerError()
    {
        var verifier = new BundledBasicArtifactVerifier(Substitute.For<IBundledTemplateInstaller>(),
            Substitute.For<IWorkloadStore>(), Substitute.For<IFileSystem>());
        Action entries = () => verifier.ForInventory(null!, new Dictionary<NativeTemplateOwnerIdentity, string>());
        Action directories = () => verifier.ForInventory([], null!);

        entries.Should().Throw<ArgumentNullException>().WithParameterName("entries");
        directories.Should().Throw<ArgumentNullException>().WithParameterName("runtimeDirectories");
    }

    [Theory]
    [InlineData("catalog")]
    [InlineData("planner")]
    [InlineData("ownership")]
    [InlineData("workloads")]
    [InlineData("inspector")]
    public void Verifier_NullDependency_RejectsProgrammerError(string dependency)
    {
        Action construct = () => _ = new NativeTemplateArtifactVerifier(
            dependency == "catalog" ? null! : Substitute.For<IBasicCompanionCatalog>(),
            dependency == "planner" ? null! : new(),
            dependency == "ownership" ? null! : Substitute.For<INativeTemplateOwnership>(),
            dependency == "workloads" ? null! : Substitute.For<IWorkloadStore>(),
            dependency == "inspector" ? null! : Substitute.For<IBasicArchiveInspector>());

        construct.Should().Throw<ArgumentNullException>().WithParameterName(dependency);
    }

    [Theory]
    [InlineData("paths")]
    [InlineData("workloads")]
    [InlineData("interaction")]
    [InlineData("fileSystem")]
    [InlineData("verifier")]
    public void FreshScopeFactory_NullDependency_RejectsProgrammerError(string dependency)
    {
        Action construct = () => _ = new BoundInitScopeFactory(
            dependency == "paths" ? null! : new(Path.GetTempPath()),
            dependency == "workloads" ? null! : Substitute.For<IWorkloadStore>(),
            dependency == "interaction" ? null! : Substitute.For<IInteractionService>(),
            dependency == "fileSystem" ? null! : Substitute.For<IFileSystem>(),
            dependency == "verifier" ? null! : Verifier());

        construct.Should().Throw<ArgumentNullException>().WithParameterName(dependency);
    }

    [Theory]
    [InlineData("templates")]
    [InlineData("scopes")]
    [InlineData("qualification")]
    public void InstalledProjection_NullDependency_RejectsProgrammerError(string dependency)
    {
        Action construct = () => _ = new QuickstartInstalledTemplates(
            dependency == "templates" ? null! : Substitute.For<ITemplaterFactory>(),
            dependency == "scopes" ? null! : Substitute.For<IBoundInitScopeFactory>(),
            dependency == "qualification" ? null! : Qualification());

        construct.Should().Throw<ArgumentNullException>().WithParameterName(dependency);
    }

    [Fact]
    public async Task InstalledProjection_NullInputs_DoNotOpenEitherScope()
    {
        var templates = Substitute.For<ITemplaterFactory>();
        var scopes = Substitute.For<IBoundInitScopeFactory>();
        QuickstartInstalledTemplates projection = new(templates, scopes, Qualification());
        QuickstartCatalog catalog = new([], new(QuickstartCatalogOrigin.LocalFile, null, null));
        QuickstartInstalledQuery query = new(WorkingDirectory.FromExplicit(Path.GetTempPath()), new("Owner", "1.0.0", "https://fixtures.invalid/v3/index.json"));
        Func<Task> nullCatalog = () => projection.ProjectAsync(null!, query, CancellationToken.None);
        Func<Task> nullQuery = () => projection.ProjectAsync(catalog, null!, CancellationToken.None);

        await nullCatalog.Should().ThrowAsync<ArgumentNullException>().WithParameterName("catalog");
        await nullQuery.Should().ThrowAsync<ArgumentNullException>().WithParameterName("query");
        scopes.ReceivedCalls().Should().BeEmpty();
        templates.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("availability")]
    [InlineData("catalog")]
    [InlineData("support")]
    [InlineData("verifier")]
    [InlineData("lifetime")]
    public void FreshScope_NullDependency_RejectsProgrammerError(string dependency)
    {
        Action construct = () => _ = new BoundInitScope(
            dependency == "availability" ? null! : new([]),
            dependency == "catalog" ? null! : new([]),
            dependency == "support" ? null! : new(new(new([])), new(Substitute.For<IFileSystem>())),
            dependency == "verifier" ? null! : Verifier(),
            dependency == "lifetime" ? null! : Substitute.For<IDisposable>());

        construct.Should().Throw<ArgumentNullException>().WithParameterName(dependency);
    }

    [Fact]
    public void Registration_BindingRemainsOptInAndDoesNotActivateCommands()
    {
        ServiceCollection services = new();
        services.AddTemplatesOrchestrator();
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(IBoundInitScopeFactory)
            || descriptor.ServiceType == typeof(NativeTemplateArtifactVerifier));
        int commands = services.Count(descriptor => descriptor.ServiceType == typeof(FuncCliCommand));

        services.AddNativeTemplateSetup().AddNativeTemplateArtifactBinding();

        services.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(IBoundInitScopeFactory));
        services.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(NativeTemplateArtifactVerifier));
        services.Count(descriptor => descriptor.ServiceType == typeof(FuncCliCommand)).Should().Be(commands);
    }

    private static NativeTemplateArtifactVerifier Verifier()
        => new(Substitute.For<IBasicCompanionCatalog>(), new(), Substitute.For<INativeTemplateOwnership>(),
            Substitute.For<IWorkloadStore>(), Substitute.For<IBasicArchiveInspector>());

    private static QuickstartQualification Qualification()
        => new(Substitute.For<IBasicCompanionCatalog>(), new(), Substitute.For<IBasicTemplateLifecycle>(),
            Substitute.For<IWorkloadStore>(), Substitute.For<ICliVersionProvider>());
}
