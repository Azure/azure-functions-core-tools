// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

extern alias CliHost;

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Node;
using Azure.Functions.Cli.Workloads.Node;
using Azure.Functions.Cli.Workloads.Node.Native;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NuGet.Versioning;
using NodeNpmInstaller = CliHost::Azure.Functions.Cli.Workloads.Node.NodeNpmInstaller;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    [Fact]
    public void Provider_NullDependencies_RejectProgrammerErrors()
    {
        Action files = () => new NodeTemplateWorkflowProvider(null!, new NodeNpmInstaller());
        Action npm = () => new NodeTemplateWorkflowProvider(_files, null!);
        files.Should().Throw<ArgumentNullException>().WithParameterName("fileSystem");
        npm.Should().Throw<ArgumentNullException>().WithParameterName("npmInstaller");
    }

    [Theory]
    [InlineData("Azure.Functions.Node.Basic.JavaScript", "JavaScript", "Azure.Functions.Templates.Node.Basic", "1.0.0", true, true)]
    [InlineData("Azure.Functions.Node.Basic.TypeScript", "TypeScript", "Azure.Functions.Templates.Node.Basic", "1.0.0-preview.2", true, true)]
    [InlineData("Azure.Functions.Node.Basic.JavaScript", "JavaScript", "Other.Package", "1.0.0", true, false)]
    [InlineData("Azure.Functions.Node.Basic.JavaScript", "TypeScript", "Azure.Functions.Templates.Node.Basic", "1.0.0", true, false)]
    [InlineData("Azure.Functions.Node.Basic.JavaScript", "js", "Azure.Functions.Templates.Node.Basic", "1.0.0", true, false)]
    [InlineData("Azure.Functions.Node.Other.JavaScript", "JavaScript", "Azure.Functions.Templates.Node.Basic", "1.0.0", true, false)]
    [InlineData("Azure.Functions.Node.Basic.JavaScript", "JavaScript", "Azure.Functions.Templates.Node.Basic", null, true, false)]
    [InlineData("Azure.Functions.Node.Basic.JavaScript", "JavaScript", "Azure.Functions.Templates.Node.Basic", "[1.0,2.0)", true, false)]
    [InlineData("Azure.Functions.Node.Http.JavaScript", "JavaScript", "Azure.Functions.Templates.Node.Basic", "1.0.0", false, false)]
    public void Provider_Handles_ClaimsOnlyOwnedProjectIdentityPackageAndCanonicalLanguage(
        string identity, string language, string package, string? version, bool project, bool expected)
    {
        var provider = new NodeTemplateWorkflowProvider(_files, new NodeNpmInstaller());
        TemplateCatalogEntry entry = new(identity, "Controlled", null, project ? TemplateType.Project : TemplateType.Item,
            [], null, language, 0, new(package, version), false, [], null);

        provider.Handles(entry).Should().Be(expected);
    }

    [Fact]
    public async Task Provider_ActualInstalledPackage_UsesHostCompiledHelperAndQualifiedProjectAdapters()
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        using var services = await CreateServicesAsync();
        var target = WorkingDirectory.FromExplicit(Path.Combine(_root, "provider-project"));
        var availability = new TemplateConstraintEvaluationSnapshot([new("node-stack", NuGetVersion.Parse("1.0.0"), ["node"], true)]);
        using var templater = await services.GetRequiredService<ITemplaterFactory>().CreateProjectAsync(new(target), availability,
            services.GetRequiredService<TemplateProjectSupport>(), CancellationToken.None);
        var selected = (await templater.ResolveGroupAsync("Azure.Functions.Node.Basic.TypeScript", TemplateType.Project, CancellationToken.None)).Group!.Single();
        var arguments = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(), []));
        var native = await selected.InvokeAsync(new(target, arguments.ValuesByCanonicalName, DryRun: true), CancellationToken.None);
        var npm = Substitute.For<NodeNpmInstaller>();
        npm.InstallAsync(target.Info.FullName, Arg.Any<CancellationToken>()).Returns(call =>
        {
            File.Exists(Path.Combine(target.Info.FullName, ".func", "config.json")).Should().BeTrue();
            File.Exists(Path.Combine(target.Info.FullName, "package.json")).Should().BeTrue();
            return (0, "host-compiled helper supplied explicitly");
        });
        var provider = new NodeTemplateWorkflowProvider(_files, npm);
        INativeProjectWorkflowProvider projects = provider;

        provider.Handles(selected.Entry).Should().BeTrue();
        provider.Stack.Should().Be("node");
        projects.Preparation.Should().BeOfType<NodeProjectPreparation>();
        typeof(NodeNpmInstaller).Assembly.Should().BeSameAs(typeof(NodeProjectPreparation).Assembly);
        services.GetServices<IProjectInitializer>().Single(initializer => initializer.Stack == "node").GetType().Assembly
            .Should().NotBeSameAs(typeof(NodeNpmInstaller).Assembly);
        services.GetService<NodeNpmInstaller>().Should().BeNull("the physical stack's helper is a different assembly type");

        var context = new NativeProjectPreparationContext(selected, target, arguments.ValuesByCanonicalName, native);
        var preview = await projects.Preparation!.PreviewAsync(context, CancellationToken.None);
        await npm.DidNotReceive().InstallAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        (await selected.InvokeAsync(new(target, arguments.ValuesByCanonicalName), CancellationToken.None)).Status.Should().Be(TemplateInvocationStatus.Success);
        var completed = await projects.Preparation.CompleteAsync(context, preview, CancellationToken.None);

        completed.Succeeded.Should().BeTrue();
        completed.Diagnostics.Should().Contain("host-compiled helper supplied explicitly");
        await npm.Received(1).InstallAsync(target.Info.FullName, Arg.Any<CancellationToken>());
        Action noEntry = () => provider.Handles(null!);
        noEntry.Should().Throw<ArgumentNullException>().WithParameterName("entry");
    }

    [Theory]
    [InlineData("JavaScript", "DurableFunctionsEntity-JavaScript")]
    [InlineData("TypeScript", "DurableFunctionsEntity-TypeScript")]
    public async Task Provider_ActualHiddenItem_UsesSelectedSourceDefaultsAndRegexWithoutPrivateContractDelegates(
        string language, string identity)
    {
        var stack = await InstallAssociationStackAsync();
        var catalog = new NodeFunctionCatalogFixture(stack, FunctionArchive);
        (await catalog.InstallAsync(_templates, _workloads, _paths, _installer, _files,
            AdmissionDeployment(new FileWorkloadOperationCoordinator(_paths)), Home, _root)).Status.Should().Be(BasicAcquisitionStatus.Completed);
        using var services = await CreateServicesAsync(nativeCatalog: catalog);
        using var scope = services.CreateScope();
        string bundlePath = Path.Combine(_root, "provider-bundle");
        Directory.CreateDirectory(Path.Combine(bundlePath, "bin"));
        File.WriteAllText(Path.Combine(bundlePath, "bin", "extensions.json"), """{"extensions":[{"bindings":["ENTITYTRIGGER"]}]}""");
        var bundle = await new TemplateEngineBundleContextReader(_files).ReadAsync(
            new ExtensionBundleResolution.Resolved(BundleHelpers.StableBundleId, "4.35.0", bundlePath, null), CancellationToken.None);
        var target = WorkingDirectory.FromExplicit(Path.Combine(_root, "provider-item"));
        var context = new TemplateEngineContext(target, new(target, "node", language), bundle, enableBundleBindingConstraints: true);
        var availability = new TemplateConstraintEvaluationSnapshot([new("node-stack", NuGetVersion.Parse("1.0.0"), ["node"], true)]);
        using var templater = await scope.ServiceProvider.GetRequiredService<ITemplaterFactory>().CreateAsync(context, availability, CancellationToken.None);
        var selected = (await templater.ResolveGroupAsync(identity, TemplateType.Item, CancellationToken.None)).Group!.Single();
        var arguments = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(), []));
        var provider = await scope.ServiceProvider.GetRequiredService<WorkloadNativeTemplatePolicies>().FindAsync(selected, CancellationToken.None);
        provider.Should().NotBeNull();

        var defaults = await provider!.Compatibility.ResolveAsync(selected, arguments, null, false, CancellationToken.None);
        var rejected = await provider.Compatibility.ResolveAsync(selected, arguments, "Unsanitized-Invalid", false, CancellationToken.None);

        selected.Entry.IsHidden.Should().BeTrue();
        defaults.Succeeded.Should().BeTrue();
        defaults.Name.Should().Be("counter");
        defaults.Origin.Should().Be("authoredDefault");
        rejected.Succeeded.Should().BeFalse();
        rejected.Name.Should().Be("Unsanitized-Invalid");
        rejected.Diagnostics.Should().ContainSingle().Which.Should().Contain(NodeItemSource.ManifestPath);
        Directory.Exists(target.Info.FullName).Should().BeFalse();
        var created = await selected.InvokeAsync(new(target, arguments.ValuesByCanonicalName, defaults.Name), CancellationToken.None);
        created.Status.Should().Be(TemplateInvocationStatus.Success, created.Diagnostic);
        string extension = language == "JavaScript" ? "js" : "ts";
        File.ReadAllText(Path.Combine(target.Info.FullName, "counter." + extension)).Should().Contain("const entityName = 'counter';");
    }
}
