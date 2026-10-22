// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.PhysicalFileSystem;
using Microsoft.TemplateEngine.Edge.Template;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class NativeSourceSnapshotBoundaryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "native-placement-boundary-" + Guid.NewGuid().ToString("N"));

    private string Package => Path.Combine(_root, "package");
    private string Base => Path.Combine(_root, "command");
    private string Source => Path.Combine(Package, "layout.json");

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Invoke_IntentSelectsNativeSourcesWithoutChangingOutputBase(bool legacy, bool nested)
    {
        await InstallAsync("regular");
        string output = nested ? Path.Combine(Base, "generated", "items") : Base;
        TemplateEngineContext context = new(WorkingDirectory.FromExplicit(output),
            itemOutputIntent: legacy ? NativeItemOutputIntent.LegacyWorkflowRelative : NativeItemOutputIntent.LiteralOutput);
        using Templater templater = TemplateFixtures.CreateTemplater(new(context, Path.Combine(_root, "hive")));
        var selected = (await templater.ResolveGroupAsync("Placed", TemplateType.Item)).Group![0];
        var request = Request(selected) with { OutputDirectory = context.CommandDirectory };

        var preview = await selected.InvokeAsync(request with { DryRun = true }, CancellationToken.None);
        var created = await selected.InvokeAsync(request, CancellationToken.None);

        string target = legacy ? Path.Combine(output, "generated", "items", "Named.js") : Path.Combine(output, "Named.js");
        preview.Status.Should().Be(TemplateInvocationStatus.Success);
        created.Status.Should().Be(TemplateInvocationStatus.Success);
        request.OutputDirectory.Info.FullName.Should().Be(output);
        created.Changes.Should().BeEquivalentTo(preview.Changes);
        Path.GetFullPath(Path.Combine(output, created.Changes.Should().ContainSingle().Subject.Path)).Should().Be(target);
        File.ReadAllText(target).Should().Be("static body");
    }

    [Fact]
    public async Task Invoke_SharedHiveAcrossIntentTransitions_DoesNotRetainPreviousNativeLayout()
    {
        await InstallAsync("regular");
        bool[] modes = [true, false, true];
        for (int index = 0; index < modes.Length; index++)
        {
            string output = Path.Combine(Base, index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            TemplateEngineContext context = new(WorkingDirectory.FromExplicit(output),
                itemOutputIntent: modes[index] ? NativeItemOutputIntent.LegacyWorkflowRelative : NativeItemOutputIntent.LiteralOutput);
            using Templater templater = TemplateFixtures.CreateTemplater(new(context, Path.Combine(_root, "hive")));
            var selected = (await templater.ResolveGroupAsync("Placed", TemplateType.Item)).Group![0];

            var result = await selected.InvokeAsync(Request(selected) with { OutputDirectory = context.CommandDirectory }, CancellationToken.None);

            result.Status.Should().Be(TemplateInvocationStatus.Success);
            string expected = modes[index] ? Path.Combine("generated", "items", "Named.js") : "Named.js";
            Path.GetFullPath(Path.Combine(output, result.Changes.Should().ContainSingle().Subject.Path)).Should().Be(Path.Combine(output, expected));
            Directory.GetFiles(output, "*", SearchOption.AllDirectories).Should().ContainSingle();
        }
    }

    [Fact]
    public async Task Invoke_LowercaseDigestPins_RetainsExactSourceAndOutputAssociation()
    {
        await InstallAsync("regular");
        using Templater templater = TemplateFixtures.CreateTemplater(new(Context(), Path.Combine(_root, "hive")));
        var selected = (await templater.ResolveGroupAsync("Placed", TemplateType.Item)).Group![0];
        var request = Request(selected);
        var snapshot = request.SourceSnapshot!;
        TemplateEffectSource[] pins = [.. snapshot.Sources.Select(source => source with { Sha256 = source.Sha256.ToLowerInvariant() })];
        NativeTemplateOutputSource[] outputs = [.. snapshot.Outputs.Select(output => output with
        {
            Source = pins.Single(pin => pin.Identity == output.Source.Identity),
        })];

        var result = await selected.InvokeAsync(request with
        {
            SourceSnapshot = new(snapshot.TemplateIdentity, snapshot.ConfigurationSha256.ToLowerInvariant(), pins, outputs),
        }, CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.Success, result.Diagnostic);
        File.ReadAllText(Path.Combine(Base, "generated", "items", "Named.js")).Should().Be("static body");
    }

    [Theory]
    [InlineData("placeholder")]
    [InlineData("extra")]
    [InlineData("wrong-name")]
    [InlineData("zero")]
    [InlineData("duplicate")]
    [InlineData("foreign-source")]
    public async Task Invoke_ExpectedRegularSourceContract_RefusesDirectoryExtraAndWrongSource(string invalid)
    {
        await InstallAsync(invalid);
        using Templater templater = TemplateFixtures.CreateTemplater(new(Context(), Path.Combine(_root, "hive")));
        var selected = (await templater.ResolveGroupAsync("Placed", TemplateType.Item)).Group![0];
        var request = Request(selected, invalid == "wrong-name" ? "other.js" : invalid == "placeholder" ? "seed.js/empty-marker" : "seed.js");

        var result = await selected.InvokeAsync(request with { DryRun = true }, CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.InvalidTemplate);
        if (invalid == "placeholder") result.Diagnostic.Should().Contain("placeholder directories");
        Directory.Exists(Base).Should().BeFalse();
    }

    [Theory]
    [InlineData("layout.json")]
    [InlineData("seed.js")]
    [InlineData(".template.config/template.json")]
    public async Task Invoke_PinnedSourceChangesAfterNativePreview_RefusesBeforeLiveCreate(string changed)
    {
        await InstallAsync("regular");
        using Templater templater = TemplateFixtures.CreateTemplater(new PlacementSession(Context(), Path.Combine(_root, "hive"),
            () => File.AppendAllText(Path.Combine(Package, changed.Replace('/', Path.DirectorySeparatorChar)),
                changed.EndsWith("template.json", StringComparison.Ordinal) ? "\n " : "changed")));
        var selected = (await templater.ResolveGroupAsync("Placed", TemplateType.Item)).Group![0];

        var result = await selected.InvokeAsync(Request(selected), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.TemplateChanged);
        Directory.Exists(Base).Should().BeFalse();
    }

    [Fact]
    public async Task Invoke_PinnedBodyChangesBeforePreview_RefusesWithoutEvaluatingNativeSources()
    {
        await InstallAsync("regular");
        int previews = 0;
        using Templater templater = TemplateFixtures.CreateTemplater(new PlacementSession(Context(), Path.Combine(_root, "hive"), () => previews++));
        var selected = (await templater.ResolveGroupAsync("Placed", TemplateType.Item)).Group![0];
        var request = Request(selected);
        File.AppendAllText(Path.Combine(Package, "seed.js"), "changed");

        var result = await selected.InvokeAsync(request, CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.TemplateChanged);
        previews.Should().Be(0);
        Directory.Exists(Base).Should().BeFalse();
    }

    [Theory]
    [InlineData("extra-file")]
    [InlineData("extra-directory")]
    [InlineData("unmapped")]
    public async Task Invoke_ActualImageDiffersFromReportedRegularSources_RefusesBeforeAnyInstall(string mismatch)
    {
        await InstallAsync("regular");
        using Templater templater = TemplateFixtures.CreateTemplater(new PlacementSession(Context(), Path.Combine(_root, "hive"), () => { },
            (fileSystem, output) =>
            {
                if (mismatch == "extra-file") fileSystem.WriteAllText(Path.Combine(output, "unreported.js"), "captured but not reported");
                if (mismatch == "extra-directory") fileSystem.CreateDirectory(Path.Combine(output, "unreported"));
            },
            original =>
            {
                if (mismatch != "unmapped") return original;
                var change = Substitute.For<IFileChange>();
                change.TargetRelativePath.Returns(original.CreationEffects!.FileChanges.Single().TargetRelativePath);
                change.ChangeKind.Returns(ChangeKind.Create);
                var effects = Substitute.For<ICreationEffects>();
                effects.FileChanges.Returns([change]);
                effects.CreationResult.Returns(original.CreationResult!);
                var result = Substitute.For<ITemplateCreationResult>();
                result.Status.Returns(CreationResultStatus.Success);
                result.CreationEffects.Returns(effects);
                result.CreationResult.Returns(original.CreationResult!);
                return result;
            }));
        var selected = (await templater.ResolveGroupAsync("Placed", TemplateType.Item)).Group![0];

        var result = await selected.InvokeAsync(Request(selected), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.InvalidTemplate);
        result.HasCommittedOutput.Should().BeFalse();
        Directory.Exists(Base).Should().BeFalse();
    }

    [Fact]
    public async Task Capture_PrivateExpansion_RevalidatesPinnedBodyAfterNativePreview()
    {
        await InstallAsync("regular");
        using Templater templater = TemplateFixtures.CreateTemplater(new PlacementSession(Context(), Path.Combine(_root, "hive"),
            () => File.AppendAllText(Path.Combine(Package, "seed.js"), "changed")));
        var selected = (await templater.ResolveGroupAsync("Placed", TemplateType.Item)).Group![0];

        var result = await selected.CaptureNativePreparationAsync(Request(selected), true, CancellationToken.None);

        result.Failure!.Status.Should().Be(TemplateInvocationStatus.TemplateChanged);
        Directory.Exists(Base).Should().BeFalse();
    }

    [SymbolicLinkFact]
    public async Task Invoke_DerivedPrefixBecomesLinkAfterPreview_RefusesBeforeOutsideWrite()
    {
        await InstallAsync("regular");
        string outside = Directory.CreateDirectory(Path.Combine(_root, "outside")).FullName;
        using Templater templater = TemplateFixtures.CreateTemplater(new PlacementSession(Context(), Path.Combine(_root, "hive"), () =>
        {
            Directory.CreateDirectory(Base);
            Directory.CreateSymbolicLink(Path.Combine(Base, "generated"), outside);
        }));
        var selected = (await templater.ResolveGroupAsync("Placed", TemplateType.Item)).Group![0];

        var result = await selected.InvokeAsync(Request(selected), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.Failed);
        result.Diagnostic.Should().Contain("symbolic link or reparse point");
        Directory.GetFileSystemEntries(outside).Should().BeEmpty();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private TemplateEngineContext Context() => new(WorkingDirectory.FromExplicit(Base), itemOutputIntent: NativeItemOutputIntent.LegacyWorkflowRelative);

    private TemplateInvocationRequest Request(ResolvedTemplate selected, string expected = "seed.js")
    {
        string body = File.Exists(Path.Combine(Package, "seed.js")) ? "seed.js" : "seed.js/empty-marker";
        TemplateEffectSource layout = Pin("layout.json");
        TemplateEffectSource content = Pin(body);
        return new(WorkingDirectory.FromExplicit(Base), new Dictionary<string, string?>(), "Named")
        {
            SourceSnapshot = new(selected.Entry.Identity, selected.ConfigurationSha256!, [layout, content], [new(expected, content)]),
        };

        TemplateEffectSource Pin(string path) => new(selected.Entry.Identity, path,
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(Package, path.Replace('/', Path.DirectorySeparatorChar))))));
    }

    private async Task InstallAsync(string kind)
    {
        Directory.CreateDirectory(Path.Combine(Package, ".template.config"));
        File.WriteAllText(Source, "owned raw layout");
        string placeholder = kind == "placeholder" ? ""","placeholderFilename":"empty-marker" """ : string.Empty;
        string legacyCondition = kind == "zero" ? "Mode == \\\"never\\\"" : "Mode == \\\"legacy\\\"";
        string extraSource = kind == "duplicate" ? """,{"target":"other","exclude":[".template.config/**/*","layout.json"]}""" : string.Empty;
        string sourceRoot = kind == "foreign-source" ? "\"source\":\"other\"," : string.Empty;
        File.WriteAllText(Path.Combine(Package, ".template.config", "template.json"), $$$"""
            {"identity":"Placed","name":"Placed","shortName":"placed","tags":{"type":"item"},"sourceName":"seed"{{{placeholder}}},
             "symbols":{"Mode":{"type":"bind","binding":"host:func:item-output-intent","defaultValue":"literal"}},
             "sources":[{ {{{sourceRoot}}}"condition":"{{{legacyCondition}}}","target":"generated/items","exclude":[".template.config/**/*","layout.json"]},
                        { {{{sourceRoot}}}"condition":"Mode != \"legacy\"","target":".","exclude":[".template.config/**/*","layout.json"]}{{{extraSource}}}]}
            """);
        if (kind == "placeholder")
        {
            Directory.CreateDirectory(Path.Combine(Package, "seed.js"));
            File.WriteAllText(Path.Combine(Package, "seed.js", "empty-marker"), string.Empty);
        }
        else
        {
            File.WriteAllText(Path.Combine(Package, "seed.js"), "static body");
        }
        if (kind == "extra") File.WriteAllText(Path.Combine(Package, "extra.txt"), "extra");
        if (kind == "foreign-source")
        {
            Directory.CreateDirectory(Path.Combine(Package, "other"));
            File.WriteAllText(Path.Combine(Package, "other", "seed.js"), "unqualified other body");
        }
        await TemplateFixtures.InstallAsync(Package, Path.Combine(_root, "hive"), CancellationToken.None);
    }

    private sealed class PlacementSession(TemplateEngineContext context, string hive, Action afterPreview,
        Action<IPhysicalFileSystem, string>? afterCapture = null, Func<ITemplateCreationResult, ITemplateCreationResult>? report = null) : TemplateEngineSession(context, hive)
    {
        protected override async Task<ITemplateCreationResult> InstantiateAsync(ITemplateInfo template, TemplateInvocationRequest request,
            InputDataSet parameters, bool dryRun, CancellationToken cancellationToken)
        {
            var result = await base.InstantiateAsync(template, request, parameters, dryRun, cancellationToken);
            if (!dryRun)
            {
                afterPreview();
                afterCapture?.Invoke(Settings.Host.FileSystem, request.OutputDirectory.Info.FullName);
            }
            return report?.Invoke(result) ?? result;
        }
    }
}
