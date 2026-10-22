// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Go;
using Azure.Functions.Cli.Templates.Java;
using Azure.Functions.Cli.Templates.PowerShell;
using Azure.Functions.Cli.Templates.Projects;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NuGet.Packaging;
using HostGoPreparation = Azure.Functions.Cli.Templates.Go.Shared.GoProjectPreparation;
using HostPowerShellPreparation = Azure.Functions.Cli.Templates.PowerShell.Shared.PowerShellProjectPreparation;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledRemainingProjectTemplateTests
{
    [Fact]
    public void OwnedProviders_HostCompiledHelpers_AreSeparateFromUnchangedPhysicalHelperOracles()
    {
        typeof(HostGoPreparation).Assembly.FullName.Should().Be(typeof(GoNativeProjectPreparation).Assembly.FullName)
            .And.NotBe(typeof(Azure.Functions.Cli.Workloads.Go.GoProjectPreparation).Assembly.FullName);
        typeof(HostPowerShellPreparation).Assembly.FullName.Should().Be(typeof(PowerShellNativeProjectPreparation).Assembly.FullName)
            .And.NotBe(typeof(Azure.Functions.Cli.Workloads.PowerShell.PowerShellProjectPreparation).Assembly.FullName);
        new HostPowerShellPreparation().ReadRequirementsTemplate().Should()
            .Be(new Azure.Functions.Cli.Workloads.PowerShell.PowerShellProjectPreparation().ReadRequirementsTemplate());
    }

    [Fact]
    public void OwnedProviders_NullDependencies_AreProgrammerErrors()
    {
        Action[] calls =
        [
            () => new GoNativeProjectPreparation(null!, new HostGoPreparation()),
            () => new GoNativeProjectPreparation(_files, null!),
            () => new PowerShellNativeProjectPreparation(null!, new HostPowerShellPreparation()),
            () => new PowerShellNativeProjectPreparation(_files, null!),
            () => new GoProjectWorkflowProvider(null!),
            () => new PowerShellProjectWorkflowProvider(null!),
            () => new OwnedProjectPreparationSource(null!),
        ];
        foreach (Action call in calls) call.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("Go", "go", 8)]
    [InlineData("Java", "java", 8)]
    [InlineData("PowerShell", "powershell", 7)]
    public void OwnedProviders_ActualNativePack_ContainsExactVersionedSourceManifest(string stack, string alias, int count)
    {
        using PackageArchiveReader outer = new(BuiltArchive(stack));
        using var bytes = new MemoryStream();
        outer.GetStream($"tools/any/templates/{alias}-basic.nupkg").CopyTo(bytes);
        bytes.Position = 0;
        using PackageArchiveReader native = new(bytes);
        using var manifest = JsonDocument.Parse(Read(native, $"provenance/{alias}/project.json"));
        JsonElement root = manifest.RootElement;
        root.GetProperty("packageId").GetString().Should().Be(native.NuspecReader.GetId());
        root.GetProperty("packageVersion").GetString().Should().Be(native.NuspecReader.GetVersion().ToNormalizedString());
        root.GetProperty("identity").GetString().Should().Be($"Azure.Functions.{stack}.Basic");
        var sources = root.GetProperty("sources").EnumerateArray().ToArray();
        sources.Should().HaveCount(count);
        foreach (JsonElement source in sources)
        {
            using Stream content = native.GetStream(source.GetProperty("path").GetString()!);
            Convert.ToHexString(SHA256.HashData(content)).Should().Be(source.GetProperty("sha256").GetString());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnedGoProvider_RealNativeInitPreview_DeclaresSkipWithoutExecutingTool(bool skip)
    {
        using var services = await InstallAsync("Go");
        HostGoTool helper = new();
        INativeProjectWorkflowProvider provider = new GoProjectWorkflowProvider(new(_files, helper));
        var request = GoPreparationRequest(provider.Preparation!, skip, dryRun: true);

        var result = await ExecuteOwnedProjectAsync(services, provider, request, CancellationToken.None);

        result.Kind.Should().Be("previewed");
        result.Preparation!.Preview.Operations.Should().ContainSingle().Which.Skipped.Should().Be(skip);
        provider.Handles(result.SelectedTemplate!).Should().BeTrue();
        helper.Calls.Should().Be(0);
        Directory.Exists(request.WorkingDirectory.Info.FullName).Should().BeFalse();
    }

    [Theory]
    [InlineData(false, 0, "")]
    [InlineData(false, 0, "successful tidy stderr")]
    [InlineData(false, 17, "module resolution failed")]
    [InlineData(false, -1, "Go process unavailable")]
    [InlineData(true, 17, "must not be executed")]
    public async Task OwnedGoProvider_ActualNativeInit_UsesSharedHelperAfterConfigurationAndPreservesResult(
        bool skip, int exitCode, string stderr)
    {
        using var services = await InstallAsync("Go");
        HostGoTool helper = new() { Outcome = (exitCode, stderr) };
        INativeProjectWorkflowProvider provider = new GoProjectWorkflowProvider(new(_files, helper));
        var request = GoPreparationRequest(provider.Preparation!, skip);

        var result = await ExecuteOwnedProjectAsync(services, provider, request, CancellationToken.None);

        bool success = skip || exitCode == 0;
        result.Kind.Should().Be(success ? "created" : "partialPreparation");
        result.Succeeded.Should().Be(success);
        helper.Calls.Should().Be(skip ? 0 : 1);
        File.Exists(Path.Combine(request.WorkingDirectory.Info.FullName, "main.go")).Should().BeTrue();
        File.Exists(Path.Combine(request.WorkingDirectory.Info.FullName, ".func", "config.json")).Should().BeTrue();
        if (!skip && stderr.Length > 0) result.Preparation!.Completion!.Diagnostics.Should().Contain(stderr);
        if (!success)
        {
            TestInteractionService interaction = new();
            new InitExecutionRenderer(interaction).Render(result, jsonOutput: false).Should().Be(1);
            interaction.AllOutput.Should().Contain(stderr).And.NotContain("SUCCESS:");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnedGoProvider_CanceledSharedHelper_PropagatesAndRetainsActualNativeFiles(bool throws)
    {
        using var services = await InstallAsync("Go");
        using CancellationTokenSource cancellation = new();
        HostGoTool helper = new() { DuringExecution = cancellation.Cancel, ThrowCancellation = throws };
        var provider = new GoProjectWorkflowProvider(new(_files, helper));
        var request = GoPreparationRequest(provider.Preparation, skip: false);

        Func<Task> action = () => ExecuteOwnedProjectAsync(services, provider, request, cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        helper.Calls.Should().Be(1);
        File.Exists(Path.Combine(request.WorkingDirectory.Info.FullName, ".func", "config.json")).Should().BeTrue();
    }

    [Theory]
    [InlineData(false, null, "7.4")]
    [InlineData(true, "12", "7.4")]
    [InlineData(true, "14", "7.6")]
    [InlineData(true, null, "7.6")]
    public async Task OwnedPowerShellProvider_ActualNativeInit_FreezesExactPreviewContentAndLookupDiagnostics(
        bool enabled, string? major, string runtime)
    {
        using var services = await InstallAsync("PowerShell");
        HostAzLookup helper = new(major);
        INativeProjectWorkflowProvider provider = new PowerShellProjectWorkflowProvider(new(_files, helper));
        var request = PowerShellPreparationRequest(provider.Preparation!, enabled) with
        {
            RawTemplateTokens = ["--managed-dependencies", enabled.ToString(), "--runtime-version", runtime],
        };

        var result = await ExecuteOwnedProjectAsync(services, provider, request, CancellationToken.None);

        result.Kind.Should().Be("created", string.Join(" ", result.Diagnostics));
        result.Succeeded.Should().BeTrue();
        provider.Handles(result.SelectedTemplate!).Should().BeTrue();
        helper.Lookups.Should().Be(enabled ? 1 : 0, "Complete must apply the preview outcome without a second lookup");
        string target = Path.Combine(request.WorkingDirectory.Info.FullName, "requirements.psd1");
        File.Exists(target).Should().Be(enabled);
        if (enabled)
        {
            var planned = (TemplateCreateFileEffect)result.Preparation!.Preview.FileEffects!.Effects.Single();
            string original = new HostPowerShellPreparation().ReadRequirementsTemplate();
            planned.Content.Should().Be(major is null ? original : original.Replace("MAJOR_VERSION", major, StringComparison.Ordinal));
            File.ReadAllText(target).Should().Be(planned.Content);
            result.Preparation.Completion!.Effects!.CompletedEffects.Should().ContainSingle().Which.Should().Be(planned);
            if (major is null)
            {
                result.Preparation.Preview.Diagnostics.Should().ContainSingle().Which.Should().Contain("Could not resolve", "commented", "MAJOR_VERSION");
                result.Preparation.Completion.Diagnostics.Should().Contain(diagnostic => diagnostic.Contains("Could not resolve", StringComparison.Ordinal));
            }
        }
        else
        {
            result.Preparation!.Preview.FileEffects!.Effects.Should().BeEmpty();
            helper.SourceReads.Should().Be(0);
        }
    }

    [Fact]
    public async Task OwnedPowerShellProvider_DryRun_ResolvesOnceButNeverCreatesLiveRequirements()
    {
        using var services = await InstallAsync("PowerShell");
        HostAzLookup helper = new("12");
        var provider = new PowerShellProjectWorkflowProvider(new(_files, helper));
        var request = PowerShellPreparationRequest(provider.Preparation, enabled: true) with { DryRun = true };

        var result = await ExecuteOwnedProjectAsync(services, provider, request, CancellationToken.None);

        result.Kind.Should().Be("previewed");
        helper.Lookups.Should().Be(1);
        ((TemplateCreateFileEffect)result.Preparation!.Preview.FileEffects!.Effects.Single()).Content.Should().Contain("# 'Az' = '12.*'");
        result.Preparation.Completion.Should().BeNull();
        Directory.Exists(request.WorkingDirectory.Info.FullName).Should().BeFalse();
    }

    [Fact]
    public async Task OwnedPowerShellProvider_CanceledReadOnlyLookup_PropagatesBeforeAnyNativeWrite()
    {
        using var services = await InstallAsync("PowerShell");
        using CancellationTokenSource cancellation = new();
        HostAzLookup helper = new(null) { DuringLookup = cancellation.Cancel };
        var provider = new PowerShellProjectWorkflowProvider(new(_files, helper));
        var request = PowerShellPreparationRequest(provider.Preparation, enabled: true);

        Func<Task> action = () => ExecuteOwnedProjectAsync(services, provider, request, cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        helper.Lookups.Should().Be(1);
        Directory.Exists(request.WorkingDirectory.Info.FullName).Should().BeFalse();
    }

    [Theory]
    [InlineData("Go")]
    [InlineData("PowerShell")]
    public async Task OwnedProviders_UnexpectedHelperFault_PropagatesInsteadOfUserErrorFallback(string stack)
    {
        using var services = await InstallAsync(stack);
        InvalidOperationException failure = new("unexpected owned helper implementation fault");
        INativeProjectWorkflowProvider provider = stack == "Go"
            ? new GoProjectWorkflowProvider(new(_files, new HostGoTool { Failure = failure }))
            : new PowerShellProjectWorkflowProvider(new(_files, new HostAzLookup("12") { Failure = failure }));
        var request = stack == "Go" ? GoPreparationRequest(provider.Preparation!, skip: false)
            : PowerShellPreparationRequest(provider.Preparation!, enabled: true);

        Func<Task> action = () => ExecuteOwnedProjectAsync(services, provider, request, CancellationToken.None);

        (await action.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);
    }

    [Theory]
    [InlineData("6.0")]
    [InlineData("7.5")]
    public async Task OwnedPowerShellProvider_UnsupportedRuntime_RejectsBeforeLookupOrNativeWrites(string runtime)
    {
        using var services = await InstallAsync("PowerShell");
        HostAzLookup helper = new("12");
        var provider = new PowerShellProjectWorkflowProvider(new(_files, helper));
        var request = PowerShellPreparationRequest(provider.Preparation, enabled: true) with
        {
            RawTemplateTokens = ["--managed-dependencies", "true", "--runtime-version", runtime],
        };

        var result = await ExecuteOwnedProjectAsync(services, provider, request, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        helper.Lookups.Should().Be(0);
        Directory.Exists(request.WorkingDirectory.Info.FullName).Should().BeFalse();
    }

    [Theory]
    [InlineData("Go", "go")]
    [InlineData("PowerShell", "powershell")]
    public async Task OwnedProviders_ActualSourceContext_RejectWrongTargetEarlyCompletionAndUnboundPreview(string stack, string alias)
    {
        using var services = await InstallAsync(stack);
        var target = WorkingDirectory.FromExplicit(Path.Combine(_root, "qualified-context"));
        using var templater = ProjectTemplater(services, target, alias);
        ResolvedTemplate selected = (await templater.ResolveGroupAsync($"Azure.Functions.{stack}.Basic",
            TemplateType.Project, CancellationToken.None)).Group!.Single();
        var arguments = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(), []));
        var native = await selected.InvokeAsync(new(target, arguments.ValuesByCanonicalName, DryRun: true), CancellationToken.None);
        NativeProjectPreparationContext context = new(selected, target, arguments.ValuesByCanonicalName, native);
        INativeProjectPreparation provider = stack == "Go" ? new GoNativeProjectPreparation(_files, new HostGoTool())
            : new PowerShellNativeProjectPreparation(_files, new HostAzLookup("12"));

        var preview = await provider.PreviewAsync(context, CancellationToken.None);
        var early = await provider.CompleteAsync(context, preview, CancellationToken.None);
        var wrongTarget = await provider.PreviewAsync(context with
        {
            TargetDirectory = WorkingDirectory.FromExplicit(Path.Combine(_root, "different-root")),
        }, CancellationToken.None);
        var unbound = await provider.CompleteAsync(context, preview with { }, CancellationToken.None);
        Dictionary<string, string?> changed = new(context.Parameters) { ["NoBundles"] = "true" };
        var changedParameters = await provider.CompleteAsync(context with { Parameters = changed }, preview, CancellationToken.None);

        preview.Succeeded.Should().BeTrue();
        early.Succeeded.Should().BeFalse();
        wrongTarget.Succeeded.Should().BeFalse();
        unbound.Succeeded.Should().BeFalse();
        changedParameters.Succeeded.Should().BeFalse();
        Directory.Exists(target.Info.FullName).Should().BeFalse();
        INativeProjectWorkflowProvider workflow = stack == "Go" ? new GoProjectWorkflowProvider((GoNativeProjectPreparation)provider)
            : new PowerShellProjectWorkflowProvider((PowerShellNativeProjectPreparation)provider);
        workflow.Handles(selected.Entry).Should().BeTrue();
        workflow.Handles(selected.Entry with { Identity = "another-native-project" }).Should().BeFalse();
        workflow.Handles(selected.Entry with { Package = new("Other.Package", "1.0.0") }).Should().BeFalse();
        workflow.Handles(selected.Entry with { Type = TemplateType.Item }).Should().BeFalse();
    }

    [Theory]
    [InlineData("Go", "go", "manifestVersion")]
    [InlineData("Go", "go", "sourceHash")]
    [InlineData("PowerShell", "powershell", "manifestVersion")]
    [InlineData("PowerShell", "powershell", "sourceHash")]
    public async Task OwnedProviders_InvalidActualPackageProvenance_RefusesBeforeHelperOrNativeWrite(string stack, string alias, string corruption)
    {
        string archive = CorruptOwnedProjectProvenance(stack, alias, corruption);
        using var services = await InstallAsync(stack, archive: archive);
        HostGoTool go = new();
        HostAzLookup ps = new("12");
        INativeProjectWorkflowProvider provider = stack == "Go" ? new GoProjectWorkflowProvider(new(_files, go))
            : new PowerShellProjectWorkflowProvider(new(_files, ps));
        var request = new InitExecutionRequest(WorkingDirectory.FromExplicit(Path.Combine(_root, "rejected-provenance")),
            $"Azure.Functions.{stack}.Basic", "App", alias, stack, false, true, false, false, [], new HashSet<string>());

        var result = await ExecuteOwnedProjectAsync(services, provider, request, CancellationToken.None);

        result.Kind.Should().Be("preparationUnavailable");
        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Contains(corruption == "sourceHash" ? "SHA-256" : "version", StringComparison.Ordinal));
        go.Calls.Should().Be(0);
        ps.Lookups.Should().Be(0);
        Directory.Exists(request.WorkingDirectory.Info.FullName).Should().BeFalse();
    }

    [Fact]
    public async Task OwnedPowerShellProvider_AtomicCreateRace_RetainsNativeConfigAndUserRequirementsWithoutOverwrite()
    {
        using var services = await InstallAsync("PowerShell");
        string root = Path.Combine(_root, "powershell-prepared");
        var provider = new PowerShellProjectWorkflowProvider(new(
            new OccupiedRequirementsFileSystem(Path.Combine(root, "requirements.psd1")), new HostAzLookup("12")));
        var request = PowerShellPreparationRequest(provider.Preparation, enabled: true);

        var result = await ExecuteOwnedProjectAsync(services, provider, request, CancellationToken.None);

        result.Kind.Should().Be("partialPreparation");
        result.Preparation!.Completion!.Effects!.Kind.Should().Be("partialFailure");
        File.ReadAllText(Path.Combine(root, "requirements.psd1")).Should().Be("user content won the create race");
        File.Exists(Path.Combine(root, ".func", "config.json")).Should().BeTrue();
        Directory.GetFiles(root, "requirements.psd1.tmp.*").Should().BeEmpty();
    }

    private string CorruptOwnedProjectProvenance(string stack, string alias, string corruption)
    {
        string destination = Path.Combine(_root, "changed-project.nupkg");
        File.Copy(BuiltArchive(stack), destination);
        using var outer = ZipFile.Open(destination, ZipArchiveMode.Update);
        var nested = outer.GetEntry($"tools/any/templates/{alias}-basic.nupkg")!;
        using MemoryStream bytes = new();
        using (Stream input = nested.Open()) input.CopyTo(bytes);
        using (ZipArchive inner = new(bytes, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = inner.GetEntry($"provenance/{alias}/project.json")!;
            JsonNode manifest;
            using (StreamReader reader = new(entry.Open())) manifest = JsonNode.Parse(reader.ReadToEnd())!;
            if (corruption == "manifestVersion") manifest["packageVersion"] = "99.0.0";
            else manifest["sources"]![0]!["sha256"] = new string('0', 64);
            string path = entry.FullName;
            entry.Delete();
            using StreamWriter writer = new(inner.CreateEntry(path).Open());
            writer.Write(manifest.ToJsonString());
        }
        nested.Delete();
        using (Stream output = outer.CreateEntry($"tools/any/templates/{alias}-basic.nupkg").Open()) output.Write(bytes.ToArray());
        var declaration = outer.GetEntry("bundled-templates.json")!;
        JsonNode data;
        using (StreamReader reader = new(declaration.Open())) data = JsonNode.Parse(reader.ReadToEnd())!;
        data["assets"]![0]!["sha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray()));
        declaration.Delete();
        using (StreamWriter writer = new(outer.CreateEntry("bundled-templates.json").Open())) writer.Write(data.ToJsonString());
        return destination;
    }

    [Fact]
    public async Task OwnedProviders_Java_UsesSourceQualifiedNativeConfigurationWithoutPreparationOrMavenOperation()
    {
        using var services = await InstallAsync("Java");
        INativeProjectWorkflowProvider provider = new JavaProjectWorkflowProvider(_files);
        var request = new InitExecutionRequest(WorkingDirectory.FromExplicit(Path.Combine(_root, "java-owned")),
            "Azure.Functions.Java.Basic", "JavaApp", "java", "Java", false, true, false, true, [], new HashSet<string>());

        var result = await ExecuteOwnedProjectAsync(services, provider, request, CancellationToken.None);

        result.Kind.Should().Be("created");
        result.Preparation.Should().BeNull();
        provider.Preparation.Should().BeNull();
        File.ReadAllText(Path.Combine(request.WorkingDirectory.Info.FullName, "pom.xml")).Should().Contain("<artifactId>javaapp</artifactId>");
        File.Exists(Path.Combine(request.WorkingDirectory.Info.FullName, ".func", "config.json")).Should().BeTrue();
        Directory.Exists(Path.Combine(request.WorkingDirectory.Info.FullName, "target")).Should().BeFalse();
    }

    private async Task<InitExecutionResult> ExecuteOwnedProjectAsync(ServiceProvider installed,
        INativeProjectWorkflowProvider provider, InitExecutionRequest request, CancellationToken cancellationToken)
    {
        request = request with { ProjectPreparation = null };
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IFileSystem>(_files);
        services.AddSingleton<IInteractionService>(new TestInteractionService());
        services.AddSingleton<ILocalSettingsProvider>(Substitute.For<ILocalSettingsProvider>());
        services.AddSingleton(installed.GetRequiredService<IFunctionsProjectResolver>());
        foreach (IProjectStack stack in installed.GetServices<IProjectStack>()) services.AddSingleton(stack);
        services.AddSingleton<InstalledProjectStackCatalog>();
        services.AddSingleton<Func<InstalledProjectStackCatalog>>(serviceProvider => serviceProvider.GetRequiredService<InstalledProjectStackCatalog>);
        services.AddSingleton<ITemplateArgumentParser, TemplateArgumentParser>();
        services.AddSingleton<TemplateExecutionInteraction>();
        services.AddSingleton<InitExecutionRenderer>();
        services.AddSingleton<NewExecutionRenderer>();
        services.AddSingleton<InitExecutionTargetInspector>();
        services.AddSingleton<TemplateProjectConfigurationPlanner>();
        services.AddSingleton<TemplateProjectConfigurationWriter>();
        services.AddSingleton<ITemplateStore>(new TemplateStore(new(Home), _files, new FileTemplateStoreLock(), NullLoggerFactory.Instance));
        services.AddSingleton<ITemplaterFactory, TemplaterFactory>();
        services.AddScoped<INativeProjectWorkflowProvider>(_ => provider);
        var availability = Substitute.For<INativeTemplateAvailability>();
        availability.CaptureAsync(Arg.Any<CancellationToken>()).Returns(Snapshot(provider.Stack));
        services.AddSingleton(availability);
        services.AddNativeTemplateWorkflowComposition();
        services.AddSingleton(Substitute.For<INewExecutionRunner>());
        using ServiceProvider composed = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using IServiceScope scope = composed.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<NativeTemplateWorkflowComposer>()
            .ExecuteProjectAsync(request, cancellationToken);
    }

    private sealed class HostGoTool : HostGoPreparation
    {
        public int Calls { get; private set; }
        public (int ExitCode, string Stderr) Outcome { get; init; }
        public Action? DuringExecution { get; init; }
        public bool ThrowCancellation { get; init; }
        public Exception? Failure { get; init; }

        public override Task<(int ExitCode, string Stderr)> RunGoModTidyAsync(string workingDirectory, CancellationToken cancellationToken)
        {
            File.Exists(Path.Combine(workingDirectory, "go.mod")).Should().BeTrue();
            File.Exists(Path.Combine(workingDirectory, "main.go")).Should().BeTrue();
            File.Exists(Path.Combine(workingDirectory, ".func", "config.json")).Should().BeTrue();
            Calls++;
            if (Failure is not null) return Task.FromException<(int ExitCode, string Stderr)>(Failure);
            DuringExecution?.Invoke();
            if (ThrowCancellation) cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Outcome);
        }
    }

    private sealed class HostAzLookup(string? major) : HostPowerShellPreparation
    {
        public int Lookups { get; private set; }
        public int SourceReads { get; private set; }
        public Action? DuringLookup { get; init; }
        public Exception? Failure { get; init; }

        public override Task<string?> GetLatestAzModuleMajorVersionAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Lookups++;
            if (Failure is not null) return Task.FromException<string?>(Failure);
            DuringLookup?.Invoke();
            return Task.FromResult(major);
        }

        public override string ReadRequirementsTemplate()
        {
            SourceReads++;
            return base.ReadRequirementsTemplate();
        }
    }
}
