// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Profiles;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Node;
using Azure.Functions.Cli.Templates.V2;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NuGet.Packaging;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    // Independent private payload fixture, not a claim about a published bundle/channel.
    private const string NodeBridgeExtensions = """
        {"extensions":[
          {"name":"storage","bindings":["BLOBTRIGGER","queueTrigger"]},
          {"name":"events","bindings":["eventGridTrigger","EventHubTrigger","serviceBusTrigger"]},
          {"name":"cosmos","bindings":["cosmosDBTrigger"]},
          {"name":"durable","bindings":["entityTrigger","orchestrationTrigger"]},
          {"name":"dapr","bindings":["daprPublish","daprServiceInvocationTrigger","daprTopicTrigger"]},
          {"name":"mcp","bindings":["mcpPromptTrigger","mcpResourceTrigger","mcpToolTrigger"]}
        ]}
        """;

    [Fact]
    public void BridgePack_ProvenanceContainsExactOriginalBytesAndActualNativeHashes()
    {
        using var inner = ReadNodeArchive();
        using var manifest = JsonDocument.Parse(Read(inner, NodeItemCompatibility.ManifestPath));
        var root = manifest.RootElement;
        root.GetProperty("packageId").GetString().Should().Be(inner.NuspecReader.GetId());
        root.GetProperty("packageVersion").GetString().Should().Be(inner.NuspecReader.GetVersion().ToNormalizedString());
        var descriptors = root.GetProperty("sources").EnumerateArray().ToArray();
        descriptors.Should().HaveCount(4);
        foreach (var descriptor in descriptors)
        {
            string path = descriptor.GetProperty("path").GetString()!;
            byte[] original = File.ReadAllBytes(Path.Combine(NodeSourceDirectory(), path["provenance/node/v2/".Length..]));
            using var source = inner.GetStream(path);
            using var bytes = new MemoryStream();
            source.CopyTo(bytes);
            bytes.ToArray().Should().Equal(original);
            descriptor.GetProperty("sha256").GetString().Should().BeEquivalentTo(Convert.ToHexString(SHA256.HashData(original)));
        }
        foreach (var mapping in root.GetProperty("templates").EnumerateArray())
        {
            string[] names = ["configuration", "host", "content"];
            foreach (string name in names)
            {
                var descriptor = mapping.GetProperty(name);
                using var stream = inner.GetStream(descriptor.GetProperty("path").GetString()!);
                descriptor.GetProperty("sha256").GetString().Should().BeEquivalentTo(Convert.ToHexString(SHA256.HashData(stream)));
            }
        }
        foreach (var mapping in root.GetProperty("projects").EnumerateArray())
        {
            string[] names = ["configuration", "host", "package"];
            foreach (string name in names)
            {
                var descriptor = mapping.GetProperty(name);
                using var stream = inner.GetStream(descriptor.GetProperty("path").GetString()!);
                descriptor.GetProperty("sha256").GetString().Should().BeEquivalentTo(Convert.ToHexString(SHA256.HashData(stream)));
            }
        }
    }

    [Theory]
    [InlineData("js", "JavaScript", false)]
    [InlineData("js", "JavaScript", true)]
    [InlineData("ts", "TypeScript", false)]
    [InlineData("ts", "TypeScript", true)]
    public async Task Bridge_AllOwnedItems_UsesActualSelectedSourceDefaultOrExplicitNameAndPreservesUserFiles(
        string alias, string language, bool explicitName)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        using var bridge = await CreateNodeBridgeAsync();
        string project = Path.Combine(_root, "node-project");
        await CreateNodeBridgeProjectAsync(bridge, project, alias);
        string target = Path.Combine(project, "src", "functions");
        string user = Path.Combine(target, "existing-user.js");
        File.WriteAllText(user, "// preserve existing user code\n");
        string host = File.ReadAllText(Path.Combine(project, "host.json"));
        string package = File.ReadAllText(Path.Combine(project, "package.json"));
        string config = File.ReadAllText(Path.Combine(project, ".func", "config.json"));
        var raw = ReadNodeSource().Templates.Where(template => template.Language!.Equals(language, StringComparison.OrdinalIgnoreCase)).ToArray();
        var index = 0;
        foreach (var template in raw)
        {
            string name = explicitName ? "ExplicitFunction" + index++ : template.Jobs![0].Inputs![0].DefaultValue!;
            string[] names = explicitName ? ["--name", name] : [];
            string reference = explicitName ? template.Id! : template.Name!;
            string[] tokens = [target, "--template", reference, "--non-interactive", "--output", "json", .. names];

            (await InvokeNodeBridgeAsync(bridge.Adapter.ParseNewInvocation(bridge.NewCommand(), tokens, dryRun: true)))
                .Should().Be(0, _interaction.AllOutput);
            var preview = bridge.Runner.Last!;
            preview.ItemName!.Name.Should().Be(name);
            preview.ItemName.Origin.Should().Be(explicitName ? "explicit" : "authoredDefault");
            string extension = language == "JavaScript" ? ".js" : ".ts";
            File.Exists(Path.Combine(target, name + extension)).Should().BeFalse();

            (await InvokeNodeBridgeAsync(bridge.Adapter.ParseNewInvocation(bridge.NewCommand(), tokens)))
                .Should().Be(0, _interaction.AllOutput);
            var created = bridge.Runner.Last!;
            created.SelectedTemplate!.Identity.Should().Be(NativeNodeIdentity(template.Id!));
            created.Context!.Bundle!.Bindings!.Contains("BLOBTRIGGER").Should().BeTrue();
            created.ItemName.Should().BeEquivalentTo(preview.ItemName);
            created.Invocation!.Changes.Should().BeEquivalentTo(preview.Invocation!.Changes);
            string expected = ExpectedOperativeNodeSource(template, name, created.ArgumentResults.Single().ValuesByCanonicalName);
            File.ReadAllText(Path.Combine(target, name + extension)).ReplaceLineEndings("\n").Should().Be(expected.ReplaceLineEndings("\n"));
        }
        File.ReadAllText(user).Should().Be("// preserve existing user code\n");
        File.ReadAllText(Path.Combine(project, "host.json")).Should().Be(host);
        File.ReadAllText(Path.Combine(project, "package.json")).Should().Be(package);
        File.ReadAllText(Path.Combine(project, ".func", "config.json")).Should().Be(config);
        File.Exists(Path.Combine(target, "functions.js")).Should().BeFalse();
        File.Exists(Path.Combine(target, "functions.ts")).Should().BeFalse();
        Directory.GetFiles(project, "function.json", SearchOption.AllDirectories).Should().BeEmpty();
        bridge.Legacy.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("js", "JavaScript")]
    [InlineData("ts", "TypeScript")]
    public async Task Bridge_ListAndExplicitHiddenIds_PreserveActualLegacyVisibility(string alias, string language)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        using var bridge = await CreateNodeBridgeAsync();
        string project = Path.Combine(_root, "project");
        await CreateNodeBridgeProjectAsync(bridge, project, alias);
        string target = Path.Combine(project, "src", "functions");
        var raw = ReadNodeSource().Templates.Where(template => template.Language!.Equals(language, StringComparison.OrdinalIgnoreCase)).ToArray();

        (await InvokeNodeBridgeAsync(bridge.Adapter.ParseNewInvocation(bridge.NewCommand(),
            [target, "--list", "--non-interactive", "--output", "json"]))).Should().Be(0, _interaction.AllOutput);

        bridge.Runner.Last!.Templates.Select(template => template.Identity).Should()
            .BeEquivalentTo(raw.Where(template => !V2HiddenTemplates.IsHidden(template.Id!)).Select(template => NativeNodeIdentity(template.Id!)));
        foreach (var hidden in raw.Where(template => V2HiddenTemplates.IsHidden(template.Id!)))
        {
            (await InvokeNodeBridgeAsync(bridge.Adapter.ParseNewInvocation(bridge.NewCommand(),
                [target, "-t", hidden.Id!, "--output", "json", "--non-interactive"], dryRun: true))).Should().Be(0, _interaction.AllOutput);
            bridge.Runner.Last!.SelectedTemplate!.IsHidden.Should().BeTrue();
            bridge.Runner.Last.ItemName!.Origin.Should().Be("authoredDefault");
        }
        Directory.GetFiles(target).Should().BeEmpty();
    }

    [Theory]
    [InlineData("Original-Invalid")]
    [InlineData("_LeadingUnderscore")]
    [InlineData("X")]
    [InlineData("Trailing_")]
    public async Task Bridge_OriginalNameRegex_RejectsUnsanitizedInputBeforePreviewOrMutation(string name)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        using var bridge = await CreateNodeBridgeAsync();
        string project = Path.Combine(_root, "project");
        await CreateNodeBridgeProjectAsync(bridge, project, "ts");
        string target = Path.Combine(project, "src", "functions");

        (await InvokeNodeBridgeAsync(bridge.Adapter.ParseNewInvocation(bridge.NewCommand(),
            [target, "-t", "TimerTrigger-TypeScript", "--name", name, "--non-interactive", "--output", "json"])))
            .Should().Be(1, _interaction.AllOutput);

        var result = bridge.Runner.Last!;
        result.Kind.Should().Be("invalidLegacyInput");
        result.ItemName!.Name.Should().Be(name);
        result.ItemName.Origin.Should().Be("explicit");
        result.Diagnostics.Should().ContainSingle().Which.Should().Contain(name).And.Contain(NodeItemCompatibility.ManifestPath);
        result.Invocation.Should().BeNull();
        Directory.GetFiles(target).Should().BeEmpty();
    }

    [Theory]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public async Task Bridge_OriginalNameLengthBoundary_UsesOriginalRegexBeforeNativePreview(int length, bool expected)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        using var bridge = await CreateNodeBridgeAsync();
        string project = Path.Combine(_root, "project");
        await CreateNodeBridgeProjectAsync(bridge, project, "js");
        string target = Path.Combine(project, "src", "functions");
        string name = new('A', length);

        int exit = await InvokeNodeBridgeAsync(bridge.Adapter.ParseNewInvocation(bridge.NewCommand(),
            [target, "-t", "TimerTrigger-JavaScript", "--name", name, "--non-interactive", "--output", "json"], dryRun: true));

        exit.Should().Be(expected ? 0 : 1, _interaction.AllOutput);
        bridge.Runner.Last!.ItemName!.Name.Should().Be(name);
        bridge.Runner.Last.ItemName.Succeeded.Should().Be(expected);
        Directory.GetFiles(target).Should().BeEmpty();
    }

    [Theory]
    [InlineData("stable")]
    [InlineData("preview")]
    [InlineData("experimental")]
    public async Task Bridge_BindingEligibility_ReadsOnlyActualSelectedBundleAndRequiresAllDeclaredBindings(string channel)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        using var bridge = await CreateNodeBridgeAsync(channel, """{"extensions":[{"bindings":["BlobTrigger"]}]}""");
        string project = Path.Combine(_root, "project");
        await CreateNodeBridgeProjectAsync(bridge, project, "js", channel);
        string target = Path.Combine(project, "src", "functions");
        string metadata = Path.Combine(bridge.BundlePath, "bin", "extensions.json");

        (await InvokeNodeBridgeAsync(bridge.Adapter.ParseNewInvocation(bridge.NewCommand(),
            [target, "-t", "EventGridBlobTrigger-JavaScript", "--non-interactive", "--output", "json"], dryRun: true)))
            .Should().Be(1, _interaction.AllOutput);
        bridge.Runner.Last!.Kind.Should().Be("Unavailable");
        bridge.Runner.Last.Context!.Bundle!.Path.Should().Be(bridge.BundlePath);

        File.WriteAllText(metadata, """{"extensions":[{"bindings":["BLOBTRIGGER","eventgridtrigger"]}]}""");
        (await InvokeNodeBridgeAsync(bridge.Adapter.ParseNewInvocation(bridge.NewCommand(),
            [target, "-t", "EventGridBlobTrigger-JavaScript", "--non-interactive", "--output", "json"], dryRun: true)))
            .Should().Be(0, _interaction.AllOutput);
        bridge.Runner.Last!.ItemName!.Name.Should().Be("eventGridBlobTrigger");
        Directory.GetFiles(target).Should().BeEmpty();

        File.WriteAllText(metadata, """{"extensions":[{"bindings":[]}]}""");
        (await InvokeNodeBridgeAsync(bridge.Adapter.ParseNewInvocation(bridge.NewCommand(),
            [target, "-t", "EventGridBlobTrigger-JavaScript", "--non-interactive", "--output", "json"], dryRun: true)))
            .Should().Be(1, _interaction.AllOutput);
        bridge.Runner.Last!.Kind.Should().Be("Unavailable");

        File.WriteAllText(metadata, "{malformed");
        (await InvokeNodeBridgeAsync(bridge.Adapter.ParseNewInvocation(bridge.NewCommand(),
            [target, "-t", "QueueTrigger-JavaScript", "--non-interactive", "--output", "json"], dryRun: true)))
            .Should().Be(1, _interaction.AllOutput);
        bridge.Runner.Last!.Templates.Single().ConstraintEvaluation.Diagnostics.Should()
            .Contain(diagnostic => diagnostic.Type == "func-bundle-bindings" && diagnostic.Status == TemplateConstraintEvaluationStatus.Failed);

        File.WriteAllText(Path.Combine(project, "host.json"), """{"version":"2.0"}""");
        (await InvokeNodeBridgeAsync(bridge.Adapter.ParseNewInvocation(bridge.NewCommand(),
            [target, "-t", "QueueTrigger-JavaScript", "--non-interactive", "--output", "json"], dryRun: true)))
            .Should().Be(1, _interaction.AllOutput);
        string[] builtIns = ["HttpTrigger-JavaScript", "TimerTrigger-JavaScript"];
        foreach (string builtIn in builtIns)
        {
            (await InvokeNodeBridgeAsync(bridge.Adapter.ParseNewInvocation(bridge.NewCommand(),
                [target, "-t", builtIn, "--non-interactive", "--output", "json"], dryRun: true))).Should().Be(0, _interaction.AllOutput);
            bridge.Runner.Last!.Context!.Bundle.Should().BeNull();
        }
    }

    [Theory]
    [InlineData("rawPrompts")]
    [InlineData("nativeContent")]
    [InlineData("nativeHost")]
    [InlineData("malformedNativeHost")]
    [InlineData("nativeConfiguration")]
    [InlineData("configurationHash")]
    [InlineData("packageVersion")]
    [InlineData("legacyIdentity")]
    [InlineData("rehashedOriginalDefault")]
    [InlineData("missingSource")]
    [InlineData("traversal")]
    public async Task Bridge_ActualMountedSourceMismatch_RefusesCompatibilityRatherThanTrustingClaimedHash(string corruption)
    {
        string archive = Path.Combine(_root, "tampered-node.nupkg");
        using (var outer = new PackageArchiveReader(BuiltArchive))
        using (var output = File.Create(archive))
        {
            outer.GetStream("tools/any/templates/node-basic.nupkg").CopyTo(output);
        }
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Update))
        {
            string path = corruption switch
            {
                "rawPrompts" => "provenance/node/v2/bindings/userPrompts.json",
                "nativeContent" => "content/items/TimerTrigger-JavaScript/funcNodeItem.js",
                "nativeHost" or "malformedNativeHost" => "content/items/TimerTrigger-JavaScript/.template.config/func.host.json",
                "nativeConfiguration" => "content/items/TimerTrigger-JavaScript/.template.config/template.json",
                "missingSource" => "provenance/node/v2/resources/Resources.json",
                _ => NodeItemCompatibility.ManifestPath,
            };
            var entry = zip.GetEntry(path)!;
            string text;
            using (var reader = new StreamReader(entry.Open())) text = reader.ReadToEnd();
            entry.Delete();
            if (corruption != "missingSource")
            {
                if (path == NodeItemCompatibility.ManifestPath)
                {
                    var root = JsonNode.Parse(text)!;
                    var mapping = root["templates"]!.AsArray().Single(item => item!["identity"]!.GetValue<string>() == "TimerTrigger-JavaScript")!;
                    if (corruption == "packageVersion") root["packageVersion"] = "99.0.0";
                    else if (corruption == "legacyIdentity") mapping["legacyId"] = "QueueTrigger-JavaScript";
                    else if (corruption == "traversal") mapping["content"]!["path"] = "../outside.js";
                    else if (corruption == "rehashedOriginalDefault")
                    {
                        string sourcePath = "provenance/node/v2/templates/templates.json";
                        var sourceEntry = zip.GetEntry(sourcePath)!;
                        JsonArray source;
                        using (var reader = new StreamReader(sourceEntry.Open())) source = JsonNode.Parse(reader.ReadToEnd())!.AsArray();
                        var timer = source.Single(item => item!["id"]!.GetValue<string>() == "TimerTrigger-JavaScript")!;
                        timer["jobs"]![0]!["inputs"]![0]!["defaultValue"] = "DifferentOriginalDefault";
                        sourceEntry.Delete();
                        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(source.ToJsonString());
                        using (var output = zip.CreateEntry(sourcePath).Open()) output.Write(bytes);
                        root["sources"]!.AsArray().Single(item => item!["path"]!.GetValue<string>() == sourcePath)!["sha256"]
                            = Convert.ToHexString(SHA256.HashData(bytes));
                    }
                    else mapping["configuration"]!["sha256"] = new string('0', 64);
                    text = root.ToJsonString();
                }
                else if (corruption == "nativeConfiguration")
                {
                    var config = JsonNode.Parse(text)!;
                    config["defaultName"] = "ChangedNativeDefault";
                    text = config.ToJsonString();
                }
                else if (corruption == "nativeHost")
                {
                    text += "\n ";
                }
                else
                {
                    text += "\nchanged";
                }
                using var writer = new StreamWriter(zip.CreateEntry(path).Open());
                writer.Write(text);
            }
        }
        string hive = Path.Combine(_root, "tampered-hive");
        await TemplateFixtures.InstallAsync(archive, hive, CancellationToken.None);
        var availability = new TemplateConstraintEvaluationSnapshot([new("node-stack", NuGetVersion.Parse("1.0.0"), ["node"], true)]);
        using var session = new TemplateEngineSession(new(WorkingDirectory.FromExplicit(_root)), hive, availability);
        using var templater = new Templater(session);
        var resolution = await templater.ResolveGroupAsync("TimerTrigger-JavaScript", TemplateType.Item, CancellationToken.None);
        if (corruption == "malformedNativeHost")
        {
            resolution.Status.Should().Be(TemplateResolutionStatus.Unavailable);
            resolution.Group.Should().BeEmpty();
            resolution.Diagnostics.Should().ContainSingle().Subject.MetadataDiagnostic.Should()
                .Contain("func.host.json").And.Contain("not valid JSON");
            Directory.Exists(Path.Combine(_root, "src")).Should().BeFalse();
            return;
        }

        resolution.Status.Should().Be(TemplateResolutionStatus.Success);
        var selected = resolution.Group!.Single();
        var arguments = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Entry.Parameters, new HashSet<string>(), []));

        var result = await new NodeItemCompatibility(_interaction).ResolveAsync(selected, arguments, null, false, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Origin.Should().Be("unresolved");
        result.Diagnostics.Should().ContainSingle().Which.Should().Contain("owned compatibility source");
        Directory.Exists(Path.Combine(_root, "src")).Should().BeFalse();
    }

    [Fact]
    public async Task Bridge_CanonicalOptions_ApplyNativePropertiesWhileRetainingRawLegacyNoOpEvidenceAndConflictSafety()
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        using var bridge = await CreateNodeBridgeAsync();
        string project = Path.Combine(_root, "project");
        await CreateNodeBridgeProjectAsync(bridge, project, "js");
        string target = Path.Combine(project, "src", "functions");
        string[] tokens = [target, "-t", "QueueTrigger-JavaScript", "--name", "OriginalQueue",
            "--queue-trigger-connection", "CustomStorageSetting", "--queue-trigger-name", "custom-queue-name",
            "--non-interactive", "--output", "json"];

        (await InvokeNodeBridgeAsync(bridge.Adapter.ParseNewInvocation(bridge.NewCommand(), tokens))).Should().Be(0, _interaction.AllOutput);

        var result = bridge.Runner.Last!;
        result.ArgumentResults.Single().ValuesByCanonicalName.Should().Contain("queueTrigger-connection", "CustomStorageSetting")
            .And.Contain("queueTrigger-name", "custom-queue-name");
        string output = Path.Combine(target, "OriginalQueue.js");
        var raw = ReadNodeSource().Templates.Single(template => template.Id == "QueueTrigger-JavaScript");
        var legacy = new V2TemplateEngine();
        string legacyTarget = Directory.CreateDirectory(Path.Combine(_root, "physical-legacy")).FullName;
        var legacyValues = result.ArgumentResults.Single().ValuesByCanonicalName.ToDictionary(pair => pair.Key, pair => pair.Value);
        legacyValues["trigger-functionName"] = "OriginalQueue";
        var legacyResult = legacy.Apply(raw, "OriginalQueue", legacyValues, new DirectoryInfo(legacyTarget), force: false);
        legacyResult.Should().BeOfType<TemplateApplicationResult.Created>();
        File.ReadAllText(Path.Combine(legacyTarget, "src", "functions", "OriginalQueue.js")).Should()
            .Contain("connection: ''", "queueName: 'js-queue-items'").And.NotContain("CustomStorageSetting", "custom-queue-name");
        string expected = ExpectedOperativeNodeSource(raw, "OriginalQueue", result.ArgumentResults.Single().ValuesByCanonicalName);
        File.ReadAllText(output).ReplaceLineEndings("\n").Should().Be(expected.ReplaceLineEndings("\n"));
        File.ReadAllText(output).Should().Contain("connection: 'CustomStorageSetting'", "queueName: 'custom-queue-name'");
        File.WriteAllText(output, "// user replacement must survive conflict");

        (await InvokeNodeBridgeAsync(bridge.Adapter.ParseNewInvocation(bridge.NewCommand(), tokens))).Should().Be(1, _interaction.AllOutput);
        bridge.Runner.Last!.Invocation!.Status.Should().Be(TemplateInvocationStatus.FileConflict);
        File.ReadAllText(output).Should().Be("// user replacement must survive conflict");
    }

    [Fact]
    public async Task Bridge_SourceCompatibility_ValidatesProgrammerInputsAndCancellationWithoutReadingOrWriting()
    {
        var compatibility = new NodeItemCompatibility(_interaction);
        Action construct = () => new NodeItemCompatibility(null!);
        construct.Should().Throw<ArgumentNullException>();
        Func<Task> missingSelected = () => compatibility.ResolveAsync(null!, null!, null, false, CancellationToken.None);
        await missingSelected.Should().ThrowAsync<ArgumentNullException>().WithParameterName("selected");

        await _installer.InstallFromPackageAsync(BuiltArchive);
        var availability = new TemplateConstraintEvaluationSnapshot([new("node-stack", NuGetVersion.Parse("1.0"), ["node"], true)]);
        using var session = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None, availability);
        using var templater = new Templater(session);
        var selected = (await templater.ResolveGroupAsync("TimerTrigger-JavaScript", TemplateType.Item, CancellationToken.None)).Group!.Single();
        var arguments = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(), []));
        Func<Task> missingArguments = () => compatibility.ResolveAsync(selected, null!, null, false, CancellationToken.None);
        await missingArguments.Should().ThrowAsync<ArgumentNullException>().WithParameterName("arguments");
        Func<Task> wrongItem = () => compatibility.ResolveAsync(selected, arguments with { TemplateIdentity = "other" }, null, false, CancellationToken.None);
        await wrongItem.Should().ThrowAsync<ArgumentException>();
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        Func<Task> cancel = () => compatibility.ResolveAsync(selected, arguments, null, false, canceled.Token);
        await cancel.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(Path.Combine(_root, "src")).Should().BeFalse();
    }

    private async Task<NodeBridgeHarness> CreateNodeBridgeAsync(string channel = "stable", string metadata = NodeBridgeExtensions,
        INativeProjectPreparation? preparation = null)
    {
        var services = await CreateServicesAsync();
        var scope = services.CreateScope();
        string bundleRoot = Path.Combine(_root, "selected-" + channel);
        string bundle = Path.Combine(bundleRoot, "tools", "any");
        Directory.CreateDirectory(Path.Combine(bundle, "bin"));
        File.WriteAllText(Path.Combine(bundle, "bin", "extensions.json"), metadata);
        var installed = Substitute.For<IInstalledBundleWorkloads>();
        string version = "4.35.0" + (channel == "stable" ? string.Empty : "-" + channel);
        installed.ListInstalledAsync(Arg.Any<CancellationToken>()).Returns([new InstalledBundleWorkload(version, bundleRoot)]);
        var bundles = new ExtensionBundleResolver(new InstalledBundleScanner(installed),
            NullBundleResolveTelemetry.Instance, NullLogger<ExtensionBundleResolver>.Instance);
        var contexts = new NewExecutionContextResolver(_files, services.GetRequiredService<IFunctionsProjectResolver>(),
            services.GetRequiredService<IProfileResolver>(), services.GetRequiredService<IOptionsMonitor<StackOptions>>(),
            services.GetServices<IProjectInitializer>(), services.GetRequiredService<IHostJsonBundleSectionReader>(), bundles,
            new TemplateEngineBundleContextReader(_files), enableBundleBindingConstraints: true);
        var arguments = new TemplateArgumentParser();
        var runner = new NodeBridgeRunner(new NewExecutionRunner(contexts, new TemplaterFactory(_templates), arguments, _interaction,
            new TemplateExecutionInteraction(arguments, _interaction)));
        var availability = new TemplateConstraintEvaluationSnapshot([new("node-stack", NuGetVersion.Parse("1.0.0"), ["node"], true)]);
        var init = new NodeInitRunner(scope.ServiceProvider.GetRequiredService<IInitExecutionRunner>());
        var adapter = new NativeTemplateCommandAdapter(init, runner,
            services.GetRequiredService<InitExecutionRenderer>(), services.GetRequiredService<NewExecutionRenderer>(), availability,
            new NodeItemCompatibility(_interaction), projectPreparation: preparation);
        return new(services, scope, adapter, runner, init, installed, bundle, _interaction);
    }

    private async Task CreateNodeBridgeProjectAsync(NodeBridgeHarness bridge, string project, string language, string channel = "stable")
    {
        string[] tokens = [project, "--stack", "node", "--language", language, "--bundles-channel", char.ToUpperInvariant(channel[0]) + channel[1..]];
        (await InvokeNodeBridgeAsync(bridge.Adapter.ParseInitInvocation(bridge.InitCommand(), tokens,
            new("basic", NonInteractive: true, JsonOutput: true)))).Should().Be(0, _interaction.AllOutput);
    }

    private static Task<int> InvokeNodeBridgeAsync(System.CommandLine.ParseResult parsed) =>
        parsed.InvokeAsync(new() { EnableDefaultExceptionHandler = false });

    private sealed class NodeBridgeRunner(INewExecutionRunner inner) : INewExecutionRunner
    {
        public NewExecutionResult? Last { get; private set; }

        public async Task<NewExecutionResult> ExecuteAsync(NewExecutionRequest request, TemplateConstraintEvaluationSnapshot availability,
            CancellationToken cancellationToken)
        {
            Last = await inner.ExecuteAsync(request, availability, cancellationToken);
            return Last;
        }
    }

    private sealed class NodeInitRunner(IInitExecutionRunner inner) : IInitExecutionRunner
    {
        public InitExecutionResult? Last { get; private set; }

        public async Task<InitExecutionResult> ExecuteAsync(InitExecutionRequest request, TemplateConstraintEvaluationSnapshot availability,
            CancellationToken cancellationToken)
        {
            Last = await inner.ExecuteAsync(request, availability, cancellationToken);
            return Last;
        }
    }

    private sealed class NodeBridgeHarness(ServiceProvider services, IServiceScope scope, NativeTemplateCommandAdapter adapter,
        NodeBridgeRunner runner, NodeInitRunner init, IInstalledBundleWorkloads installed, string bundlePath,
        TestInteractionService interaction) : IDisposable
    {
        public NativeTemplateCommandAdapter Adapter { get; } = adapter;
        public NodeBridgeRunner Runner { get; } = runner;
        public NodeInitRunner Init { get; } = init;
        public string BundlePath { get; } = bundlePath;
        public INewCommandRunner Legacy { get; } = Substitute.For<INewCommandRunner>();

        public NewCommand NewCommand() => new(Legacy, Substitute.For<INewCommandTemplateOptionProvider>());

        public InitCommand InitCommand() => new(interaction, new RecordingWorkloadHintRenderer(),
            services.GetRequiredService<ILocalSettingsProvider>(), services.GetRequiredService<IFunctionsProjectResolver>(),
            services.GetRequiredService<IHostJsonBundleSectionReader>(), new InstalledBundleScanner(installed),
            services.GetRequiredService<IFileSystem>(), services.GetServices<IProjectInitializer>());

        public void Dispose()
        {
            scope.Dispose();
            services.Dispose();
        }
    }
}
