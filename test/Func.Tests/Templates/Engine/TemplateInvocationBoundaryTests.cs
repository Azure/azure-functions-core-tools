// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;
using Microsoft.TemplateEngine.Orchestrator.RunnableProjects.Abstractions;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class TemplateInvocationBoundaryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "func-template-boundaries-" + Guid.NewGuid().ToString("N"));

    private string Hive => Path.Combine(_root, "hive");

    [SymbolicLinkFact]
    public async Task NativeEngine_ReevaluatedGeneratedPath_WritesOutsidePreviewAndReportsUnwrittenPath()
    {
        await InstallOutputTemplateAsync(
            """{"Path":{"type":"generated","generator":"sequence","fileRename":"TOKEN"}}""",
            file: Path.Combine("TOKEN", "Created.txt"));
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "output"));
        var outside = Path.Combine(_root, "owned-link-target");
        Directory.CreateDirectory(output.Info.FullName);
        Directory.CreateDirectory(outside);
        Directory.CreateSymbolicLink(Path.Combine(output.Info.FullName, "1"), outside);
        using TemplateEngineSession session = new(new(WorkingDirectory.FromExplicit(_root)), Hive);
        var sequence = RegisterSequenceMacro(session);
        var template = (await session.PackageManager.GetTemplatesAsync(CancellationToken.None)).Single();
        TemplateCreator creator = new(session.Settings);

        var preview = await creator.InstantiateAsync(template, "Created", "Created", output.Info.FullName,
            new Dictionary<string, string?>(), dryRun: true, cancellationToken: CancellationToken.None);
        var created = await creator.InstantiateAsync(template, "Created", "Created", output.Info.FullName,
            new Dictionary<string, string?>(), cancellationToken: CancellationToken.None);

        preview.Status.Should().Be(CreationResultStatus.Success);
        created.Status.Should().Be(CreationResultStatus.Success);
        sequence.Evaluations.Should().Be(3);
        var previewPath = preview.CreationEffects!.FileChanges.Should().ContainSingle().Subject.TargetRelativePath;
        Path.GetFullPath(Path.Combine(output.Info.FullName, previewPath)).Should().Be(Path.Combine(output.Info.FullName, "0", "Created.txt"));
        created.CreationEffects!.FileChanges.Should().ContainSingle().Which.TargetRelativePath.Should().Be(previewPath);
        File.Exists(Path.Combine(output.Info.FullName, previewPath)).Should().BeFalse();
        File.ReadAllText(Path.Combine(outside, "Created.txt")).Should().Be("CONTENT");
    }

    [Fact]
    public async Task InvokeAsync_ReevaluatedGeneratedPath_IsRejectedBeforeNativeEvaluation()
    {
        await InstallOutputTemplateAsync(
            """{"Path":{"type":"generated","generator":"sequence","fileRename":"TOKEN"}}""",
            file: Path.Combine("TOKEN", "Created.txt"));
        using TemplateEngineSession session = new(new(WorkingDirectory.FromExplicit(_root)), Hive);
        var sequence = RegisterSequenceMacro(session);
        using Templater templater = new(session);
        var selected = (await templater.ResolveGroupAsync("Func.Tests.Output", TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "output"));

        var created = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Created"), CancellationToken.None);

        created.Status.Should().Be(TemplateInvocationStatus.InvalidTemplate);
        sequence.Evaluations.Should().Be(0);
        created.Changes.Should().BeEmpty();
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [SymbolicLinkFact]
    public async Task NativeEngine_TransitiveValueFormCollision_WritesOutsideReportedExistingPath()
    {
        var (symbols, _) = AliasChainSymbols(2, deepestFirst: true, rename: true);
        await InstallOutputTemplateAsync(symbols, file: Path.Combine("TOKEN", "Created.txt"));
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "output"));
        var outside = Path.Combine(_root, "owned-link-target");
        Directory.CreateDirectory(Path.Combine(output.Info.FullName, "0"));
        Directory.CreateDirectory(outside);
        var reported = Path.Combine(output.Info.FullName, "0", "Created.txt");
        File.WriteAllText(reported, "original");
        Directory.CreateSymbolicLink(Path.Combine(output.Info.FullName, "1"), outside);
        using TemplateEngineSession session = new(new(WorkingDirectory.FromExplicit(_root)), Hive);
        var sequence = RegisterSequenceMacro(session);
        var template = (await session.PackageManager.GetTemplatesAsync(CancellationToken.None)).Single();
        TemplateCreator creator = new(session.Settings);

        var preview = await creator.InstantiateAsync(template, "Created", "Created", output.Info.FullName,
            new Dictionary<string, string?>(), forceCreation: true, dryRun: true, cancellationToken: CancellationToken.None);
        var created = await creator.InstantiateAsync(template, "Created", "Created", output.Info.FullName,
            new Dictionary<string, string?>(), forceCreation: true, cancellationToken: CancellationToken.None);

        preview.Status.Should().Be(CreationResultStatus.Success);
        created.Status.Should().Be(CreationResultStatus.Success);
        sequence.Evaluations.Should().Be(3);
        var previewPath = preview.CreationEffects!.FileChanges.Should().ContainSingle().Subject.TargetRelativePath;
        Path.GetFullPath(Path.Combine(output.Info.FullName, previewPath)).Should().Be(reported);
        created.CreationEffects!.FileChanges.Should().ContainSingle().Which.TargetRelativePath.Should().Be(previewPath);
        File.ReadAllText(reported).Should().Be("original");
        File.ReadAllText(Path.Combine(outside, "Created.txt")).Should().Be("CONTENT");
    }

    [Theory]
    [InlineData(2, true, true)]
    [InlineData(2, false, true)]
    [InlineData(2, true, false)]
    [InlineData(2, false, false)]
    [InlineData(4, true, true)]
    [InlineData(4, true, false)]
    [InlineData(8, true, true)]
    [InlineData(8, true, false)]
    public async Task InvokeAsync_TransitiveValueFormCollision_IsRejectedRegardlessOfDeclarationOrder(int depth, bool deepestFirst, bool rename)
    {
        var (symbols, pathParameter) = AliasChainSymbols(depth, deepestFirst, rename);
        var sources = rename ? null : JsonSerializer.Serialize(new[] { new { condition = pathParameter + " == 0" } });
        await InstallOutputTemplateAsync(symbols, sources, Path.Combine("TOKEN", "Created.txt"));
        using TemplateEngineSession session = new(new(WorkingDirectory.FromExplicit(_root)), Hive);
        var sequence = RegisterSequenceMacro(session);
        using Templater templater = new(session);
        var selected = (await templater.ResolveGroupAsync("Func.Tests.Output", TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "output"));

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Created", Force: true), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.InvalidTemplate);
        result.Diagnostic.Should().Contain("output paths");
        sequence.Evaluations.Should().Be(0);
        result.Changes.Should().BeEmpty();
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Theory]
    [InlineData("""{ "type": "project" }""")]
    [InlineData("""{ "type": "solution" }""")]
    [InlineData("""{ "type": "" }""")]
    [InlineData("""{ "type": null }""")]
    [InlineData("""{ "type": 7 }""")]
    [InlineData("""{ "type": true }""")]
    [InlineData("{}")]
    public async Task ResolveGroupAsync_MountedTypeChangedBeforeSelection_DoesNotGrantCachedItemCapability(string tags)
    {
        var package = TemplateFixtures.WriteBasicPackage(_root);
        await TemplateFixtures.InstallAsync(package, Hive);
        var cached = await TemplateFixtures.GetInstalledTemplateAsync(Hive, TemplateFixtures.ItemTemplateIdentity);
        cached.TagsCollection["type"].Should().Be("item");
        var configuration = Path.Combine(package, "item", ".template.config", "template.json");
        File.WriteAllText(configuration, File.ReadAllText(configuration).Replace("""{ "type": "item" }""", tags, StringComparison.Ordinal));
        using Templater templater = new(new TemplateEngineSession(new(WorkingDirectory.FromExplicit(_root)), Hive));

        var entries = await templater.ListAsync(TemplateType.Item, CancellationToken.None);
        var exact = await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None);
        var alias = await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateShortName, TemplateType.Item, CancellationToken.None);
        var picked = await templater.CreateGroupAsync(entries, CancellationToken.None);

        entries.Should().ContainSingle().Which.ConstraintEvaluation.Status.Should().Be(TemplateConstraintEvaluationStatus.Failed);
        TemplateResolutionResult[] resolutions = [exact, alias, picked];
        foreach (var result in resolutions)
        {
            result.Status.Should().Be(TemplateResolutionStatus.Unavailable);
            result.Group.Should().BeEmpty();
            result.Diagnostics.Should().ContainSingle().Which.ConstraintEvaluation.Diagnostics.Should()
                .ContainSingle().Which.Message.Should().Contain("type");
        }
    }

    [Theory]
    [InlineData("item")]
    [InlineData("ItEm")]
    public async Task InvokeAsync_UnchangedMountedItemType_PreservesCreation(string type)
    {
        var package = TemplateFixtures.WriteBasicPackage(_root);
        await TemplateFixtures.InstallAsync(package, Hive);
        await TemplateFixtures.GetInstalledTemplateAsync(Hive, TemplateFixtures.ItemTemplateIdentity);
        var configuration = Path.Combine(package, "item", ".template.config", "template.json");
        File.WriteAllText(configuration, File.ReadAllText(configuration).Replace("\"item\"", $"\"{type}\"", StringComparison.Ordinal));
        using Templater templater = new(new TemplateEngineSession(new(WorkingDirectory.FromExplicit(_root)), Hive));
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "output"));

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Current"), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.Success);
        File.ReadAllText(Path.Combine(output.Info.FullName, "Current.txt")).Should().Be("Current content");
        AssertWrittenPaths(output, result);
    }

    [Fact]
    public async Task ResolveGroupAsync_MountedProjectBecomesItem_DoesNotReclassifyCachedDescriptor()
    {
        var package = TemplateFixtures.WriteBasicPackage(_root);
        await TemplateFixtures.InstallAsync(package, Hive);
        await TemplateFixtures.GetInstalledTemplateAsync(Hive, TemplateFixtures.ProjectTemplateIdentity);
        var configuration = Path.Combine(package, "project", ".template.config", "template.json");
        File.WriteAllText(configuration, File.ReadAllText(configuration).Replace("\"project\"", "\"item\"", StringComparison.Ordinal));
        using Templater templater = new(new TemplateEngineSession(new(WorkingDirectory.FromExplicit(_root)), Hive));

        var project = await templater.ResolveGroupAsync(TemplateFixtures.ProjectTemplateIdentity, TemplateType.Project, CancellationToken.None);
        var item = await templater.ResolveGroupAsync(TemplateFixtures.ProjectTemplateIdentity, TemplateType.Item, CancellationToken.None);

        project.Status.Should().Be(TemplateResolutionStatus.Unavailable);
        project.Group.Should().BeEmpty();
        item.Status.Should().Be(TemplateResolutionStatus.WrongType);
        item.Group.Should().BeNull();
    }

    [Theory]
    [InlineData("""{"Path":{"type":"generated","generator":"guid","fileRename":"TOKEN"}}""", null)]
    [InlineData("""{"Path":{"type":"generated","generator":"random","parameters":{"low":1,"high":2},"fileRename":"TOKEN"}}""", null)]
    [InlineData("""{"Entropy":{"type":"generated","generator":"guid"},"Path":{"type":"derived","valueSource":"Entropy","valueTransform":"identity","fileRename":"TOKEN"}}""", null)]
    [InlineData("""{"Path":{"type":"bind","binding":"host:WorkingDirectory","fileRename":"TOKEN"}}""", null)]
    [InlineData("""{"Entropy":{"type":"generated","generator":"random","parameters":{"low":1,"high":2}},"Choose":{"type":"computed","value":"Entropy == 1"}}""",
        """[{"modifiers":[{"condition":"Choose","rename":{"TOKEN":"chosen"}}]}]""")]
    [InlineData("""{"Entropy":{"type":"generated","generator":"random","parameters":{"low":1,"high":2}}}""",
        """[{"condition":"Entropy == 1"}]""")]
    [InlineData("""{"Entropy":{"type":"generated","generator":"random","parameters":{"low":1,"high":2}}}""",
        """[{"condition":"true || (Entropy == 1)"}]""")]
    [InlineData("""{"Entropy":{"type":"generated","generator":"guid"},"Entropy-D":{"type":"parameter","fileRename":"TOKEN"}}""", null)]
    [InlineData("""{"Entropy":{"type":"generated","generator":"guid","parameters":{"format":""}},"Entropy-D":{"type":"parameter","fileRename":"TOKEN"}}""", null)]
    [InlineData("""{"Entropy":{"type":"generated","generator":"guid","parameters":{"format":null}},"Entropy-D":{"type":"parameter","fileRename":"TOKEN"}}""", null)]
    [InlineData("""{"Path":{"type":"parameter","fileRename":"TOKEN"},"Path{-VALUE-FORMS-}identity":{"type":"generated","generator":"guid"}}""", null)]
    [InlineData("""{"Entropy":{"type":"generated","generator":"guid"}}""",
        """[{"modifiers":[{"condition":"Entropy-uc-D != \"\"","rename":{"TOKEN":"chosen"}}]}]""")]
    [InlineData("""{"name":{"type":"generated","generator":"guid"}}""", null)]
    public async Task InvokeAsync_NonParameterOutputTopology_FailsBeforeNativeWrites(string symbols, string? sources)
    {
        await InstallOutputTemplateAsync(symbols, sources);
        using Templater templater = new(new TemplateEngineSession(new(WorkingDirectory.FromExplicit(_root)), Hive));
        var selected = (await templater.ResolveGroupAsync("Func.Tests.Output", TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "output"));

        var preview = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Created", DryRun: true), CancellationToken.None);
        var created = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Created", Force: true), CancellationToken.None);

        TemplateInvocationResult[] results = [preview, created];
        foreach (var result in results)
        {
            result.Status.Should().Be(TemplateInvocationStatus.InvalidTemplate);
            result.Diagnostic.Should().Contain("output paths");
            result.Changes.Should().BeEmpty();
        }

        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [SymbolicLinkFact]
    public async Task InvokeAsync_GeneratedDirectoryWithOwnedLink_FailsWithoutWritingThroughLink()
    {
        await InstallOutputTemplateAsync(
            """{"Path":{"type":"generated","generator":"random","parameters":{"low":1,"high":2},"fileRename":"TOKEN"}}""",
            file: Path.Combine("TOKEN", "Created.txt"));
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "output"));
        var outside = Path.Combine(_root, "owned-link-target");
        Directory.CreateDirectory(output.Info.FullName);
        Directory.CreateDirectory(outside);
        Directory.CreateSymbolicLink(Path.Combine(output.Info.FullName, "1"), outside);
        using Templater templater = new(new TemplateEngineSession(new(WorkingDirectory.FromExplicit(_root)), Hive));
        var selected = (await templater.ResolveGroupAsync("Func.Tests.Output", TemplateType.Item, CancellationToken.None)).Group![0];

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Created", Force: true), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.InvalidTemplate);
        result.Changes.Should().BeEmpty();
        Directory.EnumerateFileSystemEntries(outside).Should().BeEmpty();
        Directory.EnumerateFiles(output.Info.FullName, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Theory]
    [InlineData("guid0")]
    [InlineData("guid0-D")]
    [InlineData("guid0-lc-d")]
    public async Task InvokeAsync_ImplicitGuidOverwritesPathParameter_FailsWithoutWrites(string parameter)
    {
        await InstallOutputTemplateAsync(
            $$$"""{"{{{parameter}}}":{"type":"parameter","fileRename":"TOKEN"}}""",
            guids: """["a29e03ad-a64c-48d1-9f46-6be4895361b8"]""");
        using Templater templater = new(new TemplateEngineSession(new(WorkingDirectory.FromExplicit(_root)), Hive));
        var selected = (await templater.ResolveGroupAsync("Func.Tests.Output", TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "output"));

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Created"), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.InvalidTemplate);
        result.Changes.Should().BeEmpty();
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvokeAsync_ParameterTopologyAndContentOnlyRandomness_ReportWrittenPaths(bool include)
    {
        await InstallOutputTemplateAsync(
            """
            {
              "Path":{"type":"parameter","datatype":"string","defaultValue":"Stable","fileRename":"TOKEN"},
              "Include":{"type":"parameter","datatype":"bool","defaultValue":"true"},
              "Entropy":{"type":"generated","generator":"guid","replaces":"CONTENT"}
            }
            """,
            """[{"condition":"\"Entropy\" != \"\"","modifiers":[{"condition":"!Include","exclude":["TOKEN.txt"]}]}]""");
        using Templater templater = new(new TemplateEngineSession(new(WorkingDirectory.FromExplicit(_root)), Hive));
        var selected = (await templater.ResolveGroupAsync("Func.Tests.Output", TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "output"));
        var parameters = new Dictionary<string, string?> { ["Path"] = "Chosen", ["Include"] = include.ToString() };
        var request = new TemplateInvocationRequest(output, parameters, "Created", DryRun: true);

        var preview = await selected.InvokeAsync(request, CancellationToken.None);
        var created = await selected.InvokeAsync(request with { DryRun = false }, CancellationToken.None);

        preview.Status.Should().Be(TemplateInvocationStatus.Success);
        created.Status.Should().Be(TemplateInvocationStatus.Success);
        created.Changes.Should().BeEquivalentTo(preview.Changes);
        AssertWrittenPaths(output, created);
        if (include)
        {
            var content = File.ReadAllText(Path.Combine(output.Info.FullName, "Chosen.txt"));
            content.Should().NotContain("CONTENT");
            Guid.TryParse(content, out _).Should().BeTrue();

            var repeated = await selected.InvokeAsync(request with { DryRun = false, Force = true }, CancellationToken.None);
            repeated.Status.Should().Be(TemplateInvocationStatus.Success);
            AssertWrittenPaths(output, repeated);
        }
    }

    [Theory]
    [InlineData("""{"type":"generated","generator":"random","parameters":{"low":1,"high":3},"replaces":"CONTENT"}""")]
    [InlineData("""{"type":"generated","generator":"guid","parameters":{"format":"N"},"replaces":"CONTENT"}""")]
    [InlineData("""{"type":"bind","binding":"host:WorkingDirectory","replaces":"CONTENT"}""")]
    public async Task InvokeAsync_StableSuffixParameterBesideContentProducer_PreservesParameterFilename(string producer)
    {
        await InstallOutputTemplateAsync($$"""
            {
              "Entropy-D":{"type":"parameter","datatype":"string","defaultValue":"Stable","fileRename":"TOKEN"},
              "Entropy":{{producer}}
            }
            """,
            sources: """[{"condition":"Entropy-D == \"Stable\""}]""");
        using Templater templater = new(new TemplateEngineSession(new(WorkingDirectory.FromExplicit(_root)), Hive));
        var selected = (await templater.ResolveGroupAsync("Func.Tests.Output", TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "output"));
        var request = new TemplateInvocationRequest(output, new Dictionary<string, string?>(), "Created", DryRun: true);

        var preview = await selected.InvokeAsync(request, CancellationToken.None);
        var created = await selected.InvokeAsync(request with { DryRun = false }, CancellationToken.None);

        preview.Status.Should().Be(TemplateInvocationStatus.Success);
        created.Status.Should().Be(TemplateInvocationStatus.Success);
        var change = created.Changes.Should().ContainSingle().Subject;
        Path.GetFullPath(Path.Combine(output.Info.FullName, change.Path)).Should().Be(Path.Combine(output.Info.FullName, "Stable.txt"));
        File.ReadAllText(Path.Combine(output.Info.FullName, "Stable.txt")).Should().NotBe("CONTENT");
        AssertWrittenPaths(output, created);
    }

    [Theory]
    [InlineData("""{"type":"parameter","datatype":"string"}""", false)]
    [InlineData("""{"type":"parameter","datatype":"string"}""", true)]
    [InlineData("""{"type":"parameter","datatype":"string","defaultValue":"Stable","isEnabled":false}""", false)]
    [InlineData("""{"type":"parameter","datatype":"string","defaultValue":"Stable","isEnabled":"false"}""", false)]
    public async Task InvokeAsync_UnsetOptionalParameter_DoesNotHideNonParameterSourceCondition(string parameter, bool suppliedNull)
    {
        var package = TemplateFixtures.WriteBasicPackage(_root);
        var configurationDirectory = Path.Combine(package, "item", ".template.config");
        var configuration = Path.Combine(configurationDirectory, "template.json");
        File.WriteAllText(configuration, File.ReadAllText(configuration).Replace(
            "\"sourceName\": \"ItemFunction\"",
            $$$"""
            "sourceName": "ItemFunction",
            "symbols": {
              "Entropy == 1": {{{parameter}}},
              "Entropy": {"type":"generated","generator":"random","parameters":{"low":1,"high":2}}
            },
            "sources": [{"condition":"Entropy == 1"}]
            """,
            StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(configurationDirectory, "func.host.json"),
            """{"symbolInfo":{"Entropy == 1":{"longName":"unused"}}}""");
        await TemplateFixtures.InstallAsync(package, Hive);
        using Templater templater = new(new TemplateEngineSession(new(WorkingDirectory.FromExplicit(_root)), Hive));
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "output"));

        var parameters = new Dictionary<string, string?>();
        if (suppliedNull)
        {
            parameters.Add("Entropy == 1", null);
        }

        var result = await selected.InvokeAsync(new(output, parameters, "Created"), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.InvalidTemplate);
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvokeAsync_PresentParameterWithNonParameterPrefix_PreservesStableCondition(bool useForm)
    {
        var symbols = """
            {
              "Entropy-D":{"type":"parameter","datatype":"string","fileRename":"TOKEN"},
              "Entropy":{"type":"generated","generator":"random","parameters":{"low":1,"high":3},"replaces":"CONTENT"}
            }
            """;
        string condition = (useForm ? "Entropy-D{-VALUE-FORMS-}identity" : "Entropy-D") + " == \"Chosen\"";
        await InstallOutputTemplateAsync(symbols, JsonSerializer.Serialize(new[] { new { condition } }));
        using Templater templater = new(new TemplateEngineSession(new(WorkingDirectory.FromExplicit(_root)), Hive));
        var selected = (await templater.ResolveGroupAsync("Func.Tests.Output", TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "output"));
        var values = new Dictionary<string, string?> { ["Entropy-D"] = "Chosen" };

        var result = await selected.InvokeAsync(new(output, values, "Created"), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.Success);
        File.ReadAllText(Path.Combine(output.Info.FullName, "Chosen.txt")).Should().NotBe("CONTENT");
        AssertWrittenPaths(output, result);
    }

    [Fact]
    public async Task InvokeAsync_ExplicitUnsetName_DoesNotHideNonParameterSourceCondition()
    {
        await InstallOutputTemplateAsync(
            """
            {
              "name":{"type":"parameter","datatype":"string","forms":{"global":["identity == 0"]}},
              "name{-VALUE-FORMS-}identity":{"type":"generated","generator":"sequence"}
            }
            """,
            sources: """[{"condition":"name{-VALUE-FORMS-}identity == 0"}]""",
            forms: """{"identity == 0":{"identifier":"identity"}}""");
        using TemplateEngineSession session = new(new(WorkingDirectory.FromExplicit(_root)), Hive);
        var sequence = RegisterSequenceMacro(session);
        using Templater templater = new(session);
        var selected = (await templater.ResolveGroupAsync("Func.Tests.Output", TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "output"));

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Created"), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.InvalidTemplate);
        sequence.Evaluations.Should().Be(0);
        result.Changes.Should().BeEmpty();
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Theory]
    [InlineData(null, false, "output")]
    [InlineData("", false, "output")]
    [InlineData(null, true, "Default")]
    [InlineData("", true, "Default")]
    [InlineData("Created", true, "Created")]
    public async Task InvokeAsync_NativeNameSelection_PreservesOrdinaryTemplates(string? name, bool preferDefaultName, string expected)
    {
        await InstallOutputTemplateAsync("{}", file: "ItemFunction.txt", preferDefaultName: preferDefaultName);
        using Templater templater = new(new TemplateEngineSession(new(WorkingDirectory.FromExplicit(_root)), Hive));
        var selected = (await templater.ResolveGroupAsync("Func.Tests.Output", TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "output"));

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), name), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.Success);
        File.ReadAllText(Path.Combine(output.Info.FullName, expected + ".txt")).Should().Be("CONTENT");
        AssertWrittenPaths(output, result);
    }

    [Fact]
    public void OutputPathValidator_NullConfiguration_Throws()
    {
        Action action = () => TemplateOutputPathValidator.GetDiagnostic(null!);

        action.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static void AssertWrittenPaths(WorkingDirectory output, TemplateInvocationResult result)
        => Directory.EnumerateFiles(output.Info.FullName, "*", SearchOption.AllDirectories)
            .Should().BeEquivalentTo(result.Changes.Select(change => Path.GetFullPath(Path.Combine(output.Info.FullName, change.Path))));

    private static SequenceMacro RegisterSequenceMacro(TemplateEngineSession session)
    {
        SequenceMacro macro = new();
        session.Settings.Components.AddComponent(typeof(IMacro), macro);
        session.Settings.Components.AddComponent(typeof(IGeneratedSymbolMacro), macro);
        return macro;
    }

    private static (string Symbols, string PathParameter) AliasChainSymbols(int depth, bool deepestFirst, bool rename)
    {
        Dictionary<string, object> symbols = [];
        string pathParameter = "A" + string.Concat(Enumerable.Repeat("{-VALUE-FORMS-}identity", depth));
        for (int index = depth; index > 0; index--)
        {
            string name = "A" + string.Concat(Enumerable.Repeat("{-VALUE-FORMS-}identity", index));
            Dictionary<string, object> parameter = new()
            {
                ["type"] = "parameter",
                ["datatype"] = "string",
                ["defaultValue"] = "stable",
            };
            if (rename && index == depth)
            {
                parameter.Add("fileRename", "TOKEN");
            }

            symbols.Add(name, parameter);
        }

        symbols.Add("A", new { type = "derived", valueSource = "Entropy", valueTransform = "identity" });
        symbols.Add("Entropy", new { type = "generated", generator = "sequence" });
        return (JsonSerializer.Serialize(deepestFirst ? symbols : symbols.Reverse().ToDictionary()), pathParameter);
    }

    private async Task InstallOutputTemplateAsync(string symbols, string? sources = null, string file = "TOKEN.txt", string? guids = null, string? forms = null, bool preferDefaultName = false)
    {
        var package = Path.Combine(_root, "package");
        var configuration = Path.Combine(package, ".template.config");
        Directory.CreateDirectory(configuration);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(package, file))!);
        File.WriteAllText(Path.Combine(package, file), "CONTENT");
        File.WriteAllText(Path.Combine(configuration, "template.json"), $$"""
            {
              "identity":"Func.Tests.Output",
              "name":"Output fixture",
              "shortName":"func-tests-output",
              "tags":{"type":"item"},
              "sourceName":"ItemFunction",
              "defaultName":"Default",
              "preferDefaultName":{{JsonSerializer.Serialize(preferDefaultName)}},
              "symbols":{{symbols}},
              "guids":{{guids ?? "[]"}},
              "forms":{{forms ?? "{}"}},
              "sources":{{sources ?? "[]"}}
            }
            """);
        await TemplateFixtures.InstallAsync(package, Hive);
    }

    private sealed class SequenceMacro : IGeneratedSymbolMacro
    {
        public Guid Id { get; } = new("fbf08b2b-7c4b-4a0f-8f80-af4f76aa6da4");

        public string Type => "sequence";

        public int Evaluations { get; private set; }

        public IMacroConfig CreateConfig(IEngineEnvironmentSettings environmentSettings, IGeneratedSymbolConfig generatedSymbolConfig)
            => new SequenceConfig(generatedSymbolConfig.VariableName);

        public void EvaluateConfig(IEngineEnvironmentSettings environmentSettings, IVariableCollection vars, IMacroConfig config)
        {
            // Keep both effect passes identical; only native creation receives a different path.
            vars[config.VariableName] = Evaluations++ < 2 ? "0" : "1";
        }
    }

    private sealed record SequenceConfig(string VariableName) : IMacroConfig
    {
        public string Type => "sequence";
    }
}
