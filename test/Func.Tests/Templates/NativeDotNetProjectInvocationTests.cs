// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Tests.Templates.Engine;
using Azure.Functions.Cli.Workloads.DotNet;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class NativeDotNetProjectInvocationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("func-dotnet-project-invoke-").FullName;
    private readonly IDotnetCliRunner _runner = Substitute.For<IDotnetCliRunner>();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("7123")]
    public async Task QualifiedProject_ParsedCSharpHttpPort_PreservesNativeFallbackOrExplicitValue(string? explicitPort)
    {
        using ProjectCase source = await OpenProjectAsync("CSharp");
        using Templater resolver = new(source.Session);
        var selected = (await resolver.ResolveGroupAsync(source.Template.Identity, TemplateType.Project, CancellationToken.None)).Group![0];
        string frameworkAlias = "--" + selected.Parameters.Single(parameter => parameter.CanonicalName == "Framework").LongName;
        string[] tokens = explicitPort is null ? [frameworkAlias, "net10.0"] : [frameworkAlias, "net10.0", "--FunctionsHttpPort", explicitPort];
        var parsed = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(), tokens));
        parsed.IsComplete.Should().BeTrue(string.Join(" ", parsed.InvalidInputDiagnostics.Select(diagnostic => diagnostic.Message)
            .Concat(parsed.DefinitionDiagnostics)));

        var result = await selected.InvokeAsync(source.Request() with { Parameters = parsed.ValuesByCanonicalName }, CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.Success, result.Diagnostic);
        parsed.ValuesByCanonicalName.Should().Contain("Framework", "net10.0");
        File.ReadAllText(source.Project).Should().Contain("<TargetFramework>net10.0</TargetFramework>");
        using var launch = JsonDocument.Parse(File.ReadAllText(Path.Combine(source.Output, "Properties", "launchSettings.json")));
        string arguments = launch.RootElement.GetProperty("profiles").GetProperty("Resolved.App").GetProperty("commandLineArgs").GetString()!;
        if (explicitPort is not null)
        {
            arguments.Should().Be("--port " + explicitPort);
            parsed.ValuesByCanonicalName.Should().Contain("FunctionsHttpPort", explicitPort);
        }
        else
        {
            int port = int.Parse(arguments["--port ".Length..], System.Globalization.CultureInfo.InvariantCulture);
            port.Should().BeInRange(7000, 7300);
            parsed.ValuesByCanonicalName.Should().NotContainKey("FunctionsHttpPort");
        }
        await _runner.Received(1).RunAsync(Arg.Is<IReadOnlyList<string>>(arguments => arguments[0] == "restore"),
            source.Output, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("CSharp", ".csproj", "C#", 1)]
    [InlineData("FSharp", ".fsproj", "F#", 3)]
    public async Task QualifiedProject_ActualNativePreviewAndCreation_FinalizesConfigurationAndExecutesTypedDependencies(
        string language, string extension, string canonicalLanguage, int effectCount)
    {
        using ProjectCase source = await OpenProjectAsync(language);
        using Templater resolver = new(source.Session);
        TemplateResolutionResult resolution = await resolver.ResolveGroupAsync(source.Template.Identity, TemplateType.Project, CancellationToken.None);
        resolution.Status.Should().Be(TemplateResolutionStatus.Success);
        ResolvedTemplate selected = resolution.Group![0];

        TemplateInvocationResult preview = await selected.InvokeAsync(source.Request() with { DryRun = true }, CancellationToken.None);

        preview.Status.Should().Be(TemplateInvocationStatus.Success, preview.Diagnostic);
        preview.ProjectConfigurations.Should().ContainSingle().Which.PrimaryOutput.Should().Be(source.Project);
        preview.Effects!.Plan.Effects.Should().HaveCount(effectCount);
        Directory.Exists(source.Output).Should().BeFalse();
        _runner.ReceivedCalls().Should().BeEmpty();
        TemplateInvocationResult created = await selected.InvokeAsync(source.Request(), CancellationToken.None);
        created.Status.Should().Be(TemplateInvocationStatus.Success, created.Diagnostic);
        created.ConfiguredProjects.Should().Equal(source.Output);
        created.Effects!.Kind.Should().Be("applied");
        created.Effects.CompletedEffects.Should().HaveCount(effectCount);
        Path.GetExtension(source.Project).Should().Be(extension);
        File.ReadAllText(source.Project).Should().Contain("<TargetFramework>net10.0</TargetFramework>");
        File.Exists(Path.Combine(source.Output, ".gitignore")).Should().BeTrue();
        using var configuration = JsonDocument.Parse(File.ReadAllText(Path.Combine(source.Output, ".func", "config.json")));
        configuration.RootElement.GetProperty("stack").GetProperty("runtime").GetString().Should().Be("dotnet");
        configuration.RootElement.GetProperty("stack").GetProperty("language").GetString().Should().Be(canonicalLanguage);
        IReadOnlyList<string>[] commands = [.. _runner.ReceivedCalls().Select(call => (IReadOnlyList<string>)call.GetArguments()[0]!)];
        commands.Should().HaveCount(effectCount);
        commands[^1].Should().Equal("restore", source.Project);
        if (language == "FSharp")
        {
            commands[0].Should().Equal("add", source.Project, "package", "Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore", "--version", "2.0.2");
            commands[1].Should().Equal("add", source.Project, "package", "Microsoft.Azure.Functions.Worker.Extensions.Http", "--version", "3.2.0");
        }
    }

    [Theory]
    [InlineData("missingCapability")]
    [InlineData("declined")]
    [InlineData("wrongTarget")]
    [InlineData("wrongRoot")]
    [InlineData("failedPreflight")]
    public async Task QualifiedProject_MissingOrUnqualifiedEffectAuthority_RefusesBeforeNativeCreation(string refusal)
    {
        using ProjectCase source = await OpenProjectAsync("CSharp");
        source.Capability.RefusePreflight = refusal == "failedPreflight";
        TemplateInvocationRequest request = source.Request() with
        {
            EffectSupport = refusal == "missingCapability" ? null : new(
                WorkingDirectory.FromExplicit(refusal == "wrongRoot" ? _root : source.Output),
                refusal == "wrongTarget" ? Path.Combine(source.Output, "Guessed.csproj") : source.Project,
                source.Capability, Confirmed: refusal != "declined"),
        };

        TemplateInvocationResult result = await source.Session.InvokeAsync(source.Template, source.Configuration, request, CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.EffectFailure, result.Diagnostic);
        Directory.Exists(source.Output).Should().BeFalse();
        _runner.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("CSharp", "host")]
    [InlineData("CSharp", "project")]
    [InlineData("FSharp", "host")]
    [InlineData("FSharp", "project")]
    public async Task QualifiedProject_NativeDirectoryHostOrProject_RefusesBeforeCreationAndDependencies(string language, string directoryRole)
    {
        using ProjectCase source = await OpenProjectAsync(language, directoryRole: directoryRole);

        TemplateInvocationResult result = await source.InvokeAsync();

        result.Status.Should().Be(TemplateInvocationStatus.InvalidTemplate, result.Diagnostic);
        result.Changes.Should().BeEmpty();
        result.ConfiguredProjects.Should().BeEmpty();
        result.Effects.Should().BeNull();
        Directory.Exists(source.Output).Should().BeFalse();
        source.Capability.ValidationCalls.Should().Be(0);
        _runner.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("net8.0")]
    [InlineData("net48")]
    public async Task QualifiedProject_UnsupportedFramework_RefusesWithoutNewDefault(string? framework)
    {
        using ProjectCase source = await OpenProjectAsync("CSharp");
        Dictionary<string, string?> parameters = [];
        if (framework is not null)
        {
            parameters["Framework"] = framework;
        }

        TemplateInvocationRequest request = source.Request() with
        {
            Parameters = parameters,
        };

        TemplateInvocationResult result = await source.Session.InvokeAsync(source.Template, source.Configuration, request, CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.InvalidTemplate, result.Diagnostic);
        result.Diagnostic.Should().Contain("explicit Framework");
        Directory.Exists(source.Output).Should().BeFalse();
        _runner.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QualifiedProject_OccupiedCliConfiguration_RefusesEvenWithForce(bool directory)
    {
        using ProjectCase source = await OpenProjectAsync("FSharp");
        string config = Path.Combine(source.Output, ".func", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(config)!);
        if (directory)
        {
            Directory.CreateDirectory(config);
        }
        else
        {
            File.WriteAllText(config, "user-owned");
        }

        TemplateInvocationResult result = await source.InvokeAsync(force: true);

        result.Status.Should().Be(TemplateInvocationStatus.FileConflict);
        File.Exists(source.Project).Should().BeFalse();
        _runner.ReceivedCalls().Should().BeEmpty();
        if (!directory)
        {
            File.ReadAllText(config).Should().Be("user-owned");
        }
    }

    [Theory]
    [InlineData("rename")]
    [InlineData("sourceTarget")]
    [InlineData("primaryOutput")]
    public async Task QualifiedProject_UnqualifiedLayoutShape_RefusesWithoutChangingDefaultGuard(string corruption)
    {
        using ProjectCase source = await OpenProjectAsync("CSharp", root =>
        {
            if (corruption == "rename")
            {
                root["sources"]![0]!["modifiers"]![0]!["rename"]!["_gitignore"] = "different";
            }
            else if (corruption == "sourceTarget")
            {
                root["sources"]![0]!["target"] = "child/";
            }
            else
            {
                root["primaryOutputs"]![0]!["path"] = "different.csproj";
            }
        });

        TemplateInvocationResult result = await source.InvokeAsync();

        result.Status.Should().Be(TemplateInvocationStatus.InvalidTemplate, result.Diagnostic);
        Directory.Exists(source.Output).Should().BeFalse();
        _runner.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task QualifiedProject_UnknownNativeAction_RefusesBeforeAnyNativeFileOrDependency()
    {
        using ProjectCase source = await OpenProjectAsync("CSharp", root => root["postActions"]!.AsArray().Add(
            new JsonObject { ["actionId"] = "ef409ba9-dbd8-41a9-ac62-f703bd22b777", ["continueOnError"] = true }));

        TemplateInvocationResult result = await source.InvokeAsync();

        result.Status.Should().Be(TemplateInvocationStatus.EffectFailure);
        result.Diagnostic.Should().Contain("not handled");
        Directory.Exists(source.Output).Should().BeFalse();
        source.Capability.ValidationCalls.Should().Be(0);
        _runner.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task QualifiedProject_DependencyFailure_RetainsGeneratedConfigurationAndReportsPartialFailure()
    {
        using ProjectCase source = await OpenProjectAsync("FSharp");
        _runner.RunAsync(Arg.Is<IReadOnlyList<string>>(arguments => arguments[0] == "add"),
            Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(
                new DotnetCliException(1, "controlled dependency failure", "", "add")));

        TemplateInvocationResult result = await source.InvokeAsync();

        result.Status.Should().Be(TemplateInvocationStatus.EffectFailure);
        result.ConfiguredProjects.Should().Equal(source.Output);
        result.Effects!.Kind.Should().Be("partialFailure");
        result.Effects.FailedEffects.Should().HaveCount(2);
        result.Effects.CompletedEffects.Should().ContainSingle().Which.Should().BeOfType<TemplateRestoreEffect>();
        result.Diagnostic.Should().Contain("configuration were generated").And.Contain("controlled dependency failure");
        File.Exists(source.Project).Should().BeTrue();
        File.Exists(Path.Combine(source.Output, ".func", "config.json")).Should().BeTrue();
    }

    [Fact]
    public async Task QualifiedProject_CancellationDuringDependency_DoesNotReportSuccessOrHideGeneratedFiles()
    {
        using ProjectCase source = await OpenProjectAsync("FSharp");
        using CancellationTokenSource cancellation = new();
        _runner.RunAsync(Arg.Is<IReadOnlyList<string>>(arguments => arguments[0] == "add"), Arg.Any<string>(), cancellation.Token)
            .Returns(_ =>
            {
                cancellation.Cancel();
                return Task.FromCanceled(cancellation.Token);
            });
        Func<Task> action = () => source.Session.InvokeAsync(source.Template, source.Configuration, source.Request(), cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        File.Exists(source.Project).Should().BeTrue();
        File.Exists(Path.Combine(source.Output, ".func", "config.json")).Should().BeTrue();
        await _runner.DidNotReceive().RunAsync(Arg.Is<IReadOnlyList<string>>(arguments => arguments[0] == "restore"),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [SymbolicLinkFact]
    public async Task QualifiedProject_LinkedProjectOutput_RefusesEvenWithForceAndPreservesExternalBytes()
    {
        using ProjectCase source = await OpenProjectAsync("CSharp");
        Directory.CreateDirectory(source.Output);
        string external = Path.Combine(_root, "outside.csproj");
        File.WriteAllText(external, "external sentinel");
        File.CreateSymbolicLink(source.Project, external);

        TemplateInvocationResult result = await source.InvokeAsync(force: true);

        result.Status.Should().Be(TemplateInvocationStatus.Failed);
        result.Diagnostic.Should().Contain("symbolic link or reparse point");
        File.ReadAllText(external).Should().Be("external sentinel");
        _runner.ReceivedCalls().Should().BeEmpty();
    }

    [SymbolicLinkFact]
    public async Task QualifiedProject_LinkedCliConfigurationDirectory_RefusesBeforeNativeCreation()
    {
        using ProjectCase source = await OpenProjectAsync("FSharp");
        Directory.CreateDirectory(source.Output);
        string external = Path.Combine(_root, "outside");
        Directory.CreateDirectory(external);
        Directory.CreateSymbolicLink(Path.Combine(source.Output, ".func"), external);

        TemplateInvocationResult result = await source.InvokeAsync(force: true);

        result.Status.Should().Be(TemplateInvocationStatus.Failed);
        result.Diagnostic.Should().Contain("symbolic link or reparse point");
        File.Exists(source.Project).Should().BeFalse();
        Directory.GetFileSystemEntries(external).Should().BeEmpty();
        _runner.ReceivedCalls().Should().BeEmpty();
    }

    [SymbolicLinkFact]
    public async Task QualifiedProject_ExplicitLinkedInvocationRoot_PreservesExistingBoundarySemantics()
    {
        using ProjectCase source = await OpenProjectAsync("CSharp");
        string external = Path.Combine(_root, "outside");
        Directory.CreateDirectory(external);
        Directory.CreateSymbolicLink(source.Output, external);

        TemplateInvocationResult result = await source.InvokeAsync(force: true);

        result.Status.Should().Be(TemplateInvocationStatus.Success, result.Diagnostic);
        File.Exists(Path.Combine(external, Path.GetFileName(source.Project))).Should().BeTrue();
        File.Exists(Path.Combine(external, ".func", "config.json")).Should().BeTrue();
        result.ConfiguredProjects.Should().Equal(source.Output);
    }

    [SymbolicLinkFact]
    public async Task QualifiedProject_OutputBecomesLinkDuringConfiguration_RefusesBeforeDependencies()
    {
        ConfigurationMutationFileSystem files = new();
        using ProjectCase source = await OpenProjectAsync("CSharp", writerFiles: files);
        string external = Path.Combine(_root, "outside.csproj");
        File.WriteAllText(external, "external sentinel");
        files.AfterMove = () =>
        {
            File.Delete(source.Project);
            File.CreateSymbolicLink(source.Project, external);
        };

        TemplateInvocationResult result = await source.InvokeAsync();

        result.Status.Should().Be(TemplateInvocationStatus.EffectFailure);
        result.Diagnostic.Should().Contain("symbolic link or reparse point");
        result.Effects!.Kind.Should().Be("preflightFailed");
        File.ReadAllText(external).Should().Be("external sentinel");
        _runner.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task QualifiedProject_SelectedSourceChangesAfterResolution_RefusesBeforeNativeCreation()
    {
        using ProjectCase source = await OpenProjectAsync("CSharp", _ => { });
        using Templater resolver = new(source.Session);
        TemplateResolutionResult resolution = await resolver.ResolveGroupAsync(source.Template.Identity, TemplateType.Project, CancellationToken.None);
        resolution.Status.Should().Be(TemplateResolutionStatus.Success);
        string path = Path.Combine(_root, "candidate", "content", "ProjectTemplate-CSharp", ".template.config", "template.json");
        File.AppendAllText(path, "\n ");

        TemplateInvocationResult result = await resolution.Group![0].InvokeAsync(source.Request(), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.TemplateChanged);
        Directory.Exists(source.Output).Should().BeFalse();
        _runner.ReceivedCalls().Should().BeEmpty();
    }

    private async Task<ProjectCase> OpenProjectAsync(
        string language, Action<JsonObject>? mutate = null, IFileSystem? writerFiles = null, string? directoryRole = null)
    {
        string outerPath = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "bundled-dotnet-artifacts"), "*.nupkg").Single();
        string archive = Path.Combine(_root, "Azure.Functions.Templates.DotNet.nupkg");
        using (ZipArchive outer = ZipFile.OpenRead(outerPath))
        {
            outer.GetEntry("tools/any/templates/dotnet.nupkg")!.ExtractToFile(archive);
        }

        string package = archive;
        if (mutate is not null || directoryRole is not null)
        {
            package = Path.Combine(_root, "candidate");
            ZipFile.ExtractToDirectory(archive, package);
            string config = Path.Combine(package, "content", "ProjectTemplate-" + language, ".template.config", "template.json");
            if (mutate is not null)
            {
                JsonObject root = JsonNode.Parse(File.ReadAllText(config))!.AsObject();
                mutate(root);
                File.WriteAllText(config, root.ToJsonString());
            }
            if (directoryRole is not null)
            {
                string entry = directoryRole == "host" ? "host.json" : "Company.FunctionApp" + (language == "FSharp" ? ".fsproj" : ".csproj");
                string path = Path.Combine(package, "content", "ProjectTemplate-" + language, entry);
                File.Delete(path);
                Directory.CreateDirectory(path);
                File.WriteAllText(Path.Combine(path, "-.-"), string.Empty);
            }
        }

        string hive = Path.Combine(_root, "hive");
        await TemplateFixtures.InstallAsync(package, hive);
        ITemplateInfo template;
        TemplateConfigurationSnapshot configuration;
        string output = Path.Combine(_root, "output");
        string project;
        using (TemplateEngineSession setup = new(new(WorkingDirectory.FromExplicit(_root)), hive))
        {
            template = (await setup.PackageManager.GetTemplatesAsync(CancellationToken.None))
                .Single(item => item.Identity == $"Microsoft.AzureFunctions.ProjectTemplate.{language}.Isolated.3.x");
            configuration = await setup.ReadConfigurationAsync(template, CancellationToken.None);
            ITemplateCreationResult preview = await new TemplateCreator(setup.Settings).InstantiateAsync(template, "Resolved.App",
                "Resolved.App", output, new Dictionary<string, string?> { ["Framework"] = "net10.0" },
                dryRun: true, cancellationToken: CancellationToken.None);
            preview.Status.Should().Be(CreationResultStatus.Success);
            project = Path.GetFullPath(Path.Combine(output, preview.CreationEffects!.CreationResult.PrimaryOutputs.Single().Path));
        }

        TemplateProjectSupport support = new(new(new([new DotNetProjectStack()])), new(writerFiles ?? new PhysicalFileSystem()))
        {
            DotNetLayout = new(template.Identity, configuration.Fingerprint),
        };
        TemplateEngineSession session = new(new(WorkingDirectory.FromExplicit(_root)), hive, projectSupport: support);
        ProjectDependencyCapability capability = new(new(_runner), session, template, configuration, output, project);
        return new(session, template, configuration, output, project, capability);
    }

    private sealed class ConfigurationMutationFileSystem : PhysicalFileSystem
    {
        public Action? AfterMove { get; set; }

        public override void MoveFile(string sourcePath, string destinationPath, bool overwrite = false)
        {
            base.MoveFile(sourcePath, destinationPath, overwrite);
            AfterMove?.Invoke();
        }
    }

    private sealed record ProjectCase(TemplateEngineSession Session, ITemplateInfo Template, TemplateConfigurationSnapshot Configuration,
        string Output, string Project, ProjectDependencyCapability Capability) : IDisposable
    {
        public TemplateInvocationRequest Request() => new(WorkingDirectory.FromExplicit(Output),
            new Dictionary<string, string?> { ["Framework"] = "net10.0" }, "Resolved.App")
        {
            EffectSupport = new(WorkingDirectory.FromExplicit(Output), Project, Capability, Confirmed: true),
        };

        public Task<TemplateInvocationResult> InvokeAsync(bool dryRun = false, bool force = false)
            => Session.InvokeAsync(Template, Configuration, Request() with { DryRun = dryRun, Force = force }, CancellationToken.None);

        public void Dispose() => Session.Dispose();
    }

    private sealed class ProjectDependencyCapability(DotNetTemplateDependencyExecutor executor, TemplateEngineSession session,
        ITemplateInfo template, TemplateConfigurationSnapshot configuration, string output, string project) : INativeTemplateEffectCapability
    {
        private readonly TemplateEffectSource _source = new(template.Identity, $"{template.MountPointUri}::{template.ConfigPlace}", configuration.Fingerprint);

        public bool RefusePreflight { get; set; }

        public int ValidationCalls { get; private set; }

        public async Task<IReadOnlyList<string>> ValidateAsync(NativeTemplateEffectPlan plan, CancellationToken cancellationToken)
        {
            ValidationCalls++;
            TemplateConfigurationSnapshot current = await session.ReadConfigurationAsync(template, cancellationToken);
            return RefusePreflight || current.Fingerprint != _source.Sha256
                || plan.OutputDirectory.Info.FullName != output
                || plan.Effects.Any(effect => effect.Source != _source || effect.TargetPath != project
                    || effect is not (TemplatePackageReferenceEffect or TemplateRestoreEffect))
                ? ["The selected project dependency plan is not qualified."] : [];
        }

        public async Task<TemplateEffectExecutionResult> ExecuteAsync(TemplateEffect effect, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Exists(Path.Combine(output, ".func", "config.json")).Should().BeTrue("mandatory configuration precedes dependency execution");
            File.ReadAllText(project).Should().Contain("<TargetFramework>net10.0</TargetFramework>");
            try
            {
                switch (effect)
                {
                    case TemplatePackageReferenceEffect package:
                        await executor.AddPackageReferenceAsync(package.ProjectPath,
                            new(package.PackageId, package.Version, package.ProjectFileExtension), cancellationToken);
                        break;
                    case TemplateRestoreEffect restore:
                        await executor.RestoreAsync(restore.ProjectPath, cancellationToken);
                        break;
                    default:
                        return new(false, ["Unexpected effect in the test-only project capability."]);
                }

                return new(true, []);
            }
            catch (DotnetCliException exception)
            {
                return new(false, [exception.Message], TargetMayHaveChanged: true);
            }
        }
    }
}
