// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.V2;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Python;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NuGet.Packaging;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledPythonTemplateTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("func-native-python-").FullName;
    private readonly string _archive;
    private readonly List<NewTemplate> _sources;
    private readonly JsonDocument _channels;
    private readonly JsonDocument _applications;
    private readonly HashSet<string> _helperIdentities;
    private bool _loadedBootWorkload;

    public BundledPythonTemplateTests()
    {
        using var outer = new PackageArchiveReader(BuiltArchive);
        _archive = Path.Combine(_root, "python.nupkg");
        using (var output = File.Create(_archive))
        {
            outer.GetStream("tools/any/templates/python.nupkg").CopyTo(output);
        }

        using var inner = new PackageArchiveReader(_archive);
        _sources = JsonSerializer.Deserialize<List<NewTemplate>>(Read(inner, "source/templates.json"))!;
        _channels = JsonDocument.Parse(Read(inner, "source/channels.json"));
        _applications = JsonDocument.Parse(Read(inner, "source/applications.json"));
        _helperIdentities = Applications.EnumerateArray()
            .Select(application => application.GetProperty("appendHelper").GetProperty("templateIdentity").GetString()!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private string Hive => Path.Combine(_root, "hive");

    private JsonElement Applications => _applications.RootElement.GetProperty("applications");

    private static string BuiltArchive => Directory.GetFiles(
        Path.Combine(AppContext.BaseDirectory, "bundled-python-artifacts"), "*.nupkg").Single();

    [Fact]
    public void Pack_ActualPythonWorkload_OwnsNativeArchiveCompleteInventoryAndSourceProvenance()
    {
        using var outer = new PackageArchiveReader(BuiltArchive);
        using var declaration = JsonDocument.Parse(Read(outer, "bundled-templates.json"));
        var asset = declaration.RootElement.GetProperty("assets").EnumerateArray().Single();
        using var inner = new PackageArchiveReader(_archive);

        outer.NuspecReader.GetPackageTypes().Select(type => type.Name).Should().Contain("FuncCliWorkload").And.NotContain("FuncTemplate");
        inner.NuspecReader.GetPackageTypes().Select(type => type.Name).Should().Equal("FuncTemplate");
        inner.NuspecReader.GetId().Should().Be("Azure.Functions.Templates.Python");
        inner.NuspecReader.GetVersion().Should().Be(outer.NuspecReader.GetVersion());
        asset.GetProperty("path").GetString().Should().Be("tools/any/templates/python.nupkg");
        asset.GetProperty("sha256").GetString().Should().Be(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(_archive))));
        asset.GetProperty("templates").GetArrayLength().Should().Be(51);
        inner.GetFiles().Should().Contain("LICENSE", "README.md", "content/project/.gitignore", "source/channels.json");
        inner.GetFiles().Count(path => path.StartsWith("snippets/", StringComparison.Ordinal)).Should().Be(25);
        _sources.Should().HaveCount(25).And.OnlyContain(source => source.ProgrammingModel == "v2");
        _channels.RootElement.EnumerateArray().Select(channel => channel.GetProperty("templateIds").GetArrayLength())
            .Should().Equal(18, 24, 23);
        _channels.RootElement[0].GetProperty("sha256").GetString().Should()
            .Be("68d3926ba750e678fa93db03d118b7cf58b127aa14d0583eab402037b1c6e783");
        foreach (var channel in _channels.RootElement.EnumerateArray())
        {
            using var metadata = inner.GetStream($"source/bundles/{channel.GetProperty("channel").GetString()}/extensions.json");
            Convert.ToHexStringLower(SHA256.HashData(metadata)).Should().Be(channel.GetProperty("extensionsSha256").GetString());
        }

        _applications.RootElement.GetProperty("packageId").GetString().Should().Be(inner.NuspecReader.GetId());
        _applications.RootElement.GetProperty("version").GetString().Should().Be(inner.NuspecReader.GetVersion().ToNormalizedString());
        foreach (var descriptor in _applications.RootElement.GetProperty("sources").EnumerateArray()
            .Concat(_applications.RootElement.GetProperty("project").GetProperty("sources").EnumerateArray()))
        {
            using var bytes = inner.GetStream(descriptor.GetProperty("path").GetString()!);
            Convert.ToHexStringLower(SHA256.HashData(bytes)).Should().Be(descriptor.GetProperty("sha256").GetString()!.ToLowerInvariant());
        }
        Applications.GetArrayLength().Should().Be(25);
        _helperIdentities.Should().HaveCount(25);
        Applications.EnumerateArray()
            .Where(application => application.GetProperty("legacyCreateTarget").GetString() == "function_app.py.py")
            .Select(application => application.GetProperty("templateIdentity").GetString()).Should().BeEquivalentTo(
                "Azure.Functions.Python.DurableFunctionsEntityTrigger", "Azure.Functions.Python.DurableFunctionsOrchestration",
                "Azure.Functions.Python.EventGridTrigger", "Azure.Functions.Python.ServiceBusTopicTrigger");
        foreach (var application in Applications.EnumerateArray())
        {
            using var create = inner.GetStream(application.GetProperty("createSource").GetString()!);
            using var append = inner.GetStream(application.GetProperty("appendSource").GetString()!);
            Convert.ToHexStringLower(SHA256.HashData(create)).Should().Be(application.GetProperty("createSourceSha256").GetString());
            Convert.ToHexStringLower(SHA256.HashData(append)).Should().Be(application.GetProperty("appendSourceSha256").GetString());
            application.GetProperty("createTarget").GetString().Should().Be("function_app.py");
            application.GetProperty("appendTarget").GetString().Should().Be("function_app.py");
            application.GetProperty("nativeFilenameCorrected").GetBoolean().Should()
                .Be(application.GetProperty("legacyCreateTarget").GetString() == "function_app.py.py");
            var helper = application.GetProperty("appendHelper");
            using var helperSource = inner.GetStream(helper.GetProperty("source").GetString()!);
            Convert.ToHexStringLower(SHA256.HashData(helperSource)).Should().Be(application.GetProperty("appendSourceSha256").GetString());
            helper.GetProperty("sourceSha256").GetString().Should().Be(application.GetProperty("appendSourceSha256").GetString());
            helper.GetProperty("expectedOutput").GetString().Should().Be("function_body.py");
            helper.GetProperty("expectedOutputKind").GetString().Should().Be("File");
            helper.GetProperty("role").GetString().Should().Be("internal-expansion");
            foreach (var configurationSource in new[] { application, helper })
            {
                using var configuration = inner.GetStream(configurationSource.GetProperty("configuration").GetString()!);
                Convert.ToHexStringLower(SHA256.HashData(configuration)).Should().Be(configurationSource.GetProperty("configurationSha256").GetString());
            }
        }

        foreach (var source in _sources)
        {
            var stem = source.Id![..^"-Python".Length];
            Read(inner, $"snippets/{stem}/function_body.py").Should()
                .Be(source.Files!["function_body.py"].Replace("$(FUNCTION_NAME_INPUT)", "__FUNCTION_NAME__", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Install_ActualOuterArchive_RegistersOwnedNativeCatalogAndRepeatsWithoutTemplateDownload()
    {
        PhysicalFileSystem files = new();
        var home = Path.Combine(_root, "cli-home");
        WorkloadPathsOptions paths = new(Path.Combine(_root, "workload-home"));
        FileWorkloadOperationCoordinator coordinator = new(paths);
        WorkloadStore workloads = new(paths, coordinator);
        TemplateStore templates = new(new(home), files, new FileTemplateStoreLock(), NullLoggerFactory.Instance);
        BundledTemplateInstaller bundles = new(templates, new BasicArtifactInspector(files), files, new(home));
        WorkloadMetadataReader metadata = new();
        var runtime = Substitute.For<IWorkloadRuntimeIdentifierProvider>();
        runtime.Current.Returns("win-x64");
        WorkloadRidPackageSelector selector = new(runtime);
        var source = Substitute.For<IWorkloadPackageSource>();
        WorkloadInstaller installer = new(source, new WorkloadPackageInspector(metadata, selector), selector,
            new WorkloadDeploymentService(paths, workloads, metadata, coordinator, files, bundles), coordinator, workloads, files);

        var installed = await installer.InstallFromPackageAsync(BuiltArchive);
        var repeated = await installer.InstallFromPackageAsync(BuiltArchive);

        installed.AlreadyInstalled.Should().BeFalse();
        repeated.AlreadyInstalled.Should().BeTrue();
        (await workloads.GetWorkloadsAsync()).Should().ContainSingle().Which.Aliases.Should().Contain("python");
        using var session = await templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        var packages = await session.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None);
        packages.Should().ContainSingle().Which.Identifier.Should().Be("Azure.Functions.Templates.Python");
        using var templater = new Templater(session);
        (await templater.ListAsync(TemplateType.Project)).Should().ContainSingle().Which.Identity.Should().Be("Azure.Functions.Python.Basic");
        var items = await templater.ListAsync(TemplateType.Item);
        items.Where(item => !_helperIdentities.Contains(item.Identity)).Should().HaveCount(25);
        items.Where(item => _helperIdentities.Contains(item.Identity)).Should().HaveCount(25).And.OnlyContain(item => item.IsHidden);
        source.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("Stable", false)]
    [InlineData("Preview", false)]
    [InlineData("Experimental", false)]
    [InlineData("Stable", true)]
    public async Task Basic_ActualNativeGeneration_MatchesInitializerAndCanonicalProjectConfiguration(string channel, bool noBundles)
    {
        await InstallAsync();
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "native"));
        using var templater = CreateTemplater();
        var resolution = await templater.ResolveGroupAsync("basic", TemplateType.Project);
        var selected = resolution.Group!.Should().ContainSingle().Subject;
        var parameters = new Dictionary<string, string?>
        {
            ["BundlesChannel"] = channel,
            ["NoBundles"] = noBundles.ToString(),
        };
        var request = new TemplateInvocationRequest(output, parameters, "Sample", DryRun: true);

        var preview = await selected.InvokeAsync(request, CancellationToken.None);
        var previewWroteFiles = Directory.Exists(output.Info.FullName);
        var created = await selected.InvokeAsync(request with { DryRun = false }, CancellationToken.None);

        preview.Status.Should().Be(TemplateInvocationStatus.Success, preview.Diagnostic);
        previewWroteFiles.Should().BeFalse();
        created.Status.Should().Be(TemplateInvocationStatus.Success, created.Diagnostic);
        var legacy = WorkingDirectory.FromExplicit(Path.Combine(_root, "legacy"));
        Directory.CreateDirectory(legacy.Info.FullName);
        PythonProjectInitializer initializer = new(new PythonProjectStack());
        RootCommand command = [];
        initializer.GetInitOptions(new InitOptionRegistry(command));
        string[] args = noBundles ? ["--no-bundles"] : ["--bundles-channel", channel];
        await initializer.InitializeAsync(new(legacy, "Sample", "py", false), command.Parse(args));
        foreach (var path in Directory.GetFiles(legacy.Info.FullName))
        {
            var native = File.ReadAllText(Path.Combine(output.Info.FullName, Path.GetFileName(path)));
            var original = File.ReadAllText(path);
            if (Path.GetFileName(path) == "host.json")
            {
                JsonSerializer.Serialize(JsonDocument.Parse(native).RootElement).Should()
                    .Be(JsonSerializer.Serialize(JsonDocument.Parse(original).RootElement));
            }
            else
            {
                native.Should().Be(original);
            }
        }

        var configuration = new ConfigurationBuilder().AddJsonFile(Path.Combine(output.Info.FullName, ".func", "config.json")).Build();
        StackOptions options = new();
        new StackOptionsSetup(configuration).Configure(options);
        options.Runtime.Should().Be("python");
        options.Language.Should().Be("Python");
        var project = await new PythonProjectFactory().TryCreateProjectAsync(new(output), CancellationToken.None);
        project.Should().BeOfType<ProjectCreationResult.Created>().Which.Project.Should().BeOfType<PythonFunctionsProject>();
        Directory.Exists(Path.Combine(output.Info.FullName, ".venv")).Should().BeFalse();
    }

    [Theory]
    [InlineData("stable", 16)]
    [InlineData("preview", 22)]
    [InlineData("experimental", 21)]
    public async Task Items_AllPinnedChannelRecords_PreserveLegacyCreateBytesWithAuthorizedNativeFilenameCorrection(string channel, int visible)
    {
        await InstallAsync();
        var channelSource = _channels.RootElement.EnumerateArray().Single(entry => entry.GetProperty("channel").GetString() == channel);
        var bundle = await ReadBundleAsync(channel);
        using var templater = CreateTemplater(bundle);
        var entries = await templater.ListAsync(TemplateType.Item);
        var eligible = entries.Where(entry => !_helperIdentities.Contains(entry.Identity)
            && entry.ConstraintEvaluation.Status == TemplateConstraintEvaluationStatus.Eligible).ToList();
        var ids = channelSource.GetProperty("templateIds").EnumerateArray().Select(id => id.GetString()!).ToList();

        eligible.Select(entry => entry.ShortNames.Single()).Should().BeEquivalentTo(ids);
        eligible.Count(entry => !entry.IsHidden).Should().Be(visible);
        entries.Should().OnlyContain(entry => entry.MetadataDiagnostic == null);
        foreach (var id in ids)
        {
            var source = _sources.Single(source => source.Id == id);
            var entry = eligible.Single(entry => entry.ShortNames.Contains(id));
            var resolution = await templater.ResolveGroupAsync(id, TemplateType.Item);
            var selected = resolution.Group!.Should().ContainSingle().Subject;
            var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "native", id));
            var parameters = entry.Parameters.ToDictionary(parameter => parameter.CanonicalName, parameter => parameter.DefaultValue, StringComparer.Ordinal);
            var request = new TemplateInvocationRequest(output, parameters, "9 Greeting-\u4e16\u754c", DryRun: true);

            var preview = await selected.InvokeAsync(request, CancellationToken.None);
            preview.Status.Should().Be(TemplateInvocationStatus.Success, $"{id}: {preview.Diagnostic}");
            Directory.Exists(output.Info.FullName).Should().BeFalse();
            var created = await selected.InvokeAsync(request with { DryRun = false }, CancellationToken.None);

            created.Status.Should().Be(TemplateInvocationStatus.Success, $"{id}: {created.Diagnostic}");
            var legacy = Directory.CreateDirectory(Path.Combine(_root, "legacy", id));
            var values = new Dictionary<string, string?>(parameters) { ["trigger-functionName"] = "9 Greeting-\u4e16\u754c" };
            new V2TemplateEngine().Apply(source, "9 Greeting-\u4e16\u754c", values, legacy, false)
                .Should().BeOfType<TemplateApplicationResult.Created>();
            var legacyFile = Directory.GetFiles(legacy.FullName).Should().ContainSingle().Subject;
            var nativeFileName = _authorizedFilenameCorrections.Contains(id) ? "function_app.py" : Path.GetFileName(legacyFile);
            File.ReadAllText(Path.Combine(output.Info.FullName, nativeFileName)).Should()
                .Be(File.ReadAllText(legacyFile), id);
            if (_authorizedFilenameCorrections.Contains(id))
            {
                Path.GetFileName(legacyFile).Should().Be("function_app.py.py", "the unchanged V2 path remains BEFORE evidence");
                File.Exists(Path.Combine(output.Info.FullName, "function_app.py.py")).Should().BeFalse();
            }
            Directory.GetFiles(output.Info.FullName, "*", SearchOption.AllDirectories).Should().ContainSingle();
        }
    }

    [Fact]
    public async Task Http_ExplicitOriginalAliasAndAuthorization_GeneratesChosenNativeValue()
    {
        await InstallAsync();
        using var templater = CreateTemplater(new("Microsoft.Azure.Functions.ExtensionBundle", "4.35.0"));
        var selected = (await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item)).Group!.Single();
        var parameter = selected.Entry.Parameters.Should().ContainSingle().Subject;
        parameter.CanonicalName.Should().Be("httpTrigger-authLevel");
        parameter.LongName.Should().Be("http-trigger-auth-level");
        parameter.IsRequired.Should().BeTrue();
        parameter.DefaultValue.Should().Be("FUNCTION");
        parameter.Choices.Select(choice => choice.Value).Should().BeEquivalentTo("FUNCTION", "ANONYMOUS", "ADMIN");
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "http"));

        var created = await selected.InvokeAsync(new(output,
            new Dictionary<string, string?> { ["httpTrigger-authLevel"] = "ANONYMOUS" }, "Hello"), CancellationToken.None);

        created.Status.Should().Be(TemplateInvocationStatus.Success, created.Diagnostic);
        File.ReadAllText(Path.Combine(output.Info.FullName, "function_app.py")).Should()
            .Contain("func.AuthLevel.ANONYMOUS", "def Hello(").And.NotContain("$(", "__FUNCTION_NAME__");
    }

    [Fact]
    public async Task ExistingApp_RealLegacyAppendPreservesUserCode_WhileUnintegratedNativeCreateHonestlyRefuses()
    {
        await InstallAsync();
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "existing"));
        Directory.CreateDirectory(output.Info.FullName);
        var app = Path.Combine(output.Info.FullName, "function_app.py");
        const string original = "import json\nimport logging\nimport azure.functions as func\napp = func.FunctionApp()\n@app.route(route='existing')\ndef existing(req):\n    return func.HttpResponse('user code')";
        File.WriteAllText(app, original);
        using var templater = CreateTemplater(new("Microsoft.Azure.Functions.ExtensionBundle", "4.35.0"));
        var selected = (await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item)).Group!.Single();
        var values = new Dictionary<string, string?> { ["httpTrigger-authLevel"] = "ANONYMOUS" };

        var native = await selected.InvokeAsync(new(output, values, "Greeting"), CancellationToken.None);

        native.Status.Should().Be(TemplateInvocationStatus.FileConflict);
        File.ReadAllText(app).Should().Be(original);
        values["trigger-functionName"] = "Greeting";
        var legacy = new V2TemplateEngine().Apply(_sources.Single(source => source.Id == "HttpTrigger-Python"),
            "Greeting", values, output.Info, false);
        legacy.Should().BeOfType<TemplateApplicationResult.Created>();
        File.ReadAllText(app).Should().StartWith(original + Environment.NewLine).And.Contain(
            "@app.route(route=\"Greeting\", auth_level=func.AuthLevel.ANONYMOUS)", "def Greeting(");
        File.ReadAllText(app).Split("import azure.functions as func", StringSplitOptions.None).Should().HaveCount(2);
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData("Microsoft.Azure.Functions.ExtensionBundle", "3.0.0", true)]
    [InlineData("unrelated", "4.35.0", true)]
    [InlineData("Microsoft.Azure.Functions.ExtensionBundle", "4.35.0", false)]
    public async Task Items_MissingWrongBundleOrUnusableStack_RefuseWithoutOutput(string? bundleId, string? version, bool usable)
    {
        await InstallAsync();
        using var templater = CreateTemplater(bundleId is null ? null : new(bundleId, version!), usable);

        var resolution = await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item);

        resolution.Status.Should().Be(TemplateResolutionStatus.Unavailable);
        Directory.Exists(Path.Combine(_root, "output")).Should().BeFalse();
    }

    [Fact]
    public async Task Item_PreCanceledInvocation_PreservesExistingApp()
    {
        await InstallAsync();
        using var templater = CreateTemplater(new("Microsoft.Azure.Functions.ExtensionBundle", "4.35.0"));
        var selected = (await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item)).Group!.Single();
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "canceled"));
        Directory.CreateDirectory(output.Info.FullName);
        var app = Path.Combine(output.Info.FullName, "function_app.py");
        File.WriteAllText(app, "user code");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Func<Task> invoke = () => selected.InvokeAsync(new(output,
            new Dictionary<string, string?> { ["httpTrigger-authLevel"] = "FUNCTION" }, "Greeting"), cancellation.Token);

        await invoke.Should().ThrowAsync<OperationCanceledException>();
        File.ReadAllText(app).Should().Be("user code");
        Directory.GetFiles(output.Info.FullName).Should().ContainSingle();
    }

    [Theory]
    [InlineData("stable", "DaprPublishOutputBinding-Python", "daprTopicTrigger")]
    [InlineData("stable", "DurableFunctionsOrchestration-Python", "activitytrigger")]
    [InlineData("preview", "OpenAI-RAG-Python", "SemanticSearch")]
    [InlineData("preview", "OpenAI-Assistant-Python", "AssistantPost")]
    public async Task BindingConstraints_MissingSecondaryRequirement_RefuseDespiteMatchingBundleIdVersionAndPrimaryBinding(
        string channel, string template, string missing)
    {
        await InstallAsync();
        var bundle = await ReadBundleAsync(channel, missing);
        using var templater = CreateTemplater(bundle);

        var resolution = await templater.ResolveGroupAsync(template, TemplateType.Item);
        var http = await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item);

        resolution.Status.Should().Be(TemplateResolutionStatus.Unavailable);
        http.Status.Should().Be(TemplateResolutionStatus.Success);
        Directory.Exists(Path.Combine(_root, "output")).Should().BeFalse();
    }

    [Fact]
    public async Task BindingConstraints_IdVersionWithoutActualMetadata_RefusesBindingItemButNotExplicitDependencyFreeHttp()
    {
        await InstallAsync();
        using var templater = CreateTemplater(new("Microsoft.Azure.Functions.ExtensionBundle", "4.35.0"));

        var blob = await templater.ResolveGroupAsync("BlobTrigger-Python", TemplateType.Item);
        var http = await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item);

        blob.Status.Should().Be(TemplateResolutionStatus.Unavailable);
        http.Status.Should().Be(TemplateResolutionStatus.Success);
    }

    [Fact]
    public async Task BindingConstraints_DefaultFlowNotOptedIn_RemainsUnavailable()
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"), enableBindingConstraints: false);

        var resolution = await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item);

        resolution.Status.Should().Be(TemplateResolutionStatus.Unavailable);
    }

    private async Task InstallAsync() => await TemplateFixtures.InstallAsync(_archive, Hive);

    private async Task<TemplateEngineBundleContext> ReadBundleAsync(string channel, string? missingBinding = null,
        CancellationToken cancellationToken = default)
    {
        using var archive = new PackageArchiveReader(_archive);
        var snapshot = _channels.RootElement.EnumerateArray().Single(entry => entry.GetProperty("channel").GetString() == channel);
        var root = Path.Combine(_root, "selected-bundles", channel);
        Directory.CreateDirectory(Path.Combine(root, "bin"));
        var path = Path.Combine(root, "bin", "extensions.json");
        var json = Read(archive, $"source/bundles/{channel}/extensions.json");
        if (missingBinding is not null)
        {
            var node = JsonNode.Parse(json)!;
            foreach (var extension in node["extensions"]!.AsArray())
            {
                if (extension!["bindings"] is JsonArray bindings)
                {
                    for (var index = bindings.Count - 1; index >= 0; index--)
                    {
                        if (string.Equals(bindings[index]!.GetValue<string>(), missingBinding, StringComparison.OrdinalIgnoreCase))
                        {
                            bindings.RemoveAt(index);
                        }
                    }
                }
            }

            json = node.ToJsonString();
        }

        await File.WriteAllTextAsync(path, json, cancellationToken);
        return await new TemplateEngineBundleContextReader(new PhysicalFileSystem()).ReadAsync(
            new ExtensionBundleResolution.Resolved(snapshot.GetProperty("bundleId").GetString()!,
                snapshot.GetProperty("bundleVersion").GetString()!, root, null), cancellationToken);
    }

    private Templater CreateTemplater(TemplateEngineBundleContext? bundle = null, bool usable = true, bool enableBindingConstraints = true)
    {
        var directory = WorkingDirectory.FromExplicit(_root);
        var context = new TemplateEngineContext(directory, new(directory, "python", "Python"), bundle, enableBindingConstraints);
        var snapshot = new TemplateConstraintEvaluationSnapshot([new("python-stack", NuGetVersion.Parse("1.0.0"), ["python"], usable)]);
        TemplateProjectConfigurationPlanner planner = new(new([new PythonProjectStack()]));
        return new(new TemplateEngineSession(context, Hive, snapshot,
            new(planner, new TemplateProjectConfigurationWriter(new PhysicalFileSystem()))));
    }

    private static string Read(PackageArchiveReader package, string path)
    {
        using StreamReader reader = new(package.GetStream(path));
        return reader.ReadToEnd();
    }

    public void Dispose()
    {
        _channels.Dispose();
        _applications.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (UnauthorizedAccessException) when (_loadedBootWorkload)
        {
            // Windows can retain this test's loaded workload DLL until the process exits, even after its load context unloads.
        }
    }
}
