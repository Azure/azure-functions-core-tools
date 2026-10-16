// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.DotNet;
using Azure.Functions.Cli.Templates.DotNet.Shared;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Tests.Templates.Engine;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.DotNet;
using Microsoft.TemplateEngine.Edge.Template;
using NSubstitute;
using Xunit.Abstractions;
using HostRunner = Azure.Functions.Cli.Templates.DotNet.Shared.IDotnetCliRunner;
using PhysicalRunner = Azure.Functions.Cli.Workloads.DotNet.IDotnetCliRunner;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class DotNetDirectProviderTests(ITestOutputHelper testOutput) : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("func-direct-dotnet-").FullName;
    private readonly CancellationTokenSource _deadline = new(TimeSpan.FromMinutes(5));

    public void Dispose()
    {
        _deadline.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProjectProvider_BothCandidatesInOneSession_PreservesNativeSourcesAndExplicitFrameworkProvenance(bool explicitFramework)
    {
        using var fixture = await OpenAsync();
        var candidates = await fixture.Templater.ResolveGroupAsync("func", TemplateType.Project, _deadline.Token);
        candidates.Status.Should().Be(TemplateResolutionStatus.Success, string.Join("; ", candidates.Diagnostics));
        candidates.Group.Should().HaveCount(2);
        foreach (var selected in candidates.Group!)
        {
            var language = selected.Entry.Language!;
            var configBytes = await selected.ReadPackageFileAsync(
                $"content/ProjectTemplate-{(language == "C#" ? "CSharp" : "FSharp")}/.template.config/template.json", _deadline.Token);
            var source = new NativeProjectTemplateSource(selected.Entry,
                new(Encoding.UTF8.GetString(configBytes.Span), selected.ConfigurationSha256!, TemplateType.Project, []),
                selected.ConfigurationSourceIdentity!, selected.ReadPackageFileAsync);
            var qualification = await fixture.Provider.QualifyLayoutAsync(source, _deadline.Token);
            qualification!.TemplateIdentity.Should().Be(selected.Entry.Identity);
            qualification.ConfigurationFingerprint.Should().Be(Hash(configBytes.Span));
            var raw = explicitFramework ? new[] { "--target-framework", "net10.0" } : [];
            var parsed = Parse(selected, raw);
            parsed.ExplicitParameterNames.Contains("Framework").Should().Be(explicitFramework);
            parsed.ValuesByCanonicalName["Framework"].Should().Be(explicitFramework ? "net10.0" : "net8.0");
            var output = WorkingDirectory.FromExplicit(Path.Combine(_root, language == "C#" ? "cs" : "fs"));
            var invocation = new TemplateInvocationRequest(output, parsed.ValuesByCanonicalName, "Native.App");
            var command = new InitExecutionRequest(output, "func", "Native.App", "dotnet", language,
                false, true, false, false, raw, new HashSet<string>());

            var prepared = await fixture.Provider.PrepareAsync(new NativeProjectWorkflowContext(fixture.Templater, selected, parsed, command, invocation), _deadline.Token);

            prepared.Diagnostics.Should().BeEmpty();
            var request = prepared.Request!;
            request.Parameters["Framework"].Should().Be("net10.0");
            parsed.ExplicitParameterNames.Contains("Framework").Should().Be(explicitFramework);
            (await selected.ReadPackageFileAsync(
                $"content/ProjectTemplate-{(language == "C#" ? "CSharp" : "FSharp")}/.template.config/template.json", _deadline.Token))
                .ToArray().Should().Equal(configBytes.ToArray());
            var capture = await selected.CaptureNativePreparationAsync(request, new PhysicalFileSystem(), true, _deadline.Token);
            capture.Failure.Should().BeNull();
            capture.Snapshot!.Source.Sha256.Should().Be(Hash(configBytes.Span));
            testOutput.WriteLine($"Selected {selected.Entry.Identity}; configuration SHA256 {Hash(configBytes.Span)}; explicit Framework {explicitFramework}");
            var nativeProject = capture.Snapshot.Effects.CreationResult.PrimaryOutputs.Single().Path;
            nativeProject.Should().Be(language == "C#" ? "Native.App.csproj" : "Native.App.fsproj");
            request.EffectSupport!.ExactProjectPath.Should().Be(Path.Combine(output.Info.FullName, nativeProject));
            Directory.Exists(output.Info.FullName).Should().BeFalse();
            fixture.Runner.Commands.Should().BeEmpty();
            var result = await selected.InvokeAsync(request, _deadline.Token);
            result.Status.Should().Be(TemplateInvocationStatus.Success, result.Diagnostic);
            result.ConfiguredProjects.Should().Equal(output.Info.FullName);
            File.Exists(Path.Combine(output.Info.FullName, ".gitignore")).Should().BeTrue();
            File.Exists(Path.Combine(output.Info.FullName, "_gitignore")).Should().BeFalse();
            File.ReadAllBytes(request.EffectSupport.ExactProjectPath).Should().Equal(
                ExpandedFile(capture.Snapshot, output, nativeProject).ToArray());
            using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(output.Info.FullName, ".func", "config.json")));
            config.RootElement.GetProperty("stack").GetProperty("runtime").GetString().Should().Be("dotnet");
            config.RootElement.GetProperty("stack").GetProperty("language").GetString().Should().Be(language);
            fixture.Runner.Commands.Last().Should().Equal("restore", request.EffectSupport.ExactProjectPath);
            fixture.Runner.Commands.Clear();
        }
    }

    [Theory]
    [InlineData("net8.0")]
    [InlineData("net48")]
    public async Task ProjectProvider_ExplicitUnsupportedFramework_RefusesBeforeEffects(string framework)
    {
        using var fixture = await OpenAsync();
        var selected = (await fixture.Templater.ResolveGroupAsync("func", TemplateType.Project, _deadline.Token)).Group![0];
        var parsed = Parse(selected, ["-tfm", framework]);
        parsed.ExplicitParameterNames.Should().Contain("Framework");
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "refused"));
        var command = new InitExecutionRequest(output, "func", null, "dotnet", selected.Entry.Language,
            false, true, false, false, ["-tfm", framework], new HashSet<string>());

        var prepared = await fixture.Provider.PrepareAsync(new NativeProjectWorkflowContext(fixture.Templater, selected, parsed, command,
            new(output, parsed.ValuesByCanonicalName)), _deadline.Token);

        prepared.Request.Should().BeNull();
        prepared.Diagnostics.Should().ContainSingle().Which.Should().Contain("only net10.0");
        Directory.Exists(output.Info.FullName).Should().BeFalse();
        fixture.Runner.Commands.Should().BeEmpty();
    }

    [Theory]
    [InlineData("C#", false)]
    [InlineData("C#", true)]
    [InlineData("F#", false)]
    [InlineData("F#", true)]
    public async Task ItemProvider_ActualImportedProjectAndNativeCapture_PreservesDefaultsBindingsAndRepairOrder(string language, bool explicitValues)
    {
        using var fixture = await OpenAsync(language);
        var selected = await SelectItemAsync(fixture, language);
        var parsed = Parse(selected, explicitValues ? ["--namespace", "Explicit.Unchanged", "--AccessRights", "Anonymous"] : ["--AccessRights", "Anonymous"]);
        parsed.ExplicitParameterNames.Contains("namespace").Should().Be(explicitValues);
        var requestedName = explicitValues ? "ExactUserName" : null;

        var prepared = await fixture.Provider.PrepareAsync(ItemContext(fixture, selected, parsed, requestedName), _deadline.Token);

        prepared.Diagnostics.Should().BeEmpty();
        var request = prepared.Request!;
        request.Name.Should().Be(explicitValues ? "ExactUserName" : selected.Entry.DefaultName);
        request.Name.Should().NotBe(Path.GetFileName(_root));
        request.Parameters["namespace"].Should().Be(explicitValues ? "Explicit.Unchanged" : "Imported.Actual.Namespace");
        parsed.ExplicitParameterNames.Contains("namespace").Should().Be(explicitValues);
        var binding = request.MsBuildBindings!;
        binding.ProjectFilePath.Should().Be(fixture.Project);
        binding.TemplateIdentity.Should().Be(selected.Entry.Identity);
        binding.SourceIdentity.Should().Be(selected.ConfigurationSourceIdentity);
        binding.SourceFingerprint.Should().Be(selected.ConfigurationSha256);
        binding.TargetFramework.Should().Be("net10.0");
        binding.RootNamespace.Should().Be("Imported.Actual.Namespace");
        var capture = await selected.CaptureNativePreparationAsync(request, new PhysicalFileSystem(), true, _deadline.Token);
        capture.Failure.Should().BeNull();
        var snapshot = capture.Snapshot!;
        snapshot.Source.Should().Be(new TemplateEffectSource(selected.Entry.Identity, selected.ConfigurationSourceIdentity!, selected.ConfigurationSha256!));
        var primary = snapshot.Effects.CreationResult.PrimaryOutputs.Single().Path;
        var expectedName = (explicitValues ? "ExactUserName" : selected.Entry.DefaultName) + (language == "C#" ? ".cs" : ".fs");
        primary.Should().Be(expectedName);
        snapshot.ExpandedFiles.Keys.Select(path => Path.GetFullPath(Path.Combine(fixture.Directory.Info.FullName, path)))
            .Should().Equal(Path.GetFullPath(Path.Combine(fixture.Directory.Info.FullName, primary)));
        snapshot.Effects.FileChanges.Select(change => change.ChangeKind.ToString()).Should().OnlyContain(kind => kind == "Create");
        var expanded = ExpandedFile(snapshot, fixture.Directory, primary);
        var generated = Path.Combine(fixture.Directory.Info.FullName, primary);
        File.Exists(generated).Should().BeFalse();
        fixture.Runner.Commands.Should().BeEmpty();
        var preview = await selected.InvokeAsync(request with { DryRun = true }, _deadline.Token);
        preview.Status.Should().Be(TemplateInvocationStatus.Success, preview.Diagnostic);
        preview.Effects!.Plan.Effects.Select(effect => effect.Source).Should().OnlyContain(source => source == snapshot.Source);
        if (language == "F#")
        {
            var repair = preview.Effects.Plan.Effects[0].Should().BeOfType<TemplateFSharpCompileEffect>().Subject;
            repair.Should().BeSameAs(request.EffectSupport!.FSharpRepair);
            repair.ProjectPath.Should().Be(fixture.Project);
            repair.ExpectedProjectSha256.Should().BeEquivalentTo(Hash(File.ReadAllBytes(fixture.Project!)));
            repair.GeneratedFiles.Should().ContainSingle().Which.Should().Be(new TemplateGeneratedFSharpFile(generated, Hash(expanded.Span)));
            preview.Effects.Plan.Effects[1].Should().BeOfType<TemplatePackageReferenceEffect>();
            preview.Effects.Plan.Effects[2].Should().BeOfType<TemplateRestoreEffect>();
        }
        fixture.Runner.BeforeCommand = _ =>
        {
            File.ReadAllBytes(generated).Should().Equal(expanded.ToArray());
            if (language == "F#")
            {
                XDocument.Load(fixture.Project!).Descendants("Compile").Select(item => item.Attribute("Include")!.Value)
                    .Should().Equal(primary, "Program.fs");
            }
        };
        var result = await selected.InvokeAsync(request, _deadline.Token);
        result.Status.Should().Be(TemplateInvocationStatus.Success, result.Diagnostic);
        File.ReadAllBytes(generated).Should().Equal(expanded.ToArray());
        File.ReadAllText(generated).Should().Contain("namespace " + (explicitValues ? "Explicit.Unchanged" : "Imported.Actual.Namespace"));
        fixture.Runner.Commands.Should().HaveCount(language == "F#" ? 2 : 1);
        if (language == "F#")
        {
            fixture.Runner.Commands.Last().Should().Equal("restore", fixture.Project!);
        }
        else
        {
            fixture.Runner.Commands.Single().Should().Equal("add", fixture.Project!, "package",
                "Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore", "--version", "2.1.0");
        }
    }

    [Theory]
    [InlineData("missing", "installed stack no longer")]
    [InlineData("ambiguous", "installed stack no longer")]
    [InlineData("framework", "Supported frameworks")]
    public async Task ItemProvider_InvalidActualProject_RefusesBeforeGeneration(string invalid, string diagnostic)
    {
        using var fixture = await OpenAsync("F#");
        var selected = await SelectItemAsync(fixture, "F#");
        var parsed = Parse(selected, []);
        if (invalid == "missing")
        {
            File.Delete(fixture.Project!);
        }
        else if (invalid == "ambiguous")
        {
            File.WriteAllText(Path.Combine(fixture.Directory.Info.FullName, "Other.csproj"), "<Project />");
        }
        else
        {
            File.WriteAllText(Path.Combine(fixture.Directory.Info.FullName, "evaluated.props"),
                "<Project><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
        }
        var prepared = await fixture.Provider.PrepareAsync(ItemContext(fixture, selected, parsed, "Refused"), _deadline.Token);
        prepared.Request.Should().BeNull();
        string.Join("; ", prepared.Diagnostics).Should().Contain(diagnostic);
        File.Exists(Path.Combine(fixture.Directory.Info.FullName, "Refused.fs")).Should().BeFalse();
        fixture.Runner.Commands.Should().BeEmpty();
    }

    [Theory]
    [InlineData("project")]
    [InlineData("occupancy")]
    [InlineData("noOptIn")]
    public async Task ItemProvider_ChangedOrUnconfirmedPreparedInvocation_RefusesWithoutDependencies(string invalid)
    {
        using var fixture = await OpenAsync("F#");
        var selected = await SelectItemAsync(fixture, "F#");
        var prepared = await fixture.Provider.PrepareAsync(ItemContext(fixture, selected, Parse(selected, []), "Refused"), _deadline.Token);
        prepared.Diagnostics.Should().BeEmpty();
        var request = prepared.Request!;
        var generated = Path.Combine(fixture.Directory.Info.FullName, "Refused.fs");
        if (invalid == "project")
        {
            File.AppendAllText(fixture.Project!, "\n<!-- user edit -->");
        }
        else if (invalid == "occupancy")
        {
            File.WriteAllText(generated, "user-owned");
        }
        else
        {
            request = request with { EffectSupport = request.EffectSupport! with { Confirmed = false } };
        }
        var before = File.ReadAllBytes(fixture.Project!);

        var result = await selected.InvokeAsync(request, _deadline.Token);

        result.Status.Should().NotBe(TemplateInvocationStatus.Success);
        result.Diagnostic.Should().Contain(invalid == "project" ? "changed" : invalid == "occupancy" ? "overwrites" : "not confirmed");
        File.ReadAllBytes(fixture.Project!).Should().Equal(before);
        fixture.Runner.Commands.Should().BeEmpty();
        if (invalid == "occupancy")
        {
            File.ReadAllText(generated).Should().Be("user-owned");
        }
        else
        {
            File.Exists(generated).Should().BeFalse();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ItemProvider_DependencyFailureOrCancellation_PreservesNativeSourceAndCommittedFSharpRepair(bool cancel)
    {
        using var fixture = await OpenAsync("F#");
        var selected = await SelectItemAsync(fixture, "F#");
        var prepared = await fixture.Provider.PrepareAsync(ItemContext(fixture, selected, Parse(selected, []), "RetainedHttp"), _deadline.Token);
        prepared.Diagnostics.Should().BeEmpty();
        var request = prepared.Request!;
        var captured = await selected.CaptureNativePreparationAsync(request, new PhysicalFileSystem(), true, _deadline.Token);
        using var canceled = CancellationTokenSource.CreateLinkedTokenSource(_deadline.Token);
        fixture.Runner.BeforeCommand = _ =>
        {
            if (cancel)
            {
                canceled.Cancel();
                canceled.Token.ThrowIfCancellationRequested();
            }
            throw new Azure.Functions.Cli.Templates.DotNet.Shared.DotnetCliException(1, "controlled dependency failure", "", "add");
        };

        if (cancel)
        {
            Func<Task> invoke = () => selected.InvokeAsync(request, canceled.Token);
            await invoke.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            var result = await selected.InvokeAsync(request, _deadline.Token);
            result.Status.Should().Be(TemplateInvocationStatus.EffectFailure);
            result.Effects!.Kind.Should().Be("partialFailure");
            result.Effects.CompletedEffects.Should().Contain(effect => effect is TemplateFSharpCompileEffect);
            result.Effects.FailedEffects.Should().Contain(effect => effect is TemplatePackageReferenceEffect);
        }
        File.ReadAllBytes(Path.Combine(fixture.Directory.Info.FullName, "RetainedHttp.fs"))
            .Should().Equal(ExpandedFile(captured.Snapshot!, fixture.Directory, "RetainedHttp.fs").ToArray());
        XDocument.Load(fixture.Project!).Descendants("Compile").Select(item => item.Attribute("Include")!.Value)
            .Should().Equal("RetainedHttp.fs", "Program.fs");
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("C#")]
    [InlineData("F#")]
    public async Task ItemProvider_RealDependenciesAndCompilation_ProducesNativeHttpFunctionMetadata(string language)
    {
        using var fixture = await OpenAsync(language);
        fixture.Runner.RealDependencies = true;
        var selected = await SelectItemAsync(fixture, language);
        var prepared = await fixture.Provider.PrepareAsync(ItemContext(fixture, selected, Parse(selected, ["--AccessRights", "Anonymous"]), "ActualHttp"), _deadline.Token);
        prepared.Diagnostics.Should().BeEmpty();

        var result = await selected.InvokeAsync(prepared.Request!, _deadline.Token);

        result.Status.Should().Be(TemplateInvocationStatus.Success, result.Diagnostic);
        await fixture.Runner.Real.RunAsync(["build", fixture.Project!, "--no-restore", "-v:minimal", "-p:NuGetAudit=false"],
            fixture.Directory.Info.FullName, _deadline.Token);
        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixture.Directory.Info.FullName,
            "bin", "Debug", "net10.0", "functions.metadata")));
        var function = metadata.RootElement.EnumerateArray().Single(item => item.GetProperty("name").GetString() == "ActualHttp");
        function.GetProperty("bindings").EnumerateArray().Should().Contain(item => item.GetProperty("type").GetString() == "httpTrigger");
        testOutput.WriteLine($"Actual {language} project {Path.GetFileName(fixture.Project)} SHA256 {Hash(File.ReadAllBytes(fixture.Project!))}");
        testOutput.WriteLine($"Selected source {selected.ConfigurationSourceIdentity}; SHA256 {selected.ConfigurationSha256}");
        testOutput.WriteLine($"Compiled HTTP metadata: {function.GetRawText()}");
        if (language == "F#")
        {
            StringBuilder output = new();
            await fixture.Runner.Real.RunStreamingAsync(["msbuild", fixture.Project!, "-nologo", "-getItem:Compile"],
                fixture.Directory.Info.FullName, line => output.AppendLine(line), null, _deadline.Token);
            using var compile = JsonDocument.Parse(output.ToString());
            compile.RootElement.GetProperty("Items").GetProperty("Compile").EnumerateArray()
                .Select(item => item.GetProperty("Identity").GetString()).Should().Equal("ActualHttp.fs", "Program.fs");
        }
    }

    private async Task<Fixture> OpenAsync(string? language = null, string? corruption = null)
    {
        var archive = Path.Combine(_root, "native.nupkg");
        using (var outer = ZipFile.OpenRead(Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "bundled-dotnet-artifacts"), "*.nupkg").Single()))
        {
            outer.GetEntry("tools/any/templates/dotnet.nupkg")!.ExtractToFile(archive);
        }
        var package = Path.Combine(_root, "mounted");
        ZipFile.ExtractToDirectory(archive, package);
        var itemConfig = Directory.GetFiles(package, "template.json", SearchOption.AllDirectories).Single(path =>
            JsonNode.Parse(File.ReadAllText(path))!["identity"]!.GetValue<string>() == "Azure.Function.FSharp.Isolated.HttpTrigger.3.x");
        if (corruption is not null)
        {
            var config = JsonNode.Parse(File.ReadAllText(itemConfig))!.AsObject();
            if (corruption == "unknownAction")
            {
                config["postActions"]!.AsArray().Add(new JsonObject { ["actionId"] = "ef409ba9-dbd8-41a9-ac62-f703bd22b777" });
            }
            else if (corruption == "layout")
            {
                config["sources"] = new JsonArray(new JsonObject { ["target"] = "nested/" });
            }
            File.WriteAllText(itemConfig, config.ToJsonString());
            using var changed = ZipFile.Open(archive, ZipArchiveMode.Update);
            var entry = changed.GetEntry(Path.GetRelativePath(package, itemConfig).Replace('\\', '/'))!;
            entry.Delete();
            using var writer = new StreamWriter(changed.CreateEntry(Path.GetRelativePath(package, itemConfig).Replace('\\', '/')).Open());
            writer.Write(config.ToJsonString());
        }
        var hive = Path.Combine(_root, "hive");
        await TemplateFixtures.InstallAsync(archive, hive, _deadline.Token);
        var directory = WorkingDirectory.FromExplicit(Path.Combine(_root, "actual-project"));
        string? project = null;
        if (language is not null)
        {
            using TemplateEngineSession setup = new(new(directory), hive);
            var identity = $"Microsoft.AzureFunctions.ProjectTemplate.{(language == "C#" ? "CSharp" : "FSharp")}.Isolated.3.x";
            var template = (await setup.PackageManager.GetTemplatesAsync(_deadline.Token)).Single(item => item.Identity == identity);
            var created = await new TemplateCreator(setup.Settings).InstantiateAsync(template, "Source.App", "Source.App",
                directory.Info.FullName, new Dictionary<string, string?> { ["Framework"] = "net10.0" }, cancellationToken: _deadline.Token);
            created.Status.Should().Be(Microsoft.TemplateEngine.Edge.Template.CreationResultStatus.Success);
            project = Path.Combine(directory.Info.FullName, created.CreationEffects!.CreationResult.PrimaryOutputs.Single().Path);
            var document = XDocument.Load(project);
            document.Root!.Add(new XElement("Import", new XAttribute("Project", "evaluated.props")));
            document.Save(project);
            File.WriteAllText(Path.Combine(directory.Info.FullName, "evaluated.props"),
                "<Project><PropertyGroup><RootNamespace>Imported.Actual.Namespace</RootNamespace></PropertyGroup></Project>");
            DirectoryInfo repository = new(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(repository.FullName, "NuGet.Config")))
            {
                repository = repository.Parent ?? throw new InvalidOperationException("Repository NuGet.Config not found.");
            }
            File.Copy(Path.Combine(repository.FullName, "NuGet.Config"), Path.Combine(directory.Info.FullName, "NuGet.Config"));
        }
        var pathResolver = Substitute.For<Azure.Functions.Cli.Templates.DotNet.Shared.IDotnetPathResolver>();
        pathResolver.Resolve().Returns("dotnet");
        var runner = new RecordingRunner(new Azure.Functions.Cli.Templates.DotNet.Shared.DotnetCliRunner(pathResolver));
        PhysicalRunner physical = Substitute.For<PhysicalRunner>();
        FunctionsProjectResolver projects = new([new(TestWorkloads.CreateInfo("Azure.Functions.Cli.Workloads.DotNet"),
            new DotNetProjectFactory(physical))]);
        var provider = new DotNetTemplateWorkflowComposer(projects, new PhysicalFileSystem(), runner,
            new Azure.Functions.Cli.Templates.DotNet.Shared.FSharpProjectFiles()).Create();
        TemplateProjectSupport support = new(new(new([new DotNetProjectStack()])), new(new PhysicalFileSystem()))
        {
            QualifyLayoutAsync = provider.QualifyLayoutAsync,
        };
        TemplateEngineContext context = language is null ? new(directory) : new(directory, new(directory, "dotnet", language));
        var session = new TemplateEngineSession(context, hive, projectSupport: support);
        return new(new(session), provider, runner, directory, project, archive, Path.GetRelativePath(package, itemConfig).Replace('\\', '/'));
    }

    private async Task<ResolvedTemplate> SelectItemAsync(Fixture fixture, string language)
    {
        var result = await fixture.Templater.ResolveGroupAsync(
            $"Azure.Function.{(language == "C#" ? "CSharp" : "FSharp")}.Isolated.HttpTrigger.3.x", TemplateType.Item, _deadline.Token);
        result.Status.Should().Be(TemplateResolutionStatus.Success);
        return result.Group!.Single();
    }

    private static TemplateCandidateParseResult Parse(ResolvedTemplate selected, IReadOnlyList<string> raw)
    {
        var parsed = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(), raw));
        parsed.IsComplete.Should().BeTrue(string.Join("; ", parsed.InvalidInputDiagnostics.Select(item => item.Message)));
        return parsed;
    }

    private static NativeItemWorkflowContext ItemContext(Fixture fixture, ResolvedTemplate selected, TemplateCandidateParseResult parsed, string? name)
        => new(fixture.Templater, selected, parsed,
            new(fixture.Directory, selected.Entry.Identity, name, selected.Entry.Language, false, true, false, false, false, [], new HashSet<string>()),
            new(fixture.Directory, parsed.ValuesByCanonicalName, name));

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static ReadOnlyMemory<byte> ExpandedFile(NativeTemplatePreparationSnapshot snapshot, WorkingDirectory root, string nativePath)
        => snapshot.ExpandedFiles.Single(file => Path.GetFullPath(Path.Combine(root.Info.FullName, file.Key))
            == Path.GetFullPath(Path.Combine(root.Info.FullName, nativePath))).Value;

    private sealed record Fixture(Templater Templater, DotNetTemplateWorkflowProvider Provider, RecordingRunner Runner,
        WorkingDirectory Directory, string? Project, string Archive, string ItemConfiguration) : IDisposable
    {
        public void Dispose() => Templater.Dispose();
    }

    private sealed class RecordingRunner(HostRunner real) : HostRunner
    {
        public HostRunner Real { get; } = real;
        public bool RealDependencies { get; set; }
        public Action<IReadOnlyList<string>>? BeforeCommand { get; set; }
        public List<IReadOnlyList<string>> Commands { get; } = [];

        public Task RunAsync(IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add(arguments.ToArray());
            BeforeCommand?.Invoke(arguments);
            return RealDependencies ? Real.RunAsync(arguments, workingDirectory, cancellationToken) : Task.CompletedTask;
        }

        public Task RunStreamingAsync(IReadOnlyList<string> arguments, string? workingDirectory, Action<string>? onOutputLine,
            Action<string>? onErrorLine, CancellationToken cancellationToken)
            => Real.RunStreamingAsync(arguments, workingDirectory, onOutputLine, onErrorLine, cancellationToken);
    }
}
