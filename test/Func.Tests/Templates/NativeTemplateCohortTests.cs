// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Tests.Commands.Setup;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    [Fact]
    public async Task Program_UnknownNativeVersionOwnership_IsAnExplicitJsonFailureNotAnInactiveOrFallbackTemplate()
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        using var host = await CreateCohortHostAsync();
        string project = Path.Combine(_root, "unknown-owner-project");
        using (var invocation = host.CreateInvocation())
        {
            var created = await invocation.Composer.ExecuteProjectAsync(NodeComposedInit(project, "js",
                ["--no-bundles", "true", "--skip-npm-install", "true"]), CancellationToken.None);
            created.Succeeded.Should().BeTrue(string.Join(" ", created.Diagnostics));
        }
        await MigrationInstallUserPackageAsync(MigrationInnerArchive(MigrationArchive("99.0.0")));
        string registry = File.ReadAllText(_paths.WorkloadRegistryPath);
        var before = PublicSnapshot(project);
        var templates = MigrationFiles(Path.Combine(Home, "templates"));
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(1));

        var result = await SetupProgramAdvisoryTests.RunCliAsync(Path.Combine(_root, "unknown-owner-process"), Home, _paths.Home, project,
            ["new", project, "-t", "HttpTrigger-JavaScript", "-n", "MustNotCreate", "--format=json"], timeout.Token);

        result.ExitCode.Should().Be(1);
        result.StandardError.Should().BeEmpty();
        using var json = JsonDocument.Parse(result.StandardOutput);
        json.RootElement.GetProperty("kind").GetString().Should().Be("nativeCohortUnavailable");
        json.RootElement.GetProperty("diagnostics")[0].GetString().Should().Contain("unknown ownership");
        PublicSnapshot(project).Should().BeEquivalentTo(before);
        var after = MigrationFiles(Path.Combine(Home, "templates"));
        after.Keys.Should().BeEquivalentTo(templates.Keys);
        foreach (var file in templates) after[file.Key].Should().Be(file.Value);
        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(registry);
    }

    [Fact]
    public async Task CapturedCohort_ArchiveHashChangesAfterSelection_RefusesBeforeNativeOutputOrConfiguration()
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        using var host = await CreateCohortHostAsync();
        using var invocation = host.CreateInvocation();
        var availability = await invocation.Services.GetRequiredService<INativeTemplateAvailability>().CaptureAsync(CancellationToken.None);
        string target = Path.Combine(_root, "hash-guard");
        var context = new TemplateEngineContext(Azure.Functions.Cli.Common.WorkingDirectory.FromExplicit(target));
        using var templater = await invocation.Services.GetRequiredService<ITemplaterFactory>().CreateProjectAsync(context, availability,
            invocation.Services.GetRequiredService<TemplateProjectSupport>(), CancellationToken.None);
        var resolved = await templater.ResolveGroupAsync("Azure.Functions.Node.Basic.JavaScript", TemplateType.Project, CancellationToken.None);
        resolved.Status.Should().Be(TemplateResolutionStatus.Success);
        using var read = await _templates.OpenReadAsync(new(Azure.Functions.Cli.Common.WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        string archive = (await read.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None)).Single().MountPointUri;
        byte[] original = File.ReadAllBytes(archive);
        FileAttributes attributes = File.GetAttributes(archive);
        File.SetAttributes(archive, FileAttributes.Normal);
        try
        {
            using (FileStream stream = new(archive, FileMode.Append, FileAccess.Write)) stream.WriteByte(0);
            var result = await resolved.Group![0].InvokeAsync(new(context.CommandDirectory, new Dictionary<string, string?>
            {
                ["SkipNpmInstall"] = "true",
                ["NoBundles"] = "true",
                ["BundlesChannel"] = "Stable",
            }, "HashGuard"), CancellationToken.None);

            result.Status.Should().Be(TemplateInvocationStatus.TemplateChanged);
            result.Diagnostic.Should().Contain("captured workload asset hash");
            Directory.Exists(target).Should().BeFalse();
        }
        finally
        {
            File.WriteAllBytes(archive, original);
            File.SetAttributes(archive, attributes);
        }
    }

    [Fact]
    public async Task CapturedCohorts_SharedBundledUpdate_InterleaveExactV1V2V1AndReopenPublicProgramWithoutChangingUserTemplates()
    {
        WorkloadRuntimeIdentifierProvider runtime = new();
        string rid = runtime.Current;
        var coordinator = new FileWorkloadOperationCoordinator(_paths);
        var installer = CreateInstaller(coordinator, runtimeOverride: runtime);
        string pointer = MigrationPointerArchive("1.0.0", rid);
        string first = CohortArchive("1.0.0", "first", rid);
        string physicalId = "migration.stack." + rid;
        _source.FindLocalImplementation(pointer, physicalId, "1.0.0", rid).Returns(first);
        var installed = await installer.InstallFromPackageAsync(pointer);
        var shared = await _workloads.AddOwnershipAsync(MigrationOwnershipEntry(installed.Entry, installed.Entry.LogicalPackage),
            WorkloadOwnershipKind.Explicit);
        string userPackage = TemplateFixtures.WriteBasicPackage(_root);
        await MigrationInstallUserPackageAsync(userPackage);
        string firstPayload = _paths.GetInstallDirectory(shared.PackageId, shared.PackageVersion);
        var originalPayload = MigrationFiles(firstPayload);
        string legacyReceipt = Path.Combine(Home, "templates", ".bundled-owners", "azure.functions.templates.node.basic.json");
        byte[] oldReceipt = File.ReadAllBytes(BundledOwnerPath("1.0.0"));
        File.Move(BundledOwnerPath("1.0.0"), legacyReceipt);

        using var oldHost = await CreateCohortHostAsync();
        await AssertCohortAsync(oldHost, "old-before", "1.0.0", "first");
        File.ReadAllBytes(legacyReceipt).Should().Equal(oldReceipt);
        File.Exists(BundledOwnerPath("1.0.0")).Should().BeFalse("read-only generation must not migrate a compatible legacy receipt");
        string second = CohortArchive("2.0.0", "second", rid);
        MigrationSource(second);

        var updated = await installer.UpdateAsync(shared.PackageId, null, MigrationFeed, true, true);

        updated.Entry.PackageVersion.Should().Be("2.0.0");
        File.Exists(BundledOwnerPath("1.0.0")).Should().BeTrue();
        File.Exists(BundledOwnerPath("2.0.0")).Should().BeTrue();
        File.Exists(legacyReceipt).Should().BeFalse("only the explicit protected update migrates the old receipt");
        MigrationFiles(firstPayload).Should().BeEquivalentTo(originalPayload);
        (await _workloads.GetWorkloadsAsync()).Single(entry => entry.PackageVersion == "1.0.0").IsImplicitlyInstalled.Should().BeTrue();
        await installer.EnsureInstalledAsync((await _workloads.GetWorkloadsAsync()).Single(entry => entry.PackageVersion == "1.0.0"),
            CancellationToken.None, checkOnly: true);
        await installer.EnsureInstalledAsync(updated.Entry, CancellationToken.None, checkOnly: true);
        using var newHost = await CreateCohortHostAsync();
        await AssertCohortAsync(newHost, "new", "2.0.0", "second");
        await AssertCohortAsync(oldHost, "old-after", "1.0.0", "first");

        string target = Path.Combine(_root, "fresh-program");
        Directory.CreateDirectory(target);
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(1));
        var initialized = await SetupProgramAdvisoryTests.RunCliAsync(Path.Combine(_root, "cohort-process"), Home, _paths.Home, target,
            ["init", target, "-s", "node", "-l", "js", "--skip-npm-install", "--no-bundles"], timeout.Token);
        initialized.ExitCode.Should().Be(0, initialized.StandardOutput + initialized.StandardError);
        using var package = JsonDocument.Parse(File.ReadAllText(Path.Combine(target, "package.json")));
        package.RootElement.GetProperty("funcTemplateCohort").GetString().Should().Be("second");
        var created = await SetupProgramAdvisoryTests.RunCliAsync(Path.Combine(_root, "cohort-process"), Home, _paths.Home, target,
            ["new", target, "-t", "HttpTrigger-JavaScript", "-n", "FreshCohort", "--format=json"], timeout.Token);
        created.ExitCode.Should().Be(0, created.StandardOutput + created.StandardError);
        using var result = JsonDocument.Parse(created.StandardOutput);
        result.RootElement.GetProperty("selectedTemplate").GetProperty("package").GetProperty("version").GetString().Should().Be("2.0.0");
        File.ReadAllText(Path.Combine(target, "src", "functions", "FreshCohort.js"))
            .Should().Contain("app.http('FreshCohort'", "// cohort:second");
        using var read = await _templates.OpenReadAsync(new(Azure.Functions.Cli.Common.WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        IReadOnlyList<IManagedTemplatePackage> registrations = await read.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None);
        registrations.Where(value => value.Identifier == "Azure.Functions.Templates.Node.Basic").Select(value => value.Version)
            .Should().BeEquivalentTo("1.0.0", "2.0.0");
        registrations.Should().Contain(value => value.Identifier == userPackage);
    }

    private async Task<NativeTemplateWorkflowHost> CreateCohortHostAsync()
    {
        var host = await new NativeTemplateWorkflowHostFactory().CreateAsync(_interaction, _paths, new(Home), services =>
        {
            services.AddFirstPartyNativeWorkflows();
            TrackEntryContexts(services);
        }, enableBundleBindingConstraints: true, CancellationToken.None);
        await host.StartAsync(CancellationToken.None);
        return host;
    }

    private async Task AssertCohortAsync(NativeTemplateWorkflowHost host, string name, string version, string marker)
    {
        string target = Path.Combine(_root, "cohort-" + name);
        using var invocation = host.CreateInvocation();
        var project = await invocation.Composer.ExecuteProjectAsync(NodeComposedInit(target, "js",
            ["--no-bundles", "true", "--skip-npm-install", "true"]), CancellationToken.None);
        project.Succeeded.Should().BeTrue(string.Join(" ", project.Diagnostics));
        project.SelectedTemplate!.Package!.Version.Should().Be(version);
        using var package = JsonDocument.Parse(File.ReadAllText(Path.Combine(target, "package.json")));
        package.RootElement.GetProperty("funcTemplateCohort").GetString().Should().Be(marker);
        var item = await invocation.Composer.ExecuteItemAsync(NodeComposedItem(target, "HttpTrigger-JavaScript") with
        {
            Name = "ExactCohort",
            OutputIntent = NativeItemOutputIntent.LegacyWorkflowRelative,
        }, CancellationToken.None);
        item.Succeeded.Should().BeTrue(string.Join(" ", item.Diagnostics));
        item.SelectedTemplate!.Package!.Version.Should().Be(version);
        File.ReadAllText(Path.Combine(target, "src", "functions", "ExactCohort.js"))
            .Should().Contain("app.http('ExactCohort'", "// cohort:" + marker);
    }

    private string CohortArchive(string version, string marker, string rid)
    {
        string path = MigrationRidArchive(version, bundled: true, rid);
        using var outer = ZipFile.Open(path, ZipArchiveMode.Update);
        JsonNode declaration;
        var declared = outer.GetEntry(BundledTemplateInstaller.ManifestName)!;
        using (StreamReader reader = new(declared.Open())) declaration = JsonNode.Parse(reader.ReadToEnd())!;
        JsonNode asset = declaration["assets"]![0]!;
        string innerPath = asset["path"]!.GetValue<string>();
        var nested = outer.GetEntry(innerPath)!;
        using MemoryStream bytes = new();
        using (Stream source = nested.Open()) source.CopyTo(bytes);
        using (ZipArchive inner = new(bytes, ZipArchiveMode.Update, leaveOpen: true))
        {
            const string projectPath = "content/project-js/package.json";
            var projectFile = inner.GetEntry(projectPath)!;
            JsonNode project;
            using (StreamReader reader = new(projectFile.Open())) project = JsonNode.Parse(reader.ReadToEnd())!;
            project["funcTemplateCohort"] = marker;
            byte[] content = Encoding.UTF8.GetBytes(project.ToJsonString());
            Dictionary<string, byte[]> changed = new(StringComparer.Ordinal) { [projectPath] = content };
            projectFile.Delete();
            using (Stream output = inner.CreateEntry(projectPath).Open()) output.Write(content);
            var provenanceFile = inner.GetEntry("provenance/node/compatibility.json")!;
            JsonNode provenance;
            using (StreamReader reader = new(provenanceFile.Open())) provenance = JsonNode.Parse(reader.ReadToEnd())!;
            provenance["packageVersion"] = version;
            int updated = UpdateFixtureSourcePins(provenance, changed);
            updated.Should().BeGreaterThanOrEqualTo(changed.Count);
            provenanceFile.Delete();
            using StreamWriter writer = new(inner.CreateEntry("provenance/node/compatibility.json").Open());
            writer.Write(provenance.ToJsonString());

        }
        nested.Delete();
        byte[] archive = bytes.ToArray();
        using (Stream output = outer.CreateEntry(innerPath).Open()) output.Write(archive);
        asset["sha256"] = Convert.ToHexStringLower(SHA256.HashData(archive));
        AddFunctionFixtureAsset(outer, declaration, version, marker);
        declared.Delete();
        using StreamWriter manifest = new(outer.CreateEntry(BundledTemplateInstaller.ManifestName).Open());
        manifest.Write(declaration.ToJsonString());
        return path;
    }
}
