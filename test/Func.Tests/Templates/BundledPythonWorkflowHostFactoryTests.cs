// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.Loader;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledPythonTemplateTests
{
    [Fact]
    public async Task PythonBootFactory_ActualInstalledStackAndSourceBoundProviders_InitThenNewWithAllRequestCapabilitiesUnset()
    {
        var (paths, home, packageSource) = await InstallPythonRuntimeAsync("boot");
        var interaction = Substitute.For<IInteractionService>();
        NativeTemplateWorkflowHostFactory factory = new();
        using var host = await factory.CreateAsync(interaction, paths, new(home), services =>
        {
            _loadedBootWorkload = services.Any(service => service.ImplementationInstance is RuntimeWorkloadInfo);
        }, enableBundleBindingConstraints: true, CancellationToken.None);
        _loadedBootWorkload.Should().BeTrue("the actual installed physical Python workload must configure before providers are composed");
        var target = WorkingDirectory.FromExplicit(Path.Combine(_root, "boot-project"));
        InitExecutionRequest project = new(target, "Azure.Functions.Python.Basic", "BootPython", "python", "py",
            false, true, true, false, [], new HashSet<string>());
        project.ProjectPreparation.Should().BeNull();

        using (var invocation = host.CreateInvocation())
        {
            var policy = invocation.Services.GetServices<WorkloadNativeTemplatePolicyRegistration>()
                .Should().ContainSingle(registration => registration.Policy.Stack == "python").Subject;
            policy.Policy.GetType().Assembly.GetName().Name.Should().Be("Azure.Functions.Cli.Workloads.Python");
            policy.Policy.GetType().Assembly.GetReferencedAssemblies().Should().NotContain(assembly => assembly.Name == "func");
            AssemblyLoadContext.GetLoadContext(policy.Policy.GetType().Assembly).Should().BeSameAs(policy.Owner.LoadContext);
            invocation.Services.GetServices<INativeItemWorkflowProvider>().Should().NotContain(provider => provider.Stack == "python");
            invocation.Services.GetServices<INativeProjectWorkflowProvider>().Should().NotContain(provider => provider.Stack == "python");
            var preview = await invocation.Composer.ExecuteProjectAsync(project, CancellationToken.None);
            preview.Succeeded.Should().BeTrue(string.Join(" ", preview.Diagnostics));
            Directory.Exists(target.Info.FullName).Should().BeFalse();
        }
        using (var invocation = host.CreateInvocation())
        {
            var created = await invocation.Composer.ExecuteProjectAsync(project with { DryRun = false }, CancellationToken.None);
            created.Succeeded.Should().BeTrue(string.Join(" ", created.Diagnostics));
        }
        var app = Path.Combine(target.Info.FullName, "function_app.py");
        File.AppendAllText(app, "\n# user-owned boot proof\n");
        var before = File.ReadAllBytes(app);
        NewExecutionRequest item = new(target, "HttpTrigger-Python", null, null, false, true, false, false, false,
            ["--http-trigger-auth-level", "ANONYMOUS"], new HashSet<string>());
        item.ItemCompatibility.Should().BeNull();
        item.SelectedItemEffects.Should().BeNull();

        using (var invocation = host.CreateInvocation())
        {
            var appended = await invocation.Composer.ExecuteItemAsync(item, CancellationToken.None);
            appended.Succeeded.Should().BeTrue(string.Join(" ", appended.Diagnostics));
            appended.ItemName!.Origin.Should().Be("authoredDefault");
            appended.Context!.Project!.Stack.Should().Be("python");
            appended.Context.Bundle!.Id.Should().Be("Microsoft.Azure.Functions.ExtensionBundle");
            appended.Context.Bundle.Version.Should().Be("4.35.0");
            appended.Invocation!.Effects!.Kind.Should().Be("applied");
        }

        File.ReadAllBytes(app).Take(before.Length).Should().Equal(before);
        File.ReadAllText(app).Should().Contain("def http_trigger(", "func.AuthLevel.ANONYMOUS", "# user-owned boot proof");
        Directory.Exists(Path.Combine(target.Info.FullName, ".venv")).Should().BeFalse();
        File.Exists(Path.Combine(target.Info.FullName, ".func", "config.json")).Should().BeTrue();
        packageSource.ReceivedCalls().Should().BeEmpty("boot/creation uses only actually installed local packages");
    }

    private async Task<(WorkloadPathsOptions Paths, string Home, IWorkloadPackageSource PackageSource)> InstallPythonRuntimeAsync(string prefix)
    {
        PhysicalFileSystem files = new();
        WorkloadPathsOptions paths = new(Path.Combine(_root, prefix + "-workloads"));
        var home = Path.Combine(_root, prefix + "-cli");
        FileWorkloadOperationCoordinator coordinator = new(paths);
        WorkloadStore workloads = new(paths, coordinator);
        TemplateStore templates = new(new(home), files, new FileTemplateStoreLock(), NullLoggerFactory.Instance);
        BundledTemplateInstaller bundles = new(templates, new BasicArtifactInspector(files), files, new(home));
        WorkloadMetadataReader metadata = new();
        var runtime = Substitute.For<IWorkloadRuntimeIdentifierProvider>();
        runtime.Current.Returns("win-x64");
        WorkloadRidPackageSelector selector = new(runtime);
        var packageSource = Substitute.For<IWorkloadPackageSource>();
        WorkloadInstaller installer = new(packageSource, new WorkloadPackageInspector(metadata, selector), selector,
            new WorkloadDeploymentService(paths, workloads, metadata, coordinator, files, bundles), coordinator, workloads, files);
        await installer.InstallFromPackageAsync(BuiltArchive);
        await installer.InstallFromPackageAsync(BuildMetadataOnlyPythonBundle());
        return (paths, home, packageSource);
    }

    private string BuildUnownedPythonItem()
    {
        var root = Directory.CreateDirectory(Path.Combine(_root, "unowned-python-item")).FullName;
        var configuration = Path.Combine(Directory.CreateDirectory(Path.Combine(root, ".template.config")).FullName, "template.json");
        File.WriteAllText(configuration, """
            {"identity":"Fixture.Unowned.Python","name":"Unowned Python item","shortName":"unowned-python",
             "tags":{"type":"item","language":"Python"}}
            """);
        var content = Path.Combine(root, "unowned.py");
        File.WriteAllText(content, "unowned content must not be generated");
        PackageBuilder builder = new()
        {
            Id = "Fixture.Unowned.Python.Templates",
            Version = NuGetVersion.Parse("1.0.0"),
            Description = "Native source without runtime-owner provenance.",
        };
        builder.Authors.Add("test");
        builder.PackageTypes.Add(new PackageType("FuncTemplate", new Version(0, 0)));
        builder.Files.Add(new PhysicalPackageFile { SourcePath = configuration, TargetPath = "content/.template.config/template.json" });
        builder.Files.Add(new PhysicalPackageFile { SourcePath = content, TargetPath = "content/unowned.py" });
        var archive = Path.Combine(root, "unowned.nupkg");
        using var stream = File.Create(archive);
        builder.Save(stream);
        return archive;
    }

    private string BuildMetadataOnlyPythonBundle() => BuildMetadataOnlyPythonBundle(_root, _archive);

    internal static string BuildMetadataOnlyPythonBundle(string fixtureRoot, string nativeArchive)
    {
        var root = Directory.CreateDirectory(Path.Combine(fixtureRoot, "bundle-metadata-fixture")).FullName;
        var manifest = Path.Combine(root, "workload.json");
        File.WriteAllText(manifest,
            "{\"$schema\":\"https://aka.ms/func-workloads/package/v1/schema.json\",\"kind\":\"content\",\"displayName\":\"Pinned binding metadata fixture\"}");
        var extensions = Path.Combine(root, "extensions.json");
        using (PackageArchiveReader inner = new(nativeArchive))
        using (var input = inner.GetStream("source/bundles/stable/extensions.json"))
        using (var output = File.Create(extensions))
        {
            input.CopyTo(output);
        }
        PackageBuilder builder = new()
        {
            Id = IInstalledBundleWorkloads.BundleWorkloadPackageId,
            Version = NuGetVersion.Parse("4.35.0"),
            Description = "Metadata-only local fixture; not a runnable extension bundle or backend qualification.",
        };
        builder.Authors.Add("test");
        builder.Tags.Add("alias:bundles");
        builder.PackageTypes.Add(new PackageType(WorkloadPackageTypes.Workload, new Version(0, 0)));
        builder.Files.Add(new PhysicalPackageFile { SourcePath = manifest, TargetPath = "workload.json" });
        builder.Files.Add(new PhysicalPackageFile { SourcePath = extensions, TargetPath = "tools/any/bin/extensions.json" });
        var archive = Path.Combine(root, "metadata-bundle.nupkg");
        using var stream = File.Create(archive);
        builder.Save(stream);
        return archive;
    }
}
