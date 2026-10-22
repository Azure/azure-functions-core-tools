// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.IO.Compression;
using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Native;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.TemplateEngine.Abstractions.Installer;
using Microsoft.TemplateEngine.Edge.Settings;
using NuGet.Packaging;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    private static string FunctionArchive => Path.Combine(AppContext.BaseDirectory, "node-function-artifacts",
        $"Azure.Functions.Templates.Node.Functions.{NodeArtifactVersion}.nupkg");

    private async Task InstallNodeAuthoringCatalogAsync()
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        await InstallFunctionAuthoringArchiveAsync();
    }

    private async Task InstallFunctionAuthoringArchiveAsync()
    {
        using var writer = await _templates.OpenWriteAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        await writer.ExecuteAsync(async (session, token) =>
        {
            var provider = session.PackageManager.GetBuiltInManagedProvider(InstallationScope.Global);
            (await provider.InstallAsync([new InstallRequest(FunctionArchive)], token)).Should().ContainSingle().Which.Success.Should().BeTrue();
        }, async (session, token) =>
        {
            (await session.PackageManager.GetManagedTemplatePackagesAsync(false, token)).Should().HaveCount(2);
        }, CancellationToken.None);
    }

    [Fact]
    public void Producer_BasicAndFunctionArchives_PreserveDistinctCompleteOwnedInventories()
    {
        using var outer = new PackageArchiveReader(BuiltArchive);
        using var nested = new MemoryStream();
        outer.GetStream("tools/any/templates/node-basic.nupkg").CopyTo(nested);
        nested.Position = 0;
        using var basic = new PackageArchiveReader(nested);
        using var functions = new PackageArchiveReader(FunctionArchive);
        string[] basicConfigurations = [.. basic.GetFiles().Where(path => path.EndsWith("/.template.config/template.json", StringComparison.Ordinal))];
        string[] functionConfigurations = [.. functions.GetFiles().Where(path => path.EndsWith("/.template.config/template.json", StringComparison.Ordinal))];
        basicConfigurations.Should().BeEquivalentTo([
            "content/project-js/.template.config/template.json", "content/project-ts/.template.config/template.json"]);
        functions.NuspecReader.GetId().Should().Be("Azure.Functions.Templates.Node.Functions");
        functionConfigurations.Should().HaveCount(33);
        basic.GetFiles().Should().NotContain(path => path.StartsWith("content/items/", StringComparison.Ordinal)
            || path.StartsWith("content/http-", StringComparison.Ordinal));
        functions.GetFiles().Should().NotContain(path => path.StartsWith("content/project-", StringComparison.Ordinal));
        foreach (var raw in ReadNodeSource().Templates)
        {
            using var config = JsonDocument.Parse(Read(functions, NodeConfigurationPath(raw.Id!)));
            config.RootElement.GetProperty("identity").GetString().Should().Be(NativeNodeIdentity(raw.Id!));
        }
        using var declaration = JsonDocument.Parse(outer.GetStream("bundled-templates.json"));
        declaration.RootElement.GetProperty("assets")[0].GetProperty("templates").GetArrayLength().Should().Be(2);
        Convert.ToHexStringLower(SHA256.HashData(nested.ToArray())).Should()
            .Be(declaration.RootElement.GetProperty("assets")[0].GetProperty("sha256").GetString());
    }

    [Theory]
    [InlineData("JavaScript", "js", false, false)]
    [InlineData("JavaScript", "js", true, false)]
    [InlineData("TypeScript", "ts", false, true)]
    [InlineData("TypeScript", "ts", true, true)]
    public async Task Producer_ExplicitFunctionAssociation_ReachesActuallyLoadedOwnerAndKeepsHttpAvailable(
        string language, string alias, bool legacy, bool nested)
    {
        WorkloadEntry stack = await InstallAssociationStackAsync();
        NodeFunctionCatalogFixture catalog = new(stack, FunctionArchive);
        var result = await catalog.InstallAsync(_templates, _workloads, _paths, _installer, _files,
            AdmissionDeployment(new FileWorkloadOperationCoordinator(_paths)), Home, _root);
        result.Status.Should().Be(BasicAcquisitionStatus.Completed, result.Message);
        using var services = await CreateServicesAsync(nativeCatalog: catalog);
        using var scope = services.CreateScope();
        WorkloadNativeTemplatePolicyRegistration registration = scope.ServiceProvider
            .GetServices<WorkloadNativeTemplatePolicyRegistration>().Single(value => value.Policy.Stack == "node");
        registration.Policy.GetType().Assembly.GetName().Name.Should().Be("Azure.Functions.Cli.Workloads.Node");
        registration.Policy.GetType().Assembly.GetReferencedAssemblies().Should().NotContain(value => value.Name == "func");
        string targetPath = Path.Combine(_root, "loaded-http-" + alias, nested ? Path.Combine("src", "functions") : "root");
        var target = WorkingDirectory.FromExplicit(targetPath);
        var context = new TemplateEngineContext(target, new(target, "node", language),
            itemOutputIntent: legacy ? NativeItemOutputIntent.LegacyWorkflowRelative : NativeItemOutputIntent.LiteralOutput);
        var availability = new TemplateConstraintEvaluationSnapshot([new(stack.PackageId, NuGetVersion.Parse(stack.PackageVersion), ["node"], true)]);
        using var templater = await scope.ServiceProvider.GetRequiredService<ITemplaterFactory>()
            .CreateAsync(context, availability, CancellationToken.None);
        var selected = (await templater.ResolveGroupAsync("HttpTrigger-" + language, TemplateType.Item, CancellationToken.None)).Group!.Single();
        var provider = await scope.ServiceProvider.GetRequiredService<WorkloadNativeTemplatePolicies>().FindAsync(selected, CancellationToken.None);
        provider.Should().NotBeNull();
        TemplateArgumentParseRequest input = new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(), []);
        var parsed = new TemplateArgumentParser().Parse(input);
        var defaults = await provider!.Compatibility.ResolveAsync(selected, parsed, null, false, CancellationToken.None);
        defaults.Succeeded.Should().BeTrue(string.Join(" ", defaults.Diagnostics));
        var command = NodeComposedItem(targetPath, "HttpTrigger-" + language) with
        {
            OutputIntent = legacy ? NativeItemOutputIntent.LegacyWorkflowRelative : NativeItemOutputIntent.LiteralOutput,
        };
        TemplateInvocationRequest invocation = new(target, parsed.ValuesByCanonicalName, defaults.Name)
        {
            ArgumentSource = input, ExplicitParameterNames = parsed.ExplicitParameterNames,
        };
        var prepared = await provider.PrepareAsync(new NativeItemWorkflowContext(templater, selected, parsed, command, invocation), CancellationToken.None);
        prepared.Request.Should().NotBeNull(string.Join(" ", prepared.Diagnostics));
        var preview = await selected.InvokeAsync(prepared.Request! with { DryRun = true }, CancellationToken.None);
        preview.Status.Should().Be(TemplateInvocationStatus.Success, preview.Diagnostic);
        Directory.Exists(targetPath).Should().BeFalse();
        var created = await selected.InvokeAsync(prepared.Request!, CancellationToken.None);
        created.Status.Should().Be(TemplateInvocationStatus.Success, created.Diagnostic);
        string output = legacy ? Path.Combine(targetPath, "src", "functions") : targetPath;
        File.ReadAllText(Path.Combine(output, defaults.Name + "." + alias)).Should().Contain("app.http(", "authLevel: 'anonymous'");
    }

    [Fact]
    public async Task Producer_BasicInstallation_DoesNotReportFunctionReadinessOrExposeBundledHttp()
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        using var session = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        using var templater = TemplateFixtures.CreateTemplater(session);

        (await templater.ListAsync(TemplateType.Project, CancellationToken.None)).Should().HaveCount(2);
        (await templater.ListAsync(TemplateType.Item, CancellationToken.None)).Should().BeEmpty();
        (await templater.ResolveGroupAsync("http", TemplateType.Item, CancellationToken.None)).Status.Should().Be(TemplateResolutionStatus.NotFound);
    }

    [Fact]
    public async Task Producer_InstalledFunctions_EmptyProductionAssociationCannotAdoptFixtureQualification()
    {
        WorkloadEntry stack = await InstallAssociationStackAsync();
        NodeFunctionCatalogFixture fixture = new(stack, FunctionArchive);
        (await fixture.InstallAsync(_templates, _workloads, _paths, _installer, _files,
            AdmissionDeployment(new FileWorkloadOperationCoordinator(_paths)), Home, _root)).Status.Should().Be(BasicAcquisitionStatus.Completed);
        using var services = await CreateServicesAsync();
        using var scope = services.CreateScope();
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "unqualified-production"));
        var availability = new TemplateConstraintEvaluationSnapshot([new(stack.PackageId, NuGetVersion.Parse(stack.PackageVersion), ["node"], true)]);
        using var templater = await scope.ServiceProvider.GetRequiredService<ITemplaterFactory>()
            .CreateAsync(new(output, new(output, "node", "JavaScript")), availability, CancellationToken.None);

        var selected = (await templater.ResolveGroupAsync("HttpTrigger-JavaScript", TemplateType.Item, CancellationToken.None)).Group!.Single();
        Func<Task> resolve = () => scope.ServiceProvider.GetRequiredService<WorkloadNativeTemplatePolicies>().FindAsync(selected, CancellationToken.None);

        await resolve.Should().ThrowAsync<InvalidTemplateMetadataException>().WithMessage("*authorized loaded workload policy*");
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Fact]
    public async Task Producer_ActualLoadedPolicy_All33ItemsRetainSourcePinsMandatoryStagesAndScopedLifetime()
    {
        WorkloadEntry stack = await InstallAssociationStackAsync();
        NodeFunctionCatalogFixture catalog = new(stack, FunctionArchive);
        (await catalog.InstallAsync(_templates, _workloads, _paths, _installer, _files,
            AdmissionDeployment(new FileWorkloadOperationCoordinator(_paths)), Home, _root)).Status.Should().Be(BasicAcquisitionStatus.Completed);
        using var services = await CreateServicesAsync(nativeCatalog: catalog);
        using var scope = services.CreateScope();
        string bundleRoot = Path.Combine(_root, "qualified-local-bundle");
        Directory.CreateDirectory(Path.Combine(bundleRoot, "bin"));
        File.WriteAllText(Path.Combine(bundleRoot, "bin", "extensions.json"), NodeBridgeExtensions);
        var bundle = await new TemplateEngineBundleContextReader(_files).ReadAsync(
            new ExtensionBundleResolution.Resolved(BundleHelpers.StableBundleId, "4.35.0", bundleRoot, null), CancellationToken.None);
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "loaded-all-items"));
        var availability = new TemplateConstraintEvaluationSnapshot([new(stack.PackageId, NuGetVersion.Parse(stack.PackageVersion), ["node"], true)]);
        using var templater = await scope.ServiceProvider.GetRequiredService<ITemplaterFactory>().CreateAsync(
            new(output, bundle: bundle, enableBundleBindingConstraints: true), availability, CancellationToken.None);
        var policies = scope.ServiceProvider.GetRequiredService<WorkloadNativeTemplatePolicies>();
        foreach (var raw in ReadNodeSource().Templates)
        {
            var selected = (await templater.ResolveGroupAsync(raw.Id!, TemplateType.Item, CancellationToken.None)).Group!.Single();
            var candidate = await policies.GetCandidateAsync(selected.Entry, selected.Configuration!, selected.ConfigurationSourceIdentity!,
                selected.PackageMountPoint, selected.ReadPackageFileAsync, selected.ValidateConfigurationAsync, false,
                CancellationToken.None, selected.InvocationOwner);
            candidate.Should().NotBeNull();
            candidate!.Stages.Should().Be(NativeTemplatePolicyStages.Invocation | NativeTemplatePolicyStages.CapturedOutput);
            var lifetime = new NativeTemplatePolicyLifetime();
            NativeTemplateSource source = candidate.CreateSource(lifetime);
            var description = await candidate.Admission.Registration.Policy.DescribeItemAsync(source, CancellationToken.None);
            description.DefaultName.Should().Be(selected.Entry.DefaultName);
            var parsed = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(), []));
            var qualified = await candidate.Admission.Registration.Policy.QualifyInvocationAsync(
                new NodePolicyInvocationFixture(source, output, parsed.ValuesByCanonicalName), CancellationToken.None);
            qualified.RequiredSources.Should().HaveCount(8);
            foreach (var pin in qualified.RequiredSources)
            {
                var bytes = await source.ReadPackageFileAsync(pin.RelativePath, CancellationToken.None);
                pin.Sha256.Should().Be(Convert.ToHexString(SHA256.HashData(bytes.Span)));
            }
            lifetime.Dispose();
            Func<Task> expired = () => source.RevalidateAsync(CancellationToken.None);
            await expired.Should().ThrowAsync<ObjectDisposedException>();
        }
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Fact]
    public async Task Producer_LoadedOwner_UnexpectedNativeOutput_IsRejectedBeforeUserWrites()
    {
        WorkloadEntry stack = await InstallAssociationStackAsync();
        string changedArchive = Path.Combine(_root, "functions-with-extra-output.nupkg");
        File.Copy(FunctionArchive, changedArchive);
        using (var archive = ZipFile.Open(changedArchive, ZipArchiveMode.Update))
        using (var writer = new StreamWriter(archive.CreateEntry("content/http-js/unexpected.txt").Open()))
        {
            writer.Write("unexpected native output");
        }
        NodeFunctionCatalogFixture catalog = new(stack, changedArchive);
        (await catalog.InstallAsync(_templates, _workloads, _paths, _installer, _files,
            AdmissionDeployment(new FileWorkloadOperationCoordinator(_paths)), Home, _root)).Status.Should().Be(BasicAcquisitionStatus.Completed);
        using var services = await CreateServicesAsync(nativeCatalog: catalog);
        using var scope = services.CreateScope();
        var output = WorkingDirectory.FromExplicit(Directory.CreateDirectory(Path.Combine(_root, "protected-user-output")).FullName);
        string user = Path.Combine(output.Info.FullName, "user.txt");
        File.WriteAllText(user, "preserve existing user bytes");
        var availability = new TemplateConstraintEvaluationSnapshot([new(stack.PackageId, NuGetVersion.Parse(stack.PackageVersion), ["node"], true)]);
        using var templater = await scope.ServiceProvider.GetRequiredService<ITemplaterFactory>()
            .CreateAsync(new(output, new(output, "node", "JavaScript")), availability, CancellationToken.None);
        var selected = (await templater.ResolveGroupAsync("HttpTrigger-JavaScript", TemplateType.Item, CancellationToken.None)).Group!.Single();
        var provider = await scope.ServiceProvider.GetRequiredService<WorkloadNativeTemplatePolicies>().FindAsync(selected, CancellationToken.None);
        provider.Should().NotBeNull();
        TemplateArgumentParseRequest input = new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(), []);
        var parsed = new TemplateArgumentParser().Parse(input);
        TemplateInvocationRequest request = new(output, parsed.ValuesByCanonicalName, "QualifiedHttp")
        {
            ArgumentSource = input, ExplicitParameterNames = parsed.ExplicitParameterNames,
        };
        var prepared = await provider!.PrepareAsync(
            new NativeItemWorkflowContext(templater, selected, parsed, NodeComposedItem(output.Info.FullName, selected.Entry.Identity), request),
            CancellationToken.None);

        var result = await selected.InvokeAsync(prepared.Request!, CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.InvalidTemplate, result.Diagnostic);
        result.Diagnostic.Should().Contain("exactly one regular file");
        Directory.GetFiles(output.Info.FullName).Should().Equal(user);
        File.ReadAllText(user).Should().Be("preserve existing user bytes");
    }
}
