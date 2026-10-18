// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Profiles;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.DotNet;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Tests.Console;
using Azure.Functions.Cli.Tests.Templates.Engine;
using Azure.Functions.Cli.Workloads.DotNet;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using NuGet.Versioning;
using Shared = Azure.Functions.Cli.Templates.DotNet.Shared;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class DotNetTemplateWorkflowProviderTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("func-owned-dotnet-").FullName;
    private readonly TestInteractionService _interaction = new();
    private readonly List<ServiceProvider> _providers = [];

    private string Home => Path.Combine(_root, "home");

    public void Dispose()
    {
        foreach (ServiceProvider provider in _providers)
        {
            provider.Dispose();
        }
        Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [InlineData("Azure.Function.FSharp.Isolated.HttpTrigger.3.x", "--AccessRights", "Anonymous")]
    [InlineData("Azure.Function.FSharp.Isolated.BlobTrigger.3.x", "--Path", "incoming/orders\\archive\"/入荷")]
    public async Task ActualNewRunner_FSharpForcedReplacement_PreviewsWithoutWritesAndKeepsExistingCompilePosition(
        string identity, string option, string value)
    {
        await InstallAsync();
        using IServiceScope scope = Scope();
        NativeTemplateWorkflowComposer workflow = scope.ServiceProvider.GetRequiredService<NativeTemplateWorkflowComposer>();
        string root = Path.Combine(_root, "fsharp-force");
        (await workflow.ExecuteProjectAsync(Init(root, "fsharp"), CancellationToken.None)).Succeeded.Should().BeTrue();
        NewExecutionRequest first = Item(root) with { TemplateReference = identity, Name = "First", RawTemplateTokens = [option, value] };
        (await workflow.ExecuteItemAsync(first, CancellationToken.None)).Succeeded.Should().BeTrue();
        NewExecutionRequest later = Item(root) with { Name = "Later" };
        (await workflow.ExecuteItemAsync(later, CancellationToken.None)).Succeeded.Should().BeTrue();
        string project = Directory.GetFiles(root, "*.fsproj").Single();
        byte[] projectBytes = File.ReadAllBytes(project);
        string source = Path.Combine(root, "First.fs");
        byte[] rendered = File.ReadAllBytes(source);
        File.WriteAllText(source, "user content awaiting explicit replacement");
        var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        RecordingRunner runner = scope.ServiceProvider.GetRequiredService<RecordingRunner>();
        runner.Commands.Clear();

        NewExecutionResult preview = await workflow.ExecuteItemAsync(first with { Force = true, DryRun = true }, CancellationToken.None);

        preview.Succeeded.Should().BeTrue(Diagnostics(preview));
        foreach (var file in before)
        {
            File.ReadAllBytes(file.Key).Should().Equal(file.Value);
        }
        runner.Commands.Should().BeEmpty();
        NewExecutionResult applied = await workflow.ExecuteItemAsync(first with { Force = true }, CancellationToken.None);
        applied.Succeeded.Should().BeTrue(Diagnostics(applied));
        File.ReadAllBytes(source).Should().Equal(rendered);
        File.ReadAllBytes(project).Should().Equal(projectBytes);
        (await CompileAsync(runner, project)).Should().Equal("First.fs", "Later.fs", "Program.fs");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualNewRunner_CombinedSignalR_PreservesLiteralValuesAndAuxiliaryPrewriteAdmission(bool tooLong)
    {
        await InstallAsync();
        using IServiceScope scope = Scope();
        NativeTemplateWorkflowComposer workflow = scope.ServiceProvider.GetRequiredService<NativeTemplateWorkflowComposer>();
        string root = Path.Combine(_root, "combined-signalr");
        (await workflow.ExecuteProjectAsync(Init(root, "csharp"), CancellationToken.None)).Succeeded.Should().BeTrue();
        NewExecutionRequest request = Item(root) with
        {
            TemplateReference = "Azure.Function.CSharp.Isolated.SignalR.3.x",
            Name = tooLong ? new string('S', 129) : "SignalLiteral",
            RawTemplateTokens = ["--HubName", "hub\\archive\"\t\n入荷"],
        };
        var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);

        NewExecutionResult preview = await workflow.ExecuteItemAsync(request with { DryRun = true }, CancellationToken.None);
        NewExecutionResult result = await workflow.ExecuteItemAsync(request, CancellationToken.None);

        preview.Succeeded.Should().Be(!tooLong, Diagnostics(preview));
        result.Succeeded.Should().Be(!tooLong, Diagnostics(result));
        foreach (var file in before)
        {
            File.ReadAllBytes(file.Key).Should().Equal(file.Value);
        }
        if (tooLong)
        {
            Directory.GetFiles(root, "*", SearchOption.AllDirectories).Should().BeEquivalentTo(before.Keys);
        }
        else
        {
            result.ArgumentResults.Single().ValuesByCanonicalName["HubName"].Should().Be("hub\\archive\"\t\n入荷");
            File.ReadAllText(Path.Combine(root, "SignalLiteral.cs")).Should().ContainAll(
                "[Function(\"SignalLiteral\")]", "    public class SignalLiteralConnectionInfo", "HubName = \"hub\\\\archive\\\"\\t\\n入荷\"");
        }
    }

    [Theory]
    [InlineData("csharp", "Azure.Function.CSharp.Isolated.SqlTriggerBinding", "table", ".cs")]
    [InlineData("fsharp", "Azure.Function.FSharp.Isolated.BlobTrigger.3.x", "Path", ".fs")]
    public async Task ActualNewRunner_LiteralPreviewAndRepeatedOutput_PreserveFilesAndCanonicalValues(
        string language, string identity, string parameter, string extension)
    {
        await InstallAsync();
        using IServiceScope scope = Scope();
        NativeTemplateWorkflowComposer workflow = scope.ServiceProvider.GetRequiredService<NativeTemplateWorkflowComposer>();
        string root = Path.Combine(_root, "literal-preview");
        (await workflow.ExecuteProjectAsync(Init(root, language), CancellationToken.None)).Succeeded.Should().BeTrue();
        var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(
            path => path, File.ReadAllBytes, StringComparer.Ordinal);
        NewExecutionRequest request = Item(root) with
        {
            TemplateReference = identity,
            RawTemplateTokens = ["--" + parameter, "orders\\archive\"\t\n入荷"],
        };

        NewExecutionResult preview = await workflow.ExecuteItemAsync(request with { DryRun = true }, CancellationToken.None);

        preview.Succeeded.Should().BeTrue(Diagnostics(preview));
        preview.ArgumentResults.Single().ValuesByCanonicalName[parameter].Should().Be("orders\\archive\"\t\n入荷");
        File.Exists(Path.Combine(root, "Greeting" + extension)).Should().BeFalse();
        foreach (var file in before)
        {
            File.ReadAllBytes(file.Key).Should().Equal(file.Value);
        }
        NewExecutionResult created = await workflow.ExecuteItemAsync(request, CancellationToken.None);
        created.Succeeded.Should().BeTrue(Diagnostics(created));
        byte[] source = File.ReadAllBytes(Path.Combine(root, "Greeting" + extension));
        NewExecutionResult repeated = await workflow.ExecuteItemAsync(request, CancellationToken.None);
        repeated.Succeeded.Should().BeFalse();
        File.ReadAllBytes(Path.Combine(root, "Greeting" + extension)).Should().Equal(source);
    }

    [Theory]
    [InlineData("C#", "csharp", ".csproj", ".cs")]
    [InlineData("F#", "fsharp", ".fsproj", ".fs")]
    public async Task ActualInitAndNewRunners_ProductionProvider_BothCandidateLanguagesCreateAndPrepareWithoutTestCapabilities(
        string language, string alias, string projectExtension, string sourceExtension)
    {
        await InstallAsync();
        using IServiceScope scope = Scope();
        NativeTemplateWorkflowComposer workflow = scope.ServiceProvider.GetRequiredService<NativeTemplateWorkflowComposer>();
        string projectRoot = Path.Combine(_root, alias);
        InitExecutionRequest init = Init(projectRoot, alias);

        InitExecutionResult preview = await workflow.ExecuteProjectAsync(init with { DryRun = true }, CancellationToken.None);

        preview.Succeeded.Should().BeTrue(Diagnostics(preview));
        preview.ArgumentResults.Single().ValuesByCanonicalName["Framework"].Should().Be("net10.0");
        Directory.Exists(projectRoot).Should().BeFalse();
        preview.SelectedTemplate!.Language.Should().Be(language);
        preview.UnavailableTemplates.Where(entry => entry.Package?.Identifier == "Azure.Functions.Templates.DotNet")
            .Should().BeEmpty("the same project session must qualify both source-pinned language candidates, not latch one identity");
        InitExecutionResult created = await workflow.ExecuteProjectAsync(init, CancellationToken.None);
        created.Succeeded.Should().BeTrue(Diagnostics(created));
        string project = Directory.GetFiles(projectRoot, "*" + projectExtension).Single();
        File.ReadAllText(project).Should().Contain("<TargetFramework>net10.0</TargetFramework>");
        using var configuration = JsonDocument.Parse(File.ReadAllText(Path.Combine(projectRoot, ".func", "config.json")));
        configuration.RootElement.GetProperty("stack").GetProperty("language").GetString().Should().Be(language);
        NewExecutionRequest item = Item(projectRoot);
        NewExecutionResult itemPreview = await workflow.ExecuteItemAsync(item with { DryRun = true }, CancellationToken.None);
        itemPreview.Succeeded.Should().BeTrue(Diagnostics(itemPreview));
        File.Exists(Path.Combine(projectRoot, "Greeting" + sourceExtension)).Should().BeFalse();
        NewExecutionResult applied = await workflow.ExecuteItemAsync(item, CancellationToken.None);
        applied.Succeeded.Should().BeTrue(Diagnostics(applied));
        applied.ArgumentResults.Single().ValuesByCanonicalName["namespace"].Should().Be("OwnedApp");
        string content = File.ReadAllText(Path.Combine(projectRoot, "Greeting" + sourceExtension));
        content.Should().Contain("Anonymous");
        content.Should().NotContain("Company.Function");
        RecordingRunner runner = scope.ServiceProvider.GetRequiredService<RecordingRunner>();
        runner.Commands.Should().Contain(command => command.SequenceEqual(new[] { "restore", project }));
        if (language == "F#")
        {
            applied.Invocation!.Effects!.Plan.Effects[0].Should().BeOfType<TemplateFSharpCompileEffect>();
            (await CompileAsync(runner, project)).Should().Equal("Greeting.fs", "Program.fs");
        }
    }

    [Theory]
    [InlineData("C#", "csharp")]
    [InlineData("F#", "fsharp")]
    [Trait("Category", "Integration")]
    public async Task ActualRunners_ProductionProvider_RealDependenciesBuildNativeHttpMetadata(string language, string alias)
    {
        await InstallAsync();
        using IServiceScope scope = Scope(realDependencies: true);
        NativeTemplateWorkflowComposer workflow = scope.ServiceProvider.GetRequiredService<NativeTemplateWorkflowComposer>();
        string root = Path.Combine(_root, alias);
        DirectoryInfo repository = new(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(repository.FullName, "NuGet.Config")))
        {
            repository = repository.Parent ?? throw new InvalidOperationException("Repository NuGet.Config not found.");
        }
        File.Copy(Path.Combine(repository.FullName, "NuGet.Config"), Path.Combine(_root, "NuGet.Config"));
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(3));

        InitExecutionResult initialized = await workflow.ExecuteProjectAsync(Init(root, alias), timeout.Token);
        initialized.Succeeded.Should().BeTrue(Diagnostics(initialized));
        NewExecutionResult item = await workflow.ExecuteItemAsync(Item(root), timeout.Token);
        item.Succeeded.Should().BeTrue(Diagnostics(item));
        string project = Directory.GetFiles(root, language == "F#" ? "*.fsproj" : "*.csproj").Single();
        await scope.ServiceProvider.GetRequiredService<RecordingRunner>().RunAsync(
            ["build", project, "--no-restore", "-m:1", "-v:minimal", "-p:NuGetAudit=false"], root, timeout.Token);
        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "bin", "Debug", "net10.0", "functions.metadata")));
        metadata.RootElement.EnumerateArray().Select(function => function.GetProperty("name").GetString()).Should().Contain("Greeting");
    }

    [Theory]
    [InlineData("net8.0")]
    [InlineData("net48")]
    public async Task ActualInitRunner_UnsupportedFramework_RefusesBeforeCreation(string framework)
    {
        await InstallAsync();
        using IServiceScope scope = Scope();
        string root = Path.Combine(_root, "unsupported");
        InitExecutionRequest request = Init(root, "csharp") with { RawTemplateTokens = ["--target-framework", framework] };

        InitExecutionResult result = await scope.ServiceProvider.GetRequiredService<NativeTemplateWorkflowComposer>()
            .ExecuteProjectAsync(request, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        Directory.Exists(root).Should().BeFalse();
        scope.ServiceProvider.GetRequiredService<RecordingRunner>().Commands.Should().BeEmpty();
    }

    [Theory]
    [InlineData("ambiguous")]
    [InlineData("occupied")]
    [InlineData("wrongFramework")]
    public async Task ActualNewRunner_ChangedProjectOrTarget_RefusesWithoutRepairOrDependency(string invalid)
    {
        await InstallAsync();
        using IServiceScope scope = Scope();
        NativeTemplateWorkflowComposer workflow = scope.ServiceProvider.GetRequiredService<NativeTemplateWorkflowComposer>();
        string root = Path.Combine(_root, "source");
        (await workflow.ExecuteProjectAsync(Init(root, "fsharp"), CancellationToken.None)).Succeeded.Should().BeTrue();
        if (invalid == "ambiguous")
        {
            File.Copy(Directory.GetFiles(root, "*.fsproj").Single(), Path.Combine(root, "Second.fsproj"));
        }
        else if (invalid == "occupied")
        {
            File.WriteAllText(Path.Combine(root, "Greeting.fs"), "user-owned");
        }
        else
        {
            string project = Directory.GetFiles(root, "*.fsproj").Single();
            File.WriteAllText(project, File.ReadAllText(project).Replace("net10.0", "net8.0", StringComparison.Ordinal));
        }
        RecordingRunner runner = scope.ServiceProvider.GetRequiredService<RecordingRunner>();
        runner.Commands.Clear();

        NewExecutionResult result = await workflow.ExecuteItemAsync(Item(root), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        runner.Commands.Should().BeEmpty();
        if (invalid == "occupied")
        {
            File.ReadAllText(Path.Combine(root, "Greeting.fs")).Should().Be("user-owned");
        }
    }

    [Fact]
    public async Task ActualNewRunner_DependencyFailure_RetainsGeneratedSourceAndRequiredCompileRepairWithPartialFailure()
    {
        await InstallAsync();
        using IServiceScope scope = Scope();
        NativeTemplateWorkflowComposer workflow = scope.ServiceProvider.GetRequiredService<NativeTemplateWorkflowComposer>();
        string root = Path.Combine(_root, "partial");
        (await workflow.ExecuteProjectAsync(Init(root, "fsharp"), CancellationToken.None)).Succeeded.Should().BeTrue();
        RecordingRunner runner = scope.ServiceProvider.GetRequiredService<RecordingRunner>();
        runner.FailPackage = true;

        NewExecutionResult result = await workflow.ExecuteItemAsync(Item(root), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Invocation!.Effects!.Kind.Should().Be("partialFailure");
        File.Exists(Path.Combine(root, "Greeting.fs")).Should().BeTrue();
        (await CompileAsync(runner, Directory.GetFiles(root, "*.fsproj").Single())).Should().Equal("Greeting.fs", "Program.fs");
    }

    [Theory]
    [InlineData("--target-framework=net8.0")]
    [InlineData("-tfm:net8.0")]
    public async Task ActualInitRunner_ExplicitFrameworkForms_AreNotConfusedWithAuthoredDefaults(string argument)
    {
        await InstallAsync();
        using IServiceScope scope = Scope();
        string root = Path.Combine(_root, "explicit-framework");

        InitExecutionResult result = await scope.ServiceProvider.GetRequiredService<NativeTemplateWorkflowComposer>()
            .ExecuteProjectAsync(Init(root, "csharp") with { RawTemplateTokens = [argument] }, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        Directory.Exists(root).Should().BeFalse();
        scope.ServiceProvider.GetRequiredService<RecordingRunner>().Commands.Should().BeEmpty();
    }

    [Theory]
    [InlineData("csharp", ".cs")]
    [InlineData("fsharp", ".fs")]
    public async Task ActualNewRunner_ExplicitNamespace_RemainsCanonicalUserInput(string language, string extension)
    {
        await InstallAsync();
        using IServiceScope scope = Scope();
        NativeTemplateWorkflowComposer workflow = scope.ServiceProvider.GetRequiredService<NativeTemplateWorkflowComposer>();
        string root = Path.Combine(_root, "namespace");
        (await workflow.ExecuteProjectAsync(Init(root, language), CancellationToken.None)).Succeeded.Should().BeTrue();

        NewExecutionResult result = await workflow.ExecuteItemAsync(Item(root) with
        {
            RawTemplateTokens = ["--namespace", "User.Selected", "--AccessRights", "Anonymous"],
        }, CancellationToken.None);

        result.Succeeded.Should().BeTrue(Diagnostics(result));
        File.ReadAllText(Path.Combine(root, "Greeting" + extension)).Should().Contain("namespace User.Selected");
    }

    [Fact]
    public async Task ActualNewRunner_ChangedPinnedConfiguration_RefusesProductionCapabilityBeforeSourceCreation()
    {
        await InstallAsync(changeItemConfiguration: true);
        using IServiceScope scope = Scope();
        NativeTemplateWorkflowComposer workflow = scope.ServiceProvider.GetRequiredService<NativeTemplateWorkflowComposer>();
        string root = Path.Combine(_root, "changed-source");
        (await workflow.ExecuteProjectAsync(Init(root, "csharp"), CancellationToken.None)).Succeeded.Should().BeTrue();
        RecordingRunner runner = scope.ServiceProvider.GetRequiredService<RecordingRunner>();
        runner.Commands.Clear();

        NewExecutionResult result = await workflow.ExecuteItemAsync(Item(root), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Contains("pinned native source", StringComparison.Ordinal));
        File.Exists(Path.Combine(root, "Greeting.cs")).Should().BeFalse();
        runner.Commands.Should().BeEmpty();
    }

    [SymbolicLinkFact]
    public async Task ActualNewRunner_LinkedSelectedProject_RefusesAndPreservesExternalBytes()
    {
        await InstallAsync();
        using IServiceScope scope = Scope();
        NativeTemplateWorkflowComposer workflow = scope.ServiceProvider.GetRequiredService<NativeTemplateWorkflowComposer>();
        string root = Path.Combine(_root, "linked-project");
        (await workflow.ExecuteProjectAsync(Init(root, "csharp"), CancellationToken.None)).Succeeded.Should().BeTrue();
        string project = Directory.GetFiles(root, "*.csproj").Single();
        string outside = Path.Combine(_root, "outside.csproj");
        File.Move(project, outside);
        File.CreateSymbolicLink(project, outside);
        byte[] before = File.ReadAllBytes(outside);
        RecordingRunner runner = scope.ServiceProvider.GetRequiredService<RecordingRunner>();
        runner.Commands.Clear();

        NewExecutionResult result = await workflow.ExecuteItemAsync(Item(root), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        File.ReadAllBytes(outside).Should().Equal(before);
        File.Exists(Path.Combine(root, "Greeting.cs")).Should().BeFalse();
        runner.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task ActualNewRunner_CanceledDependency_DoesNotReportSuccessOrHideGeneratedAndRepairedSource()
    {
        await InstallAsync();
        using IServiceScope scope = Scope();
        NativeTemplateWorkflowComposer workflow = scope.ServiceProvider.GetRequiredService<NativeTemplateWorkflowComposer>();
        string root = Path.Combine(_root, "canceled");
        (await workflow.ExecuteProjectAsync(Init(root, "fsharp"), CancellationToken.None)).Succeeded.Should().BeTrue();
        using CancellationTokenSource cancellation = new();
        RecordingRunner runner = scope.ServiceProvider.GetRequiredService<RecordingRunner>();
        runner.CancelPackage = cancellation;
        Func<Task> action = () => workflow.ExecuteItemAsync(Item(root), cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        File.Exists(Path.Combine(root, "Greeting.fs")).Should().BeTrue();
        (await CompileAsync(runner, Directory.GetFiles(root, "*.fsproj").Single())).Should().Equal("Greeting.fs", "Program.fs");
    }

    [Theory]
    [InlineData("csharp", "HttpTriggerCSharp.cs")]
    [InlineData("fsharp", "HttpTriggerFSharp.fs")]
    public async Task ActualNewRunner_OmittedName_UsesAuthoredDefaultNotPrivateExpansionDirectory(string language, string expectedFile)
    {
        await InstallAsync();
        using IServiceScope scope = Scope();
        NativeTemplateWorkflowComposer workflow = scope.ServiceProvider.GetRequiredService<NativeTemplateWorkflowComposer>();
        string root = Path.Combine(_root, "named-project");
        (await workflow.ExecuteProjectAsync(Init(root, language), CancellationToken.None)).Succeeded.Should().BeTrue();

        NewExecutionResult result = await workflow.ExecuteItemAsync(Item(root) with { Name = null }, CancellationToken.None);

        result.Succeeded.Should().BeTrue(Diagnostics(result));
        File.Exists(Path.Combine(root, expectedFile)).Should().BeTrue();
    }

    [Fact]
    public void HostHelperCopy_IsCliOwnedAndNotAPhysicalWorkloadServiceOrRuntimeReference()
    {
        typeof(DotNetTemplateWorkflowProvider).Assembly.Should().BeSameAs(typeof(Shared.DotNetTemplateDependencyExecutor).Assembly);
        typeof(Shared.DotNetTemplateDependencyExecutor).Assembly.Should().NotBeSameAs(typeof(DotNetWorkload).Assembly);
        typeof(DotNetTemplateWorkflowProvider).Assembly.GetReferencedAssemblies()
            .Should().NotContain(reference => reference.Name == "Azure.Functions.Cli.Workloads.DotNet");
    }

    private IServiceScope Scope(bool realDependencies = false)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IInteractionService>(_interaction);
        services.AddSingleton<IFileSystem, PhysicalFileSystem>();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(new CliConfigurationPathsOptions(Home));
        services.AddSingleton<ILocalSettingsProvider, LocalSettingsProvider>();
        services.AddSingleton<ICliConfigurationProvider, CliConfigurationProvider>();
        services.AddOptions();
        services.AddSingleton<IConfigureOptions<StackOptions>, StackOptionsSetup>();
        services.AddSingleton<IConfigureOptions<ProjectProfileOptions>, ProjectProfileOptionsSetup>();
        IProfileResolver profile = Substitute.For<IProfileResolver>();
        profile.ResolveAsync(Arg.Any<ProfileResolutionContext>(), Arg.Any<CancellationToken>()).Returns(new ProfileResolution.None([]));
        services.AddSingleton(profile);
        new DotNetWorkload().Configure(new DefaultFunctionsCliBuilder(services, TestWorkloads.CreateInfo("Owned.DotNet")));
        services.AddSingleton<InstalledProjectStackCatalog>();
        services.AddSingleton<IFunctionsProjectResolver, FunctionsProjectResolver>();
        services.AddSingleton<IHostJsonBundleSectionReader, HostJsonBundleSectionReader>();
        services.AddSingleton(Substitute.For<IExtensionBundleResolver>());
        services.AddTemplatesOrchestrator();
        services.AddNativeTemplateWorkflowComposition();
        services.AddSingleton(new RecordingRunner(realDependencies));
        services.AddScoped(provider => new DotNetTemplateWorkflowComposer(provider.GetRequiredService<IFunctionsProjectResolver>(),
            provider.GetRequiredService<IFileSystem>(), provider.GetRequiredService<RecordingRunner>(), new Shared.FSharpProjectFiles()).Create());
        services.AddScoped<INativeItemWorkflowProvider>(provider => provider.GetRequiredService<DotNetTemplateWorkflowProvider>());
        services.AddScoped<INativeProjectWorkflowProvider>(provider => provider.GetRequiredService<DotNetTemplateWorkflowProvider>());
        INativeTemplateAvailability availability = Substitute.For<INativeTemplateAvailability>();
        availability.CaptureAsync(Arg.Any<CancellationToken>()).Returns(new TemplateConstraintEvaluationSnapshot(
            [new("Owned.DotNet", NuGetVersion.Parse("1.0.0"), ["dotnet"], true)]));
        services.AddSingleton(availability);
        ServiceProvider provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return provider.CreateScope();
    }

    [Theory]
    [InlineData("source")]
    [InlineData("derived")]
    public async Task ActualNewRunner_LiteralSourceOrDerivedSymbolDrift_RefusesPreviewAndCreationBeforeWrites(string drift)
    {
        await InstallAsync(literalDrift: drift);
        using IServiceScope scope = Scope();
        NativeTemplateWorkflowComposer workflow = scope.ServiceProvider.GetRequiredService<NativeTemplateWorkflowComposer>();
        string root = Path.Combine(_root, "literal-drift");
        (await workflow.ExecuteProjectAsync(Init(root, "csharp"), CancellationToken.None)).Succeeded.Should().BeTrue();
        NewExecutionRequest request = Item(root) with
        {
            TemplateReference = "Azure.Function.CSharp.Isolated.SqlTriggerBinding",
            RawTemplateTokens = ["--table", "orders\\archive"],
        };
        var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(
            path => path, File.ReadAllBytes, StringComparer.Ordinal);
        RecordingRunner runner = scope.ServiceProvider.GetRequiredService<RecordingRunner>();
        runner.Commands.Clear();

        NewExecutionResult preview = await workflow.ExecuteItemAsync(request with { DryRun = true }, CancellationToken.None);
        NewExecutionResult created = await workflow.ExecuteItemAsync(request, CancellationToken.None);

        preview.Succeeded.Should().BeFalse();
        created.Succeeded.Should().BeFalse();
        Directory.GetFiles(root, "*", SearchOption.AllDirectories).Should().BeEquivalentTo(before.Keys);
        foreach (var file in before)
        {
            File.ReadAllBytes(file.Key).Should().Equal(file.Value);
        }
        runner.Commands.Should().BeEmpty();
    }

    private async Task InstallAsync(bool changeItemConfiguration = false, string? literalDrift = null)
    {
        string outer = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "bundled-dotnet-artifacts"), "*.nupkg").Single();
        string archive = Path.Combine(_root, "native.nupkg");
        using (ZipArchive package = ZipFile.OpenRead(outer))
        {
            package.GetEntry("tools/any/templates/dotnet.nupkg")!.ExtractToFile(archive);
        }
        if (changeItemConfiguration)
        {
            using ZipArchive package = ZipFile.Open(archive, ZipArchiveMode.Update);
            ZipArchiveEntry entry = package.GetEntry("content/HttpTrigger-CSharp/.template.config/template.json")!;
            string content;
            using (StreamReader reader = new(entry.Open()))
            {
                content = reader.ReadToEnd();
            }
            entry.Delete();
            using StreamWriter writer = new(package.CreateEntry("content/HttpTrigger-CSharp/.template.config/template.json").Open());
            writer.Write(content + "\n ");
        }
        if (literalDrift is not null)
        {
            using ZipArchive package = ZipFile.Open(archive, ZipArchiveMode.Update);
            string path = "content/SqlTrigger-CSharp-Isolated/" + (literalDrift == "source"
                ? "SqlTriggerBindingCSharp.cs" : ".template.config/template.json");
            ZipArchiveEntry entry = package.GetEntry(path)!;
            string content;
            using (StreamReader reader = new(entry.Open()))
            {
                content = reader.ReadToEnd();
            }
            entry.Delete();
            using StreamWriter writer = new(package.CreateEntry(path).Open());
            writer.Write(literalDrift == "source" ? content.Replace("__FuncBinding_table__", "decoy", StringComparison.Ordinal)
                : content.Replace("\"valueSource\": \"table\"", "\"valueSource\": \"connectionStringSetting\"", StringComparison.Ordinal));
        }
        await TemplateFixtures.InstallAsync(archive, Path.Combine(Home, "templates"), CancellationToken.None);
    }

    private static InitExecutionRequest Init(string path, string language) => new(WorkingDirectory.FromExplicit(path), "func",
        "OwnedApp", "dotnet", language, false, true, false, false, [], new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    private static NewExecutionRequest Item(string path) => new(WorkingDirectory.FromExplicit(path), "http", "Greeting", null,
        false, true, false, false, false, ["--AccessRights", "Anonymous"], new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    private static string Diagnostics(InitExecutionResult result) => string.Join(" ", result.Diagnostics)
        + result.Preview?.Diagnostic + result.Invocation?.Diagnostic;
    private static string Diagnostics(NewExecutionResult result) => string.Join(" ", result.Diagnostics)
        + result.Invocation?.Diagnostic;

    private static async Task<string?[]> CompileAsync(Shared.IDotnetCliRunner runner, string project)
    {
        StringBuilder output = new();
        await runner.RunStreamingAsync(["msbuild", project, "-nologo", "-getItem:Compile"], Path.GetDirectoryName(project),
            line => output.AppendLine(line), null, CancellationToken.None);
        using var items = JsonDocument.Parse(output.ToString());
        return [.. items.RootElement.GetProperty("Items").GetProperty("Compile").EnumerateArray().Select(item => item.GetProperty("Identity").GetString())];
    }

    private sealed class RecordingRunner(bool realDependencies) : Shared.IDotnetCliRunner
    {
        private readonly Shared.DotnetCliRunner _real = new(new Shared.DotnetPathResolver());

        public List<IReadOnlyList<string>> Commands { get; } = [];
        public bool FailPackage { get; set; }
        public CancellationTokenSource? CancelPackage { get; set; }

        public Task RunAsync(IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add([.. arguments]);
            if (arguments[0] == "add" && CancelPackage is { } cancellation)
            {
                cancellation.Cancel();
                return Task.FromCanceled(cancellationToken);
            }
            return FailPackage && arguments[0] == "add"
                ? Task.FromException(new Shared.DotnetCliException(1, "controlled package failure", "", "add"))
                : realDependencies ? _real.RunAsync(arguments, workingDirectory, cancellationToken) : Task.CompletedTask;
        }

        public Task RunStreamingAsync(IReadOnlyList<string> arguments, string? workingDirectory, Action<string>? onOutputLine,
            Action<string>? onErrorLine, CancellationToken cancellationToken)
            => _real.RunStreamingAsync(arguments, workingDirectory, onOutputLine, onErrorLine, cancellationToken);
    }
}
