// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.IO.Compression;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Node;
using Azure.Functions.Cli.Tests.Templates.Engine;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Node;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    [Theory]
    [InlineData("js", ".js", "src/functions/*.js")]
    [InlineData("ts", ".ts", "dist/src/functions/*.js")]
    public async Task RelativePlacement_LegacyRootSpellingsAndNestedBase_FollowOwnedPathWhileLiteralRemainsLiteral(
        string language, string extension, string main)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        using var host = await CreateNodePlacementHostAsync();
        string project = Path.Combine(_root, "relative-" + language);
        await CreatePlacementProjectAsync(host, project, language);
        string package = File.ReadAllText(Path.Combine(project, "package.json"));
        string config = File.ReadAllText(Path.Combine(project, ".func", "config.json"));
        var originalRoot = new WorkingDirectory(new DirectoryInfo(project), WasExplicit: false);
        var explicitRoot = WorkingDirectory.FromExplicit(project);
        var nested = WorkingDirectory.FromExplicit(Path.Combine(project, "nested-target"));
        foreach (var test in new[]
        {
            (Directory: originalRoot, Name: "OmittedRoot"),
            (Directory: explicitRoot, Name: "ExplicitRoot"),
            (Directory: nested, Name: "ExplicitNested"),
        })
        {
            var request = NodeComposedItem(test.Directory.Info.FullName, "HTTP trigger") with
            {
                WorkingDirectory = test.Directory,
                Name = test.Name,
                OutputIntent = NativeItemOutputIntent.LegacyWorkflowRelative,
            };
            string output = Path.Combine(test.Directory.Info.FullName, "src", "functions", test.Name + extension);
            using var invocation = host.CreateInvocation();

            var preview = await invocation.Composer.ExecuteItemAsync(request with { DryRun = true }, CancellationToken.None);
            File.Exists(output).Should().BeFalse();
            var created = await invocation.Composer.ExecuteItemAsync(request, CancellationToken.None);

            preview.Succeeded.Should().BeTrue(string.Join("; ", preview.Diagnostics));
            created.Succeeded.Should().BeTrue(string.Join("; ", created.Diagnostics));
            created.Invocation!.Changes.Should().BeEquivalentTo(preview.Invocation!.Changes);
            created.Context!.CommandDirectory.Should().Be(test.Directory);
            created.Context.Project!.RootDirectory.Info.FullName.Should().Be(project);
            File.ReadAllText(output).Should().Contain($"app.http('{test.Name}'");
            File.Exists(Path.Combine(test.Directory.Info.FullName, test.Name + extension)).Should().BeFalse();
            File.Exists(Path.Combine(test.Directory.Info.FullName, "src", "functions", "src", "functions", test.Name + extension))
                .Should().BeFalse();
        }

        foreach (var directory in new[] { explicitRoot, nested })
        {
            var request = NodeComposedItem(directory.Info.FullName, "HTTP trigger") with { Name = "LiteralControl" };
            request.OutputIntent.Should().Be(NativeItemOutputIntent.LiteralOutput);
            using var invocation = host.CreateInvocation();
            var result = await invocation.Composer.ExecuteItemAsync(request, CancellationToken.None);
            result.Succeeded.Should().BeTrue(string.Join("; ", result.Diagnostics));
            File.ReadAllText(Path.Combine(directory.Info.FullName, "LiteralControl" + extension)).Should().Contain("app.http('LiteralControl'");
        }
        using (var metadata = JsonDocument.Parse(package))
        {
            metadata.RootElement.GetProperty("main").GetString().Should().Be(main);
        }
        File.ReadAllText(Path.Combine(project, "package.json")).Should().Be(package);
        File.ReadAllText(Path.Combine(project, ".func", "config.json")).Should().Be(config);
    }

    [Theory]
    [InlineData("src")]
    [InlineData("src/functions")]
    public async Task RelativePlacement_DerivedPrefixIsAFile_RefusesWithoutChangingUserFile(string prefix)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        using var host = await CreateNodePlacementHostAsync();
        string project = Path.Combine(_root, "prefix-file");
        await CreatePlacementProjectAsync(host, project, "js");
        string path = Path.Combine(project, prefix.Replace('/', Path.DirectorySeparatorChar));
        Directory.Delete(path, recursive: true);
        File.WriteAllText(path, "existing user file");
        var request = NodeComposedItem(project, "HTTP trigger") with
        {
            Name = "BlockedPrefix",
            OutputIntent = NativeItemOutputIntent.LegacyWorkflowRelative,
        };
        using var invocation = host.CreateInvocation();

        var preview = await invocation.Composer.ExecuteItemAsync(request with { DryRun = true }, CancellationToken.None);
        var result = await invocation.Composer.ExecuteItemAsync(request, CancellationToken.None);

        preview.Succeeded.Should().BeFalse();
        result.Succeeded.Should().BeFalse();
        File.ReadAllText(path).Should().Be("existing user file");
        File.Exists(Path.Combine(project, "BlockedPrefix.js")).Should().BeFalse();
    }

    [Fact]
    public async Task RelativePlacement_ConflictAndCancellation_PreserveOriginalUserEffects()
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        using var host = await CreateNodePlacementHostAsync();
        string project = Path.Combine(_root, "placement-conflict");
        await CreatePlacementProjectAsync(host, project, "ts");
        string file = Path.Combine(project, "src", "functions", "ExistingUser.ts");
        File.WriteAllText(file, "// user source");
        var request = NodeComposedItem(project, "HttpTrigger-TypeScript") with
        {
            Name = "ExistingUser",
            OutputIntent = NativeItemOutputIntent.LegacyWorkflowRelative,
        };
        using var invocation = host.CreateInvocation();

        var result = await invocation.Composer.ExecuteItemAsync(request, CancellationToken.None);
        result.Succeeded.Should().BeFalse();
        File.ReadAllText(file).Should().Be("// user source");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        Func<Task> canceled = () => invocation.Composer.ExecuteItemAsync(request with { Name = "CanceledName" }, cancellation.Token);
        await canceled.Should().ThrowAsync<OperationCanceledException>();
        File.Exists(Path.Combine(project, "src", "functions", "CanceledName.ts")).Should().BeFalse();
        File.Exists(Path.Combine(project, "CanceledName.ts")).Should().BeFalse();
        var forced = await invocation.Composer.ExecuteItemAsync(request with { Force = true }, CancellationToken.None);
        forced.Succeeded.Should().BeTrue(string.Join("; ", forced.Diagnostics));
        File.ReadAllText(file).Should().Contain("app.http('ExistingUser'");
    }

    [SymbolicLinkFact]
    public async Task RelativePlacement_DerivedPrefixLink_RefusesBeforePreviewOrCreationOutsideOriginalBoundary()
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        using var host = await CreateNodePlacementHostAsync();
        foreach (string prefix in new[] { "src", "src/functions" })
        {
            string project = Path.Combine(_root, "linked-prefix-" + prefix.Replace('/', '-'));
            await CreatePlacementProjectAsync(host, project, "js");
            string outside = Directory.CreateDirectory(Path.Combine(_root, "outside-" + prefix.Replace('/', '-'))).FullName;
            string link = Path.Combine(project, prefix.Replace('/', Path.DirectorySeparatorChar));
            Directory.Delete(link, recursive: true);
            Directory.CreateSymbolicLink(link, outside);
            try
            {
                var request = NodeComposedItem(project, "HttpTrigger-JavaScript") with
                {
                    Name = "LinkedPrefix",
                    OutputIntent = NativeItemOutputIntent.LegacyWorkflowRelative,
                };
                using var invocation = host.CreateInvocation();

                var preview = await invocation.Composer.ExecuteItemAsync(request with { DryRun = true }, CancellationToken.None);
                var result = await invocation.Composer.ExecuteItemAsync(request, CancellationToken.None);

                preview.Succeeded.Should().BeFalse();
                result.Succeeded.Should().BeFalse();
                Directory.GetFileSystemEntries(outside).Should().BeEmpty();
                File.Exists(Path.Combine(project, "LinkedPrefix.js")).Should().BeFalse();
            }
            finally
            {
                Directory.Delete(link);
            }
        }

    }

    [Fact]
    public async Task RelativePlacement_OwnedArchiveSourceChangesAfterPreview_RefusesStalePlanWithoutWrites()
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        string project = Path.Combine(_root, "source-changed");
        using (var host = await CreateNodePlacementHostAsync())
        {
            await CreatePlacementProjectAsync(host, project, "js");
        }
        var command = WorkingDirectory.FromExplicit(project);
        var context = new TemplateEngineContext(command, new(command, "node", "JavaScript"));
        var availability = new TemplateConstraintEvaluationSnapshot([new("node-stack", NuGetVersion.Parse("1.0.0"), ["node"], true)]);
        TemplateInvocationRequest request;
        string archive;
        using (var session = await _templates.OpenReadAsync(context, CancellationToken.None, availability))
        using (var templater = TemplateFixtures.CreateTemplater(session))
        {
            var selected = (await templater.ResolveGroupAsync("HttpTrigger-JavaScript", TemplateType.Item, CancellationToken.None)).Group!.Single();
            var source = await NodeItemCompatibility.ReadSourceAsync(selected, CancellationToken.None);
            var arguments = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(), []));
            request = new(command, arguments.ValuesByCanonicalName, "ChangedSource")
            {
                SourceSnapshot = new(selected.Entry.Identity, selected.ConfigurationSha256!, source.Sources,
                    [new(Path.GetFileName(source.ContentPath), source.ContentSource)]),
            };
            var preview = await selected.InvokeAsync(request with { DryRun = true }, CancellationToken.None);
            preview.Status.Should().Be(TemplateInvocationStatus.Success, preview.Diagnostic);
            archive = (await session.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None)).Single().MountPointUri;
        }
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Update))
        {
            const string source = "provenance/node/v2/templates/templates.json";
            var entry = zip.GetEntry(source)!;
            string text;
            using (var reader = new StreamReader(entry.Open())) text = reader.ReadToEnd();
            entry.Delete();
            using var writer = new StreamWriter(zip.CreateEntry(source).Open());
            writer.Write(text + "\n");
        }
        using var changedSession = await _templates.OpenReadAsync(context, CancellationToken.None, availability);
        using var changedTemplates = TemplateFixtures.CreateTemplater(changedSession);
        var current = (await changedTemplates.ResolveGroupAsync("HttpTrigger-JavaScript", TemplateType.Item, CancellationToken.None)).Group!.Single();

        var result = await current.InvokeAsync(request, CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.TemplateChanged);
        result.Diagnostic.Should().Contain("source changed");
        File.Exists(Path.Combine(project, "src", "functions", "ChangedSource.js")).Should().BeFalse();
        File.Exists(Path.Combine(project, "ChangedSource.js")).Should().BeFalse();
    }

    [SymbolicLinkFact]
    public async Task RelativePlacement_LinkAppearsAfterNativePreview_SharedGuardRefusesLiveWrite()
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        string project = Path.Combine(_root, "late-link-project");
        using (var host = await CreateNodePlacementHostAsync())
        {
            await CreatePlacementProjectAsync(host, project, "js");
        }
        var command = WorkingDirectory.FromExplicit(project);
        var context = new TemplateEngineContext(command, new(command, "node", "JavaScript"),
            itemOutputIntent: NativeItemOutputIntent.LegacyWorkflowRelative);
        var availability = new TemplateConstraintEvaluationSnapshot([new("node-stack", NuGetVersion.Parse("1.0.0"), ["node"], true)]);
        string outside = Directory.CreateDirectory(Path.Combine(_root, "late-link-outside")).FullName;
        string link = Path.Combine(project, "src", "functions");
        using var session = new NodeAfterPreviewSession(context, Path.Combine(Home, "templates"), availability)
        {
            AfterPreview = () =>
            {
                Directory.Delete(link);
                Directory.CreateSymbolicLink(link, outside);
            },
        };
        using var templater = TemplateFixtures.CreateTemplater(session);
        var selected = (await templater.ResolveGroupAsync("HttpTrigger-JavaScript", TemplateType.Item, CancellationToken.None)).Group!.Single();
        var source = await NodeItemCompatibility.ReadSourceAsync(selected, CancellationToken.None);
        var parsed = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(), []));
        var request = new TemplateInvocationRequest(command, parsed.ValuesByCanonicalName, "LateLinked")
        {
            SourceSnapshot = new(selected.Entry.Identity, selected.ConfigurationSha256!, source.Sources,
                [new(Path.GetFileName(source.ContentPath), source.ContentSource)]),
        };
        try
        {
            var result = await selected.InvokeAsync(request, CancellationToken.None);

            result.Status.Should().NotBe(TemplateInvocationStatus.Success);
            session.AfterPreview.Should().BeNull();
            session.CreationCalls.Should().Be(0);
            Directory.GetFileSystemEntries(outside).Should().BeEmpty();
            File.Exists(Path.Combine(project, "LateLinked.js")).Should().BeFalse();
        }
        finally
        {
            if (new DirectoryInfo(link).LinkTarget is not null)
            {
                Directory.Delete(link);
            }
        }

    }

    [Theory]
    [InlineData("extra.txt")]
    [InlineData("extra-directory/_._")]
    public async Task RelativePlacement_UnexpectedNativeSourceOrPlaceholder_RefusesInBothModes(string extra)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        using var host = await CreateNodePlacementHostAsync();
        string project = Path.Combine(_root, "unexpected-source");
        await CreatePlacementProjectAsync(host, project, "js");
        string archive;
        using (var reader = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(project)), CancellationToken.None))
        {
            archive = (await reader.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None)).Single().MountPointUri;
        }
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Update))
        {
            using var writer = new StreamWriter(zip.CreateEntry("content/http-js/" + extra).Open());
            if (!extra.EndsWith("_._", StringComparison.Ordinal))
            {
                writer.Write("extra native output must not bypass the source snapshot");
            }
        }
        foreach (var intent in new[] { NativeItemOutputIntent.LiteralOutput, NativeItemOutputIntent.LegacyWorkflowRelative })
        {
            using var invocation = host.CreateInvocation();
            var request = NodeComposedItem(project, "HttpTrigger-JavaScript") with { Name = "ExpectedOnly", OutputIntent = intent };

            var result = await invocation.Composer.ExecuteItemAsync(request, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            File.Exists(Path.Combine(project, "ExpectedOnly.js")).Should().BeFalse();
            File.Exists(Path.Combine(project, "src", "functions", "ExpectedOnly.js")).Should().BeFalse();
            File.Exists(Path.Combine(project, "extra.txt")).Should().BeFalse();
            File.Exists(Path.Combine(project, "src", "functions", "extra.txt")).Should().BeFalse();
            Directory.Exists(Path.Combine(project, "extra-directory")).Should().BeFalse();
            Directory.Exists(Path.Combine(project, "src", "functions", "extra-directory")).Should().BeFalse();
        }
    }

    private sealed class NodeAfterPreviewSession(TemplateEngineContext context, string hive, TemplateConstraintEvaluationSnapshot availability)
        : TemplateEngineSession(context, hive, availability)
    {
        public Action? AfterPreview { get; set; }
        public int CreationCalls { get; private set; }

        protected override async Task<ITemplateCreationResult> InstantiateAsync(ITemplateInfo template, TemplateInvocationRequest request,
            InputDataSet parameters, bool dryRun, CancellationToken cancellationToken)
        {
            var result = await base.InstantiateAsync(template, request, parameters, dryRun, cancellationToken);
            if (dryRun)
            {
                Action? action = AfterPreview;
                AfterPreview = null;
                action?.Invoke();
            }
            else
            {
                CreationCalls++;
            }
            return result;
        }
    }

    private async Task<NativeTemplateWorkflowHost> CreateNodePlacementHostAsync() =>
        await new NativeTemplateWorkflowHostFactory().CreateAsync(_interaction, _paths, new(Home), services =>
        {
            _loadContexts.AddRange(services.Select(service => service.ImplementationInstance).OfType<RuntimeWorkloadInfo>()
                .Select(workload => workload.LoadContext));
            services.AddScoped<NodeNpmInstaller>();
            services.AddScoped<NodeTemplateWorkflowProvider>();
            services.AddScoped<INativeItemWorkflowProvider>(provider => provider.GetRequiredService<NodeTemplateWorkflowProvider>());
            services.AddScoped<INativeProjectWorkflowProvider>(provider => provider.GetRequiredService<NodeTemplateWorkflowProvider>());
        }, enableBundleBindingConstraints: true, CancellationToken.None);

    private static async Task CreatePlacementProjectAsync(NativeTemplateWorkflowHost host, string project, string language)
    {
        using var invocation = host.CreateInvocation();
        var request = NodeComposedInit(project, language, ["--no-bundles", "true", "--skip-npm-install", "true"]);
        var result = await invocation.Composer.ExecuteProjectAsync(request, CancellationToken.None);
        result.Succeeded.Should().BeTrue(string.Join("; ", result.Diagnostics));
    }
}
