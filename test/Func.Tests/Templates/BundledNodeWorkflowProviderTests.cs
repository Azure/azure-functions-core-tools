// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Native;
using Azure.Functions.Cli.Workloads.Node.Native;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    [Fact]
    public void Provider_NullDependencies_RejectProgrammerErrors()
    {
        Action policy = () => new NodeNativeTemplatePolicy(null!);
        Action npm = () => new NodeProjectPreparation(null!);
        policy.Should().Throw<ArgumentNullException>().WithParameterName("preparation");
        npm.Should().Throw<ArgumentNullException>().WithParameterName("npmInstaller");
        var owner = new NodeNativeTemplatePolicy(new(new()));
        Action missingSource = () => owner.Handles(null!);
        Action missingPreparationSource = () => owner.RequiresProjectPreparation(null!);
        missingSource.Should().Throw<ArgumentNullException>().WithParameterName("selected");
        missingPreparationSource.Should().Throw<ArgumentNullException>().WithParameterName("selected");
    }

    [Theory]
    [InlineData("Azure.Functions.Node.Basic.JavaScript", "JavaScript", "Azure.Functions.Templates.Node.Basic", "1.0.0", true, true)]
    [InlineData("Azure.Functions.Node.Basic.TypeScript", "TypeScript", "Azure.Functions.Templates.Node.Basic", "1.0.0-preview.2", true, true)]
    [InlineData("Azure.Functions.Node.Basic.JavaScript", "JavaScript", "Other.Package", "1.0.0", true, false)]
    [InlineData("Azure.Functions.Node.Basic.JavaScript", "TypeScript", "Azure.Functions.Templates.Node.Basic", "1.0.0", true, true)]
    [InlineData("Azure.Functions.Node.Basic.JavaScript", "js", "Azure.Functions.Templates.Node.Basic", "1.0.0", true, true)]
    [InlineData("Azure.Functions.Node.Other.JavaScript", "JavaScript", "Azure.Functions.Templates.Node.Basic", "1.0.0", true, true)]
    [InlineData("Azure.Functions.Node.Basic.JavaScript", "JavaScript", "Azure.Functions.Templates.Node.Basic", null, true, true)]
    [InlineData("Azure.Functions.Node.Basic.JavaScript", "JavaScript", "Azure.Functions.Templates.Node.Basic", "[1.0,2.0)", true, true)]
    [InlineData("Azure.Functions.Node.Http.JavaScript", "JavaScript", "Azure.Functions.Templates.Node.Basic", "1.0.0", false, false)]
    public void Provider_RequiredPhase_CannotBeSuppressedByMalformedBasicProjectMetadata(
        string identity, string language, string package, string? version, bool project, bool expected)
    {
        var source = Substitute.For<NativeTemplateSource>();
        source.Identity.Returns(identity);
        source.Language.Returns(language);
        source.PackageId.Returns(package);
        source.PackageVersion.Returns(version!);
        source.IsProject.Returns(project);
        var policy = new NodeNativeTemplatePolicy(new(new()));

        policy.RequiresProjectPreparation(source).Should().Be(expected);
        if (expected)
        {
            policy.CreateProjectPreparation(source).Should().BeOfType<NodeProjectPreparation>();
        }
        else
        {
            Action unsupported = () => policy.CreateProjectPreparation(source);
            unsupported.Should().Throw<NotSupportedException>();
        }
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
