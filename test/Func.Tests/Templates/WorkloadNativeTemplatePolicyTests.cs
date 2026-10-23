// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Runtime.Loader;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Native;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    private const string PolicyRuntimeId = "Fixture.Policy.Runtime";
    private const string PolicyAssembly = "Azure.Functions.Cli.Workloads.Tests.Fixtures.NativePolicy.dll";

    [Theory]
    [InlineData(false, false, "valid")]
    [InlineData(false, true, "valid")]
    [InlineData(true, false, "valid")]
    [InlineData(true, true, "valid")]
    [InlineData(false, false, "no-defaults")]
    [InlineData(true, true, "no-defaults")]
    [InlineData(false, true, "replace-greeting")]
    [InlineData(true, true, "replace-greeting")]
    public async Task LoadedPolicy_ExplicitDefaultAndPromptValues_ComposeThroughActualCommands(bool item, bool prompt, string mode)
    {
        WorkloadEntry entry = await InstallPolicyFixtureAsync(composedParameters: true, promptRequired: prompt);
        string target = Path.Combine(_root, "composed-owner-values");
        var directory = WorkingDirectory.FromExplicit(target);
        var contexts = Substitute.For<INewExecutionContextResolver>();
        contexts.ResolveAsync(Arg.Any<NewExecutionRequest>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new NewExecutionContextResult(new(directory, new(directory, "fixture", "Fixture")), null));
        PolicyPromptInteraction interaction = new();
        using ServiceProvider provider = await CreatePolicyServicesAsync(contexts: contexts, interaction: interaction);
        using IServiceScope scope = provider.CreateScope();
        NativeTemplatePolicy policy = scope.ServiceProvider.GetRequiredService<WorkloadNativeTemplatePolicyRegistration>().Policy;
        policy.GetType().GetProperty("Mode")!.SetValue(policy, mode);
        var availability = new TemplateConstraintEvaluationSnapshot([new(entry.PackageId, NuGetVersion.Parse(entry.PackageVersion), entry.Aliases, true)]);
        if (item)
        {
            Directory.CreateDirectory(target);
        }
        int exit = item
            ? await new NewExecutionCommand(scope.ServiceProvider.GetRequiredService<INewExecutionRunner>(),
                provider.GetRequiredService<NewExecutionRenderer>(), availability)
                .ParseInvocation(["Fixture.Editor", "--path", target, "--name", "KeptName", "--Greeting", "hello", "--output", "json"]).InvokeAsync()
            : await new InitExecutionCommand(scope.ServiceProvider.GetRequiredService<IInitExecutionRunner>(),
                provider.GetRequiredService<InitExecutionRenderer>(), availability)
                .ParseInvocation([target, "--template", "Fixture.Project", "--name", "KeptName", "--Greeting", "hello", "--output", "json"]).InvokeAsync();

        exit.Should().Be(mode == "replace-greeting" ? 1 : 0, interaction.AllOutput);
        interaction.Prompts.Should().Be(prompt ? 1 : 0);
        if (mode == "replace-greeting")
        {
            if (item)
            {
                Directory.GetFileSystemEntries(target).Should().BeEmpty();
            }
            else
            {
                Directory.Exists(target).Should().BeFalse();
            }
            interaction.AllOutput.Should().Contain("explicit or undeclared");
            return;
        }
        NativeTemplateCapturedOutput capture = GetFixtureState<NativeTemplateCapturedOutput>(policy, "LastCapture");
        capture.Invocation.CanonicalValues["Greeting"].Should().Be("hello");
        capture.Invocation.CanonicalValues["Framework"].Should().Be(mode == "no-defaults" ? "net8.0" : "net10.0");
        capture.Invocation.CanonicalValues["Prompted"].Should().Be(prompt ? "host-prompt-value" : "authored-prompt");
        capture.Invocation.ExplicitParameterNames.Should().BeEquivalentTo(["Greeting"]);
        capture.Invocation.Name.Should().Be("KeptName");
        if (!item)
        {
            File.ReadAllText(Path.Combine(target, "KeptName.proj"))
                .Should().Be($"KeptName|{capture.Invocation.CanonicalValues["Framework"]}|hello|{capture.Invocation.CanonicalValues["Prompted"]}");
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task LoadedPolicy_RawConfigurationFingerprint_PreservesBomAndRefusesChangedRawSource(bool bom, bool drift)
    {
        WorkloadEntry entry = await InstallPolicyFixtureAsync(utf8Bom: bom);
        using ServiceProvider provider = await CreatePolicyServicesAsync();
        using IServiceScope scope = provider.CreateScope();
        string target = Path.Combine(_root, "raw-owner-project");
        var availability = new TemplateConstraintEvaluationSnapshot([new(entry.PackageId, NuGetVersion.Parse(entry.PackageVersion), entry.Aliases, true)]);
        using Templater templater = await scope.ServiceProvider.GetRequiredService<ITemplaterFactory>().CreateProjectAsync(
            new(WorkingDirectory.FromExplicit(target)), availability, provider.GetRequiredService<TemplateProjectSupport>(), CancellationToken.None);
        IReadOnlyList<TemplateCatalogEntry> catalog = await templater.ListAsync(TemplateType.Project);
        NativeTemplatePolicy policy = scope.ServiceProvider.GetRequiredService<WorkloadNativeTemplatePolicyRegistration>().Policy;
        GetFixtureState<NativeTemplateSource>(policy, "LastSource").Identity.Should().Be("Fixture.Project");
        catalog.Should().ContainSingle().Which.ProjectConfigurationDiagnostic.Should().BeNull("unchanged raw bytes must retain their original qualification");
        catalog.Should().ContainSingle().Which.ProjectConfigurations.Should().ContainSingle("the original raw bytes must admit the owned declaration");
        var resolution = await templater.ResolveGroupAsync("Fixture.Project", TemplateType.Project);
        resolution.Status.Should().Be(TemplateResolutionStatus.Success);
        ResolvedTemplate selected = resolution.Group![0];
        ReadOnlyMemory<byte> original = await selected.ReadPackageFileAsync("content/project/.template.config/template.json", CancellationToken.None);
        string.Equals(selected.ConfigurationSha256, Convert.ToHexString(SHA256.HashData(original.Span)), StringComparison.OrdinalIgnoreCase)
            .Should().BeTrue("the fingerprint must identify the original package bytes");
        if (bom)
        {
            original.Span[..3].SequenceEqual(new UTF8Encoding(true).GetPreamble()).Should().BeTrue();
        }
        NativeProjectLayoutQualification wrongPin = new(selected.Entry.Identity, new('A', 64), new(0, "fixture", "Fixture"));
        TemplateProjectConfigurationPlanner planner = provider.GetRequiredService<TemplateProjectSupport>().Planner;
        Action mismatched = () => planner.ReadOwnedLayout(selected.Configuration!, wrongPin);
        mismatched.Should().Throw<InvalidTemplateMetadataException>().WithMessage("*does not match its source qualification*");
        Action missingSnapshot = () => planner.ReadOwnedLayout(null!, wrongPin);
        missingSnapshot.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
        Action missingDeclaration = () => planner.ReadOwnedLayout(selected.Configuration!, null!);
        missingDeclaration.Should().Throw<ArgumentNullException>().WithParameterName("qualification");
        ResolvedTemplate foreign = (await templater.ResolveGroupAsync("Fixture.Editor", TemplateType.Item)).Group![0];
        NativeProjectLayoutQualification actualPin = new(selected.Entry.Identity, selected.ConfigurationSha256!, new(0, "fixture", "Fixture"));
        Action swappedSource = () => planner.ReadOwnedLayout(foreign.Configuration!, actualPin);
        swappedSource.Should().Throw<InvalidTemplateMetadataException>().WithMessage("*does not match its source qualification*");
        if (drift)
        {
            using var archive = ZipFile.Open(selected.PackageMountPoint!, ZipArchiveMode.Update);
            var configuration = archive.GetEntry("content/project/.template.config/template.json")!;
            using MemoryStream raw = new();
            using (Stream input = configuration.Open())
            {
                input.CopyTo(raw);
            }
            configuration.Delete();
            using Stream changed = archive.CreateEntry("content/project/.template.config/template.json").Open();
            changed.Write([.. raw.ToArray(), (byte)' ']);
        }

        int exit = await new InitExecutionCommand(scope.ServiceProvider.GetRequiredService<IInitExecutionRunner>(),
            provider.GetRequiredService<InitExecutionRenderer>(), availability)
            .ParseInvocation([target, "--template", "Fixture.Project", "--name", "Created", "--non-interactive", "--output", "json"]).InvokeAsync();

        exit.Should().Be(drift ? 1 : 0, _interaction.AllOutput);
        Directory.Exists(target).Should().Be(!drift);
        if (!drift)
        {
            File.ReadAllText(Path.Combine(target, "Created.proj")).Should().Be("Created|net10.0");
        }
    }

    [Fact]
    public async Task LoadedPolicy_SourceDeclaration_ReachesRealCatalogResolveAndInitWithAlternativeLayout()
    {
        WorkloadEntry entry = await InstallPolicyFixtureAsync();
        using ServiceProvider provider = await CreatePolicyServicesAsync();
        using IServiceScope scope = provider.CreateScope();
        WorkloadNativeTemplatePolicyRegistration registration = scope.ServiceProvider.GetRequiredService<WorkloadNativeTemplatePolicyRegistration>();
        var stack = provider.GetRequiredService<Azure.Functions.Cli.Projects.InstalledProjectStackCatalog>().FindStack("fixture")!;
        AssemblyLoadContext.GetLoadContext(stack.GetType().Assembly).Should().BeSameAs(registration.Owner.LoadContext);
        AssemblyLoadContext.GetLoadContext(registration.Policy.GetType().Assembly).Should().BeSameAs(registration.Owner.LoadContext);
        File.Exists(Path.Combine(Home, "templates", ".bundled-owners", "fixture.policy.templates", "2.0.0.json")).Should().BeTrue();
        var availability = new TemplateConstraintEvaluationSnapshot([new(entry.PackageId, NuGetVersion.Parse(entry.PackageVersion), entry.Aliases, true)]);
        ITemplaterFactory factory = scope.ServiceProvider.GetRequiredService<ITemplaterFactory>();
        using Templater templater = await factory.CreateProjectAsync(
            new(WorkingDirectory.FromExplicit(_root)), availability, provider.GetRequiredService<TemplateProjectSupport>(), CancellationToken.None);

        var catalog = await templater.ListAsync(TemplateType.Project);
        var resolved = await templater.ResolveGroupAsync("Fixture.Project", TemplateType.Project);

        registration.Policy.GetType().GetProperty("LastSource")!.GetValue(registration.Policy).Should().NotBeNull("the actual owner policy must participate in catalog admission");
        catalog.Should().ContainSingle();
        catalog[0].ProjectConfigurations.Should().NotBeNull(JsonSerializer.Serialize(catalog[0]));
        catalog[0].ProjectConfigurations.Should().ContainSingle().Which.Stack.Should().Be("fixture");
        resolved.Status.Should().Be(TemplateResolutionStatus.Success);
        string target = Path.Combine(_root, "created-owner-project");
        InitExecutionCommand command = new(scope.ServiceProvider.GetRequiredService<IInitExecutionRunner>(),
            provider.GetRequiredService<InitExecutionRenderer>(), availability);
        int exit = await command.ParseInvocation([target, "--template", "Fixture.Project", "--stack", "fixture",
            "--language", "fx", "--name", "Created", "--non-interactive", "--output", "json"]).InvokeAsync();

        exit.Should().Be(0, _interaction.AllOutput);
        File.ReadAllText(Path.Combine(target, "Created.proj")).Should().Be("Created|net10.0");
        File.ReadAllText(Path.Combine(target, ".ownerignore")).Should().Be("owner-ignore");
        using var configuration = JsonDocument.Parse(File.ReadAllText(Path.Combine(target, ".func", "config.json")));
        configuration.RootElement.GetProperty("stack").GetProperty("runtime").GetString().Should().Be("fixture");
        registration.Owner.PackageVersion.Should().Be("1.0.0");
        AssemblyLoadContext.GetLoadContext(registration.Policy.GetType().Assembly).Should().BeSameAs(registration.Owner.LoadContext);
        registration.Policy.GetType().Assembly.GetReferencedAssemblies().Should().NotContain(assembly => assembly.Name == "func");
        NativeTemplateCapturedOutput captured = GetFixtureState<NativeTemplateCapturedOutput>(registration.Policy, "LastCapture");
        captured.Invocation.CanonicalValues["Framework"].Should().Be("net10.0");
        Func<Task> expired = () => captured.Invocation.Source.RevalidateAsync(CancellationToken.None);
        await expired.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Theory]
    [InlineData("missing-declaration")]
    [InlineData("wrong-index")]
    [InlineData("foreign-stack")]
    [InlineData("unknown-stage")]
    [InlineData("refused")]
    [InlineData("unhandled")]
    public async Task LoadedPolicy_InvalidSourceDeclaration_RefusesBeforeSelectionOrOutput(string mode)
    {
        WorkloadEntry entry = await InstallPolicyFixtureAsync();
        using ServiceProvider provider = await CreatePolicyServicesAsync(additionalStack: mode == "foreign-stack" ? new ForeignPolicyStack() : null);
        using IServiceScope scope = provider.CreateScope();
        NativeTemplatePolicy policy = scope.ServiceProvider.GetRequiredService<WorkloadNativeTemplatePolicyRegistration>().Policy;
        policy.GetType().GetProperty("Mode")!.SetValue(policy, mode);
        string target = Path.Combine(_root, "rejected-owner-project");
        InitExecutionCommand command = new(scope.ServiceProvider.GetRequiredService<IInitExecutionRunner>(),
            provider.GetRequiredService<InitExecutionRenderer>(), new([new(entry.PackageId, NuGetVersion.Parse(entry.PackageVersion), entry.Aliases, true)]));

        int exit = await command.ParseInvocation([target, "--template", "Fixture.Project", "--non-interactive", "--output", "json"]).InvokeAsync();

        exit.Should().Be(1, _interaction.AllOutput);
        GetFixtureState<NativeTemplateSource>(policy, "LastSource").Identity.Should().Be("Fixture.Project");
        if (mode == "foreign-stack")
        {
            provider.GetRequiredService<InstalledProjectStackCatalog>().FindStack("foreign").Should().NotBeNull();
            _interaction.AllOutput.Should().Contain("actual admitted runtime stack");
        }
        if (mode == "unhandled")
        {
            _interaction.AllOutput.Should().Contain("does not handle this required native content");
        }
        Directory.Exists(target).Should().BeFalse();
        _interaction.AllOutput.Should().NotContain("\"succeeded\":true");
    }

    [Fact]
    public async Task LoadedPolicy_UnhandledOrdinaryGenericProject_KeepsItsExistingMetadataRoute()
    {
        WorkloadEntry fixture = await InstallPolicyFixtureAsync();
        WorkloadEntry node = (await _installer.InstallFromPackageAsync(BuiltArchive)).Entry;
        using ServiceProvider provider = await CreatePolicyServicesAsync();
        using IServiceScope scope = provider.CreateScope();
        NativeTemplatePolicy policy = scope.ServiceProvider.GetRequiredService<WorkloadNativeTemplatePolicyRegistration>().Policy;
        policy.GetType().GetProperty("Mode")!.SetValue(policy, "unhandled");
        string target = Path.Combine(_root, "ordinary-node-project");
        InitExecutionCommand command = new(scope.ServiceProvider.GetRequiredService<IInitExecutionRunner>(),
            provider.GetRequiredService<InitExecutionRenderer>(), new(
                [new(fixture.PackageId, NuGetVersion.Parse(fixture.PackageVersion), fixture.Aliases, true),
                 new(node.PackageId, NuGetVersion.Parse(node.PackageVersion), node.Aliases, true)]));

        int exit = await command.ParseInvocation([target, "--template", "Azure.Functions.Node.Basic.JavaScript",
            "--name", "Ordinary", "--non-interactive", "--output", "json"]).InvokeAsync();

        exit.Should().Be(0, _interaction.AllOutput);
        File.Exists(Path.Combine(target, "host.json")).Should().BeTrue();
        using var configuration = JsonDocument.Parse(File.ReadAllText(Path.Combine(target, ".func", "config.json")));
        configuration.RootElement.GetProperty("stack").GetProperty("runtime").GetString().Should().Be("node");
        policy.GetType().GetProperty("LastCapture")!.GetValue(policy).Should().BeNull();
    }

    [Fact]
    public async Task LoadedPolicy_CommandScopes_DoNotCaptureSingletonRunnerOrPolicy()
    {
        WorkloadEntry entry = await InstallPolicyFixtureAsync();
        var directory = WorkingDirectory.FromExplicit(Directory.CreateDirectory(Path.Combine(_root, "scoped-new-commands")).FullName);
        var contexts = Substitute.For<INewExecutionContextResolver>();
        contexts.ResolveAsync(Arg.Any<NewExecutionRequest>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new NewExecutionContextResult(new(directory, new(directory, "fixture", "Fixture")), null));
        using ServiceProvider provider = await CreatePolicyServicesAsync(contexts: contexts);
        using IServiceScope first = provider.CreateScope();
        using IServiceScope second = provider.CreateScope();

        var firstRegistration = first.ServiceProvider.GetRequiredService<WorkloadNativeTemplatePolicyRegistration>();
        var secondRegistration = second.ServiceProvider.GetRequiredService<WorkloadNativeTemplatePolicyRegistration>();
        firstRegistration.Policy.Should().NotBeSameAs(secondRegistration.Policy);
        first.ServiceProvider.GetRequiredService<INewExecutionRunner>().Should()
            .NotBeSameAs(second.ServiceProvider.GetRequiredService<INewExecutionRunner>());
        Action root = () => provider.GetRequiredService<INewExecutionRunner>();
        root.Should().Throw<InvalidOperationException>();
        var availability = new TemplateConstraintEvaluationSnapshot([new(entry.PackageId, NuGetVersion.Parse(entry.PackageVersion), entry.Aliases, true)]);
        NewExecutionCommand firstCommand = new(first.ServiceProvider.GetRequiredService<INewExecutionRunner>(),
            provider.GetRequiredService<NewExecutionRenderer>(), availability);
        NewExecutionCommand secondCommand = new(second.ServiceProvider.GetRequiredService<INewExecutionRunner>(),
            provider.GetRequiredService<NewExecutionRenderer>(), availability);
        string[] tokens = ["Fixture.Editor", "--path", directory.Info.FullName, "--name", "KeptName",
            "--non-interactive", "--dry-run", "--force", "--output", "json"];

        (await firstCommand.ParseInvocation(tokens).InvokeAsync()).Should().Be(0, _interaction.AllOutput);
        (await secondCommand.ParseInvocation(tokens).InvokeAsync()).Should().Be(0, _interaction.AllOutput);

        NativeTemplateCapturedOutput firstCapture = GetFixtureState<NativeTemplateCapturedOutput>(firstRegistration.Policy, "LastCapture");
        NativeTemplateCapturedOutput secondCapture = GetFixtureState<NativeTemplateCapturedOutput>(secondRegistration.Policy, "LastCapture");
        secondCapture.Invocation.Should().NotBeSameAs(firstCapture.Invocation);
        secondCapture.Invocation.Source.Should().NotBeSameAs(firstCapture.Invocation.Source);
        secondCapture.Invocation.Name.Should().Be("KeptName");
        secondCapture.Invocation.DryRun.Should().BeTrue();
        secondCapture.Invocation.Force.Should().BeTrue();
        Func<Task> expired = () => firstCapture.Invocation.Source.RevalidateAsync(CancellationToken.None);
        await expired.Should().ThrowAsync<ObjectDisposedException>();
        Directory.GetFileSystemEntries(directory.Info.FullName).Should().BeEmpty();
    }

    [Theory]
    [InlineData("valid", "net8.0", false)]
    [InlineData("valid", "net10.0", true)]
    [InlineData("explicit-overwrite", "net10.0", false)]
    [InlineData("no-stages", "net10.0", true)]
    public async Task LoadedPolicy_InvocationValues_KeepExplicitFrameworkAndHostRequiredQualification(
        string mode, string framework, bool success)
    {
        WorkloadEntry entry = await InstallPolicyFixtureAsync();
        using ServiceProvider provider = await CreatePolicyServicesAsync();
        using IServiceScope scope = provider.CreateScope();
        NativeTemplatePolicy policy = scope.ServiceProvider.GetRequiredService<WorkloadNativeTemplatePolicyRegistration>().Policy;
        policy.GetType().GetProperty("Mode")!.SetValue(policy, mode);
        string target = Path.Combine(_root, "explicit-owner-project");
        InitExecutionCommand command = new(scope.ServiceProvider.GetRequiredService<IInitExecutionRunner>(),
            provider.GetRequiredService<InitExecutionRenderer>(), new([new(entry.PackageId, NuGetVersion.Parse(entry.PackageVersion), entry.Aliases, true)]));

        int exit = await command.ParseInvocation([target, "--template", "Fixture.Project", "--Framework", framework,
            "--non-interactive", "--output", "json"]).InvokeAsync();

        exit.Should().Be(success ? 0 : 1, _interaction.AllOutput);
        Directory.Exists(target).Should().Be(success);
        if (success)
        {
            var captured = GetFixtureState<NativeTemplateCapturedOutput>(policy, "LastCapture");
            captured.Invocation.CanonicalValues["Framework"].Should().Be(framework);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadedPolicy_QualifiedNonAppendWithoutBoundExecutor_RefusesBeforeNativeInstall(bool suppliedUnownedCapability)
    {
        WorkloadEntry entry = await InstallPolicyFixtureAsync();
        using ServiceProvider provider = await CreatePolicyServicesAsync();
        using IServiceScope scope = provider.CreateScope();
        string target = WriteCompileTarget();
        using Templater templater = await OpenPolicyItemAsync(scope, entry, target);
        ResolvedTemplate selected = (await templater.ResolveGroupAsync("Fixture.Compile", TemplateType.Item)).Group![0];
        var capability = Substitute.For<INativeTemplateEffectCapability>();
        capability.ValidateAsync(Arg.Any<NativeTemplateEffectPlan>(), Arg.Any<CancellationToken>()).Returns([]);
        TemplateInvocationRequest request = new(WorkingDirectory.FromExplicit(target), new Dictionary<string, string?>(), "Handler")
        {
            EffectSupport = suppliedUnownedCapability
                ? new(WorkingDirectory.FromExplicit(target), Path.Combine(target, "Fixture.fsproj"), capability, true) : null,
        };

        TemplateInvocationResult result = await selected.InvokeAsync(request);

        result.Status.Should().Be(TemplateInvocationStatus.EffectFailure, result.Diagnostic);
        result.HasCommittedOutput.Should().BeFalse();
        result.Diagnostic.Should().Contain("no bound executor");
        File.Exists(Path.Combine(target, "Handler.fs")).Should().BeFalse();
        File.ReadAllText(Path.Combine(target, "Fixture.fsproj")).Should().Contain("Bootstrap.fs").And.NotContain("Handler.fs");
        capability.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("valid", true)]
    [InlineData("bad-generated-hash", false)]
    [InlineData("bad-original-hash", false)]
    [InlineData("foreign-source", false)]
    [InlineData("missing-repair", false)]
    [InlineData("no-stages", true)]
    [InlineData("missing-invocation", false)]
    [InlineData("invocation-diagnostics", false)]
    [InlineData("missing-capture", false)]
    [InlineData("capture-diagnostics", false)]
    public async Task LoadedPolicy_CapturedCompileData_JoinsActualHashesAndUsesOwnerTerminalAnchor(string mode, bool success)
    {
        WorkloadEntry entry = await InstallPolicyFixtureAsync();
        using ServiceProvider provider = await CreatePolicyServicesAsync();
        using IServiceScope scope = provider.CreateScope();
        WorkloadNativeTemplatePolicyRegistration registration = scope.ServiceProvider.GetRequiredService<WorkloadNativeTemplatePolicyRegistration>();
        registration.Policy.GetType().GetProperty("Mode")!.SetValue(registration.Policy, mode);
        string target = WriteCompileTarget();
        using Templater templater = await OpenPolicyItemAsync(scope, entry, target);
        ResolvedTemplate selected = (await templater.ResolveGroupAsync("Fixture.Compile", TemplateType.Item)).Group![0];
        NativeTemplatePolicyCandidate candidate = (await scope.ServiceProvider.GetRequiredService<WorkloadNativeTemplatePolicies>()
            .GetCandidateAsync(selected.Entry, selected.Configuration!, selected.ConfigurationSourceIdentity!, selected.PackageMountPoint,
                selected.ReadPackageFileAsync, selected.ValidateConfigurationAsync, false, CancellationToken.None, selected.InvocationOwner))!;
        TerminalCompileExecutor executor = new(_files);

        TemplateInvocationResult result = await selected.InvokeAsync(new(WorkingDirectory.FromExplicit(target), new Dictionary<string, string?>(), "Handler")
        {
            OwnerExecutor = candidate.Admission.BindExecutor(registration, executor, true),
        });

        result.Status.Should().Be(success ? TemplateInvocationStatus.Success : TemplateInvocationStatus.InvalidTemplate, result.Diagnostic);
        result.HasCommittedOutput.Should().Be(success);
        executor.Executions.Should().Be(success ? 1 : 0);
        File.Exists(Path.Combine(target, "Handler.fs")).Should().Be(success);
        var document = XDocument.Load(Path.Combine(target, "Fixture.fsproj"));
        document.Descendants("Compile").Select(element => (string?)element.Attribute("Include"))
            .Should().Equal(success ? ["Handler.fs", "Bootstrap.fs"] : ["Bootstrap.fs"]);
    }

    [Theory]
    [InlineData("owner.settings", "source-qualified content", true)]
    [InlineData("requirements.psd1", "source-qualified content", false)]
    [InlineData("owner.settings", "unqualified content", false)]
    public async Task LoadedPolicy_CreateOnlyContentPolicy_IsOwnedNotCoreAndDoesNotGrantExecution(string name, string content, bool qualified)
    {
        WorkloadEntry entry = await InstallPolicyFixtureAsync();
        using ServiceProvider provider = await CreatePolicyServicesAsync();
        using IServiceScope scope = provider.CreateScope();
        var target = WorkingDirectory.FromExplicit(Path.Combine(_root, "qualified-create-only"));
        using Templater templater = await scope.ServiceProvider.GetRequiredService<ITemplaterFactory>().CreateProjectAsync(
            new(target), new([new(entry.PackageId, NuGetVersion.Parse(entry.PackageVersion), entry.Aliases, true)]),
            provider.GetRequiredService<TemplateProjectSupport>(), CancellationToken.None);
        ResolvedTemplate selected = (await templater.ResolveGroupAsync("Fixture.Project", TemplateType.Project)).Group![0];
        NativeTemplatePolicyCandidate candidate = (await scope.ServiceProvider.GetRequiredService<WorkloadNativeTemplatePolicies>()
            .GetCandidateAsync(selected.Entry, selected.Configuration!, selected.ConfigurationSourceIdentity!, selected.PackageMountPoint,
                selected.ReadPackageFileAsync, selected.ValidateConfigurationAsync, true, CancellationToken.None, selected.InvocationOwner))!;
        using NativeTemplatePolicySession session = new(candidate, target, new TemplateArgumentParser());
        TemplateInvocationRequest request = await session.PrepareAsync(new(target,
            new Dictionary<string, string?> { ["Framework"] = "net10.0" }, "Created")
        {
            ExplicitParameterNames = new HashSet<string> { "Framework" },
        }, CancellationToken.None);
        NativeTemplatePreparationResult capture = await selected.CaptureNativePreparationAsync(request, true, CancellationToken.None);
        capture.Failure.Should().BeNull();
        await session.QualifyCaptureAsync(capture.Snapshot!, CancellationToken.None);
        TemplateCreateFileEffect effect = new(new(selected.Entry.Identity, selected.ConfigurationSourceIdentity!, selected.ConfigurationSha256!),
            Path.Combine(target.Info.FullName, name), content);
        NativeTemplateEffectPlan plan = new NativeTemplateEffectPlanner().PlanFilesAndDependencies(target, [effect]).Plan!;

        Exception? refusal = await Record.ExceptionAsync(() => session.QualifyEffectsAsync(plan, CancellationToken.None));

        if (qualified)
        {
            refusal.Should().BeNull();
            Action missingExecutor = () => session.BindExistingExecutor(request, target, Path.Combine(target.Info.FullName, name));
            missingExecutor.Should().Throw<InvalidDataException>().WithMessage("*no bound executor*");
        }
        else
        {
            refusal.Should().BeOfType<InvalidDataException>().Which.Message.Should().Contain("refused");
        }
        Directory.Exists(target.Info.FullName).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadedPolicy_ExecutorAdmission_IsBoundToOneActualInvocation(bool freshExecutorBinding)
    {
        WorkloadEntry entry = await InstallPolicyFixtureAsync();
        using ServiceProvider provider = await CreatePolicyServicesAsync();
        using IServiceScope scope = provider.CreateScope();
        WorkloadNativeTemplatePolicyRegistration registration = scope.ServiceProvider.GetRequiredService<WorkloadNativeTemplatePolicyRegistration>();
        string target = WriteCompileTarget();
        using Templater templater = await OpenPolicyItemAsync(scope, entry, target);
        ResolvedTemplate selected = (await templater.ResolveGroupAsync("Fixture.Compile", TemplateType.Item)).Group![0];
        NativeTemplatePolicyCandidate candidate = (await scope.ServiceProvider.GetRequiredService<WorkloadNativeTemplatePolicies>()
            .GetCandidateAsync(selected.Entry, selected.Configuration!, selected.ConfigurationSourceIdentity!, selected.PackageMountPoint,
                selected.ReadPackageFileAsync, selected.ValidateConfigurationAsync, false, CancellationToken.None, selected.InvocationOwner))!;
        TerminalCompileExecutor executor = new(_files);
        BoundNativeTemplateExecutor admission = candidate.Admission.BindExecutor(registration, executor, true);
        TemplateInvocationRequest preview = new(WorkingDirectory.FromExplicit(target), new Dictionary<string, string?>(), "Handler", DryRun: true)
        {
            OwnerExecutor = admission,
        };
        TemplateInvocationResult previewResult = await selected.InvokeAsync(preview);
        previewResult.Status.Should().Be(TemplateInvocationStatus.Success, previewResult.Diagnostic);
        previewResult.HasCommittedOutput.Should().BeFalse();
        executor.Executions.Should().Be(0);

        TemplateInvocationResult result = await selected.InvokeAsync(preview with
        {
            DryRun = false,
            OwnerExecutor = freshExecutorBinding ? candidate.Admission.BindExecutor(registration, executor, true) : admission,
        });

        result.Status.Should().Be(freshExecutorBinding ? TemplateInvocationStatus.Success : TemplateInvocationStatus.EffectFailure, result.Diagnostic);
        result.HasCommittedOutput.Should().Be(freshExecutorBinding);
        executor.Executions.Should().Be(freshExecutorBinding ? 1 : 0);
        File.Exists(Path.Combine(target, "Handler.fs")).Should().Be(freshExecutorBinding);
        if (!freshExecutorBinding)
        {
            result.Diagnostic.Should().Contain("another invocation");
            File.ReadAllText(Path.Combine(target, "Fixture.fsproj")).Should().NotContain("Handler.fs");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadedPolicy_AppendBridge_UsesActualWorkloadCapabilityAndRefusesSourceDrift(bool mutateSource)
    {
        WorkloadEntry entry = await InstallPolicyFixtureAsync();
        using ServiceProvider provider = await CreatePolicyServicesAsync();
        using IServiceScope scope = provider.CreateScope();
        string target = Directory.CreateDirectory(Path.Combine(_root, "owned-append")).FullName;
        string file = Path.Combine(target, "function_app.txt");
        File.WriteAllText(file, "user-owned");
        using Templater templater = await OpenPolicyItemAsync(scope, entry, target);
        ResolvedTemplate selected = (await templater.ResolveGroupAsync("Fixture.Item", TemplateType.Item)).Group![0];
        WorkloadNativeTemplatePolicyAdapter adapter = (await scope.ServiceProvider.GetRequiredService<WorkloadNativeTemplatePolicies>()
            .FindAsync(selected, CancellationToken.None))!;
        NativeSelectedItemBinding binding = await adapter.ResolveAsync(selected, CancellationToken.None);
        binding.Diagnostics.Should().BeEmpty();
        if (mutateSource)
        {
            await File.AppendAllTextAsync(selected.PackageMountPoint!, "changed");
        }

        TemplateInvocationResult? result = await adapter.InvokeAsync(templater, selected, binding,
            new(WorkingDirectory.FromExplicit(target), new Dictionary<string, string?>(), "Named") { PolicyEffectsConfirmed = true },
            CancellationToken.None);

        result.Should().NotBeNull();
        result!.Status.Should().Be(mutateSource ? TemplateInvocationStatus.EffectFailure : TemplateInvocationStatus.Success, result.Diagnostic);
        result.HasCommittedOutput.Should().BeFalse();
        result.Changes.Should().BeEmpty();
        File.ReadAllText(file).Should().Be(mutateSource ? "user-owned" : "user-owned\nNamed");
        if (!mutateSource)
        {
            result.Effects!.CompletedEffects.Should().ContainSingle().Which.Should().BeOfType<TemplateAppendFileEffect>();
        }
    }

    [Theory]
    [InlineData("runtime-source")]
    [InlineData("runtime-payload")]
    [InlineData("native-owner")]
    [InlineData("declaration")]
    public async Task LoadedPolicy_AdmissionDriftAfterSelection_RefusesBeforeNativeCreation(string changed)
    {
        WorkloadEntry entry = await InstallPolicyFixtureAsync();
        using ServiceProvider provider = await CreatePolicyServicesAsync();
        using IServiceScope scope = provider.CreateScope();
        var target = WorkingDirectory.FromExplicit(Path.Combine(_root, "drifted-owner-project"));
        using Templater templater = await scope.ServiceProvider.GetRequiredService<ITemplaterFactory>().CreateProjectAsync(
            new(target), new([new(entry.PackageId, NuGetVersion.Parse(entry.PackageVersion), entry.Aliases, true)]),
            provider.GetRequiredService<TemplateProjectSupport>(), CancellationToken.None);
        ResolvedTemplate selected = (await templater.ResolveGroupAsync("Fixture.Project", TemplateType.Project)).Group![0];
        if (changed == "runtime-source")
        {
            var row = JsonSerializer.SerializeToNode(entry, WorkloadJsonContext.Default.WorkloadEntry)!;
            row["source"] = "https://foreign.invalid/v3/index.json";
            await _workloads.SaveWorkloadAsync(JsonSerializer.Deserialize(row, WorkloadJsonContext.Default.WorkloadEntry)!);
        }
        if (changed == "runtime-payload")
        {
            File.AppendAllText(Path.Combine(_paths.GetInstallDirectory(entry.PackageId, entry.PackageVersion), "workload.json"), "\n ");
        }
        if (changed == "native-owner")
        {
            string path = Path.Combine(Home, "templates", ".bundled-owners", "fixture.policy.templates", "2.0.0.json");
            var receipt = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
            receipt["sha256"] = new string('A', 64);
            File.WriteAllText(path, receipt.ToJsonString());
        }
        if (changed == "declaration")
        {
            NativeTemplatePolicy policy = scope.ServiceProvider.GetRequiredService<WorkloadNativeTemplatePolicyRegistration>().Policy;
            policy.GetType().GetProperty("Mode")!.SetValue(policy, "wrong-index");
        }

        TemplateInvocationResult result = await selected.InvokeAsync(new(target,
            new Dictionary<string, string?> { ["Framework"] = "net10.0" }, "Created")
        {
            ExplicitParameterNames = new HashSet<string> { "Framework" },
        });

        result.Status.Should().NotBe(TemplateInvocationStatus.Success);
        result.HasCommittedOutput.Should().BeFalse();
        Directory.Exists(target.Info.FullName).Should().BeFalse();
    }

    [Fact]
    public async Task LoadedPolicy_PreviewAndCreate_HaveFreshExpiredViewsAndCopiedFinalDefaults()
    {
        WorkloadEntry entry = await InstallPolicyFixtureAsync();
        using ServiceProvider provider = await CreatePolicyServicesAsync();
        using IServiceScope scope = provider.CreateScope();
        NativeTemplatePolicy policy = scope.ServiceProvider.GetRequiredService<WorkloadNativeTemplatePolicyRegistration>().Policy;
        string target = Path.Combine(_root, "separate-invocations");
        InitExecutionCommand command = new(scope.ServiceProvider.GetRequiredService<IInitExecutionRunner>(),
            provider.GetRequiredService<InitExecutionRenderer>(), new([new(entry.PackageId, NuGetVersion.Parse(entry.PackageVersion), entry.Aliases, true)]));
        string[] tokens = [target, "--template", "Fixture.Project", "--name", "Created", "--non-interactive", "--output", "json"];

        (await command.ParseInvocation([.. tokens, "--dry-run"]).InvokeAsync()).Should().Be(0, _interaction.AllOutput);
        NativeTemplateCapturedOutput preview = GetFixtureState<NativeTemplateCapturedOutput>(policy, "LastCapture");
        var defaults = GetFixtureState<Dictionary<string, string?>>(policy, "ReturnedDefaults");
        defaults["Framework"] = "changed-after-return";
        preview.Invocation.CanonicalValues["Framework"].Should().Be("net10.0");
        Func<Task> expiredSource = () => preview.Invocation.Source.ReadPackageFileAsync("content/layout.json", CancellationToken.None);
        Func<Task> expiredImage = () => preview.ReadFileAsync(preview.Files[0].TargetRelativePath, CancellationToken.None);
        await expiredSource.Should().ThrowAsync<ObjectDisposedException>();
        await expiredImage.Should().ThrowAsync<ObjectDisposedException>();
        Directory.Exists(target).Should().BeFalse();

        (await command.ParseInvocation(tokens).InvokeAsync()).Should().Be(0, _interaction.AllOutput);

        NativeTemplateCapturedOutput created = GetFixtureState<NativeTemplateCapturedOutput>(policy, "LastCapture");
        created.Invocation.Should().NotBeSameAs(preview.Invocation);
        created.Invocation.Source.Should().NotBeSameAs(preview.Invocation.Source);
        created.Invocation.CanonicalValues["Framework"].Should().Be("net10.0");
        File.ReadAllText(Path.Combine(target, "Created.proj")).Should().Be("Created|net10.0");
    }

    [Fact]
    public async Task LoadedPolicy_ExecutorFromAnotherCommandRegistration_CannotAcquireAdmissionAuthority()
    {
        WorkloadEntry entry = await InstallPolicyFixtureAsync();
        using ServiceProvider provider = await CreatePolicyServicesAsync();
        using IServiceScope first = provider.CreateScope();
        using IServiceScope second = provider.CreateScope();
        string target = WriteCompileTarget();
        using Templater templater = await OpenPolicyItemAsync(first, entry, target);
        ResolvedTemplate selected = (await templater.ResolveGroupAsync("Fixture.Compile", TemplateType.Item)).Group![0];
        NativeTemplatePolicyCandidate candidate = (await first.ServiceProvider.GetRequiredService<WorkloadNativeTemplatePolicies>()
            .GetCandidateAsync(selected.Entry, selected.Configuration!, selected.ConfigurationSourceIdentity!, selected.PackageMountPoint,
                selected.ReadPackageFileAsync, selected.ValidateConfigurationAsync, false, CancellationToken.None, selected.InvocationOwner))!;
        WorkloadNativeTemplatePolicyRegistration foreign = second.ServiceProvider.GetRequiredService<WorkloadNativeTemplatePolicyRegistration>();

        Action bind = () => candidate.Admission.BindExecutor(foreign, new TerminalCompileExecutor(_files), true);

        bind.Should().Throw<ArgumentException>().WithParameterName("owner");
        File.Exists(Path.Combine(target, "Handler.fs")).Should().BeFalse();
    }

    [Fact]
    public async Task LoadedPolicy_EditorNoOp_CompletesWithoutExecutorOrPhysicalMarker()
    {
        WorkloadEntry entry = await InstallPolicyFixtureAsync();
        using ServiceProvider provider = await CreatePolicyServicesAsync();
        using IServiceScope scope = provider.CreateScope();
        string target = Directory.CreateDirectory(Path.Combine(_root, "editor-no-output")).FullName;
        using Templater templater = await OpenPolicyItemAsync(scope, entry, target);
        ResolvedTemplate selected = (await templater.ResolveGroupAsync("Fixture.Editor", TemplateType.Item)).Group![0];

        TemplateInvocationResult result = await selected.InvokeAsync(new(WorkingDirectory.FromExplicit(target), new Dictionary<string, string?>(), "Editor"));

        result.Status.Should().Be(TemplateInvocationStatus.Success, result.Diagnostic);
        result.HasCommittedOutput.Should().BeFalse();
        result.Changes.Should().BeEmpty();
        result.Effects!.CompletedEffects.Should().ContainSingle().Which.Should().BeOfType<TemplateEditorNoOpEffect>();
        Directory.GetFileSystemEntries(target).Should().BeEmpty();
    }

    private string WriteCompileTarget()
    {
        string target = Directory.CreateDirectory(Path.Combine(_root, "compile-target")).FullName;
        File.WriteAllText(Path.Combine(target, "Fixture.fsproj"), "<Project><ItemGroup><Compile Include=\"Bootstrap.fs\" /></ItemGroup></Project>");
        File.WriteAllText(Path.Combine(target, "Bootstrap.fs"), "module Bootstrap");
        return target;
    }

    private static Task<Templater> OpenPolicyItemAsync(IServiceScope scope, WorkloadEntry entry, string target)
    {
        var directory = WorkingDirectory.FromExplicit(target);
        return scope.ServiceProvider.GetRequiredService<ITemplaterFactory>().CreateAsync(new(directory, new(directory, "fixture", "Fixture")),
            new([new(entry.PackageId, NuGetVersion.Parse(entry.PackageVersion), entry.Aliases, true)]), CancellationToken.None);
    }

    private sealed class TerminalCompileExecutor(PhysicalFileSystem files) : INativeTemplateEffectCapability
    {
        public int Executions { get; private set; }

        public async Task<IReadOnlyList<string>> ValidateAsync(NativeTemplateEffectPlan plan, CancellationToken cancellationToken)
        {
            foreach (TemplateFSharpCompileEffect effect in plan.Effects.OfType<TemplateFSharpCompileEffect>())
            {
                if (!string.Equals(await files.ComputeSha256Async(effect.ProjectPath, cancellationToken), effect.ExpectedProjectSha256,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return ["The actual compile project digest changed."];
                }
            }
            return [];
        }

        public async Task<TemplateEffectExecutionResult> ExecuteAsync(TemplateEffect effect, CancellationToken cancellationToken)
        {
            var compile = (TemplateFSharpCompileEffect)effect;
            if (!string.Equals(await files.ComputeSha256Async(compile.ProjectPath, cancellationToken), compile.ExpectedProjectSha256,
                StringComparison.OrdinalIgnoreCase))
            {
                return new(false, ["The actual compile project digest changed."]);
            }
            var document = XDocument.Load(compile.ProjectPath);
            XElement terminal = document.Descendants("Compile").Last();
            ((string?)terminal.Attribute("Include")).Should().Be(Path.GetFileName(compile.EvaluatedCompileOrder[^1]));
            foreach (TemplateGeneratedFSharpFile generated in compile.GeneratedFiles)
            {
                terminal.AddBeforeSelf(new XElement("Compile", new XAttribute("Include", Path.GetFileName(generated.Path))));
            }
            await files.WriteAllTextAsync(compile.ProjectPath, document.ToString(), cancellationToken);
            Executions++;
            return new(true, []);
        }
    }

    private async Task<ServiceProvider> CreatePolicyServicesAsync(IBasicCompanionCatalog? authority = null,
        IProjectStack? additionalStack = null, INewExecutionContextResolver? contexts = null, IInteractionService? interaction = null,
        Action<IServiceCollection>? configure = null)
    {
        return await CreateServicesAsync(services =>
        {
            services.AddSingleton<IWorkloadStore>(_workloads);
            services.AddSingleton<IWorkloadPaths>(_paths);
            services.AddSingleton<ICliVersionProvider>(AssemblyCliVersionProvider.Instance);
            if (additionalStack is not null)
            {
                services.AddSingleton(additionalStack);
            }
            if (contexts is not null)
            {
                services.AddSingleton(contexts);
            }
            if (interaction is not null)
            {
                services.AddSingleton(interaction);
            }
            if (authority is not null)
            {
                services.AddSingleton(authority);
                services.AddSingleton<BasicAcquisitionPlanner>();
            }
            var coordinator = new FileWorkloadOperationCoordinator(_paths);
            BundledTemplateInstaller bundled = new(_templates, _inspector, _files, new(Home));
            services.AddSingleton<IBundledTemplateInstaller>(bundled);
            services.AddSingleton<IWorkloadDeploymentService>(new WorkloadDeploymentService(
                _paths, _workloads, new WorkloadMetadataReader(), coordinator, _files,
                bundled));
            configure?.Invoke(services);
        }, validateScopes: true);
    }

    private async Task<WorkloadEntry> InstallPolicyFixtureAsync(bool fromCatalog = false,
        bool composedParameters = false, bool promptRequired = false, bool utf8Bom = false, bool preparation = false)
    {
        string input = Directory.CreateDirectory(Path.Combine(_root, "policy-content")).FullName;
        WritePolicyContent(input);
        if (preparation)
        {
            string path = Path.Combine(input, "content", "project", ".template.config", "template.json");
            var configuration = JsonNode.Parse(File.ReadAllText(path))!;
            configuration["symbols"]!["SkipNpmInstall"] = JsonNode.Parse("""{"type":"parameter","datatype":"bool","defaultValue":"false"}""");
            File.WriteAllText(path, configuration.ToJsonString());
        }
        if (composedParameters)
        {
            string[] kinds = ["project", "editor"];
            foreach (string kind in kinds)
            {
                string path = Path.Combine(input, "content", kind, ".template.config", "template.json");
                var configuration = JsonNode.Parse(File.ReadAllText(path))!;
                configuration["symbols"] ??= new JsonObject();
                JsonObject symbols = configuration["symbols"]!.AsObject();
                symbols["Framework"] ??= JsonNode.Parse(
                    """{"type":"parameter","datatype":"choice","defaultValue":"net8.0","choices":[{"choice":"net8.0"},{"choice":"net9.0"},{"choice":"net10.0"}]}""");
                symbols["Greeting"] = JsonNode.Parse(
                    """{"type":"parameter","datatype":"string","defaultValue":"authored-greeting","replaces":"GREETING"}""");
                symbols["Prompted"] = promptRequired ? JsonNode.Parse(
                    """{"type":"parameter","datatype":"string","isRequired":true,"replaces":"PROMPTED"}""")
                    : JsonNode.Parse("""{"type":"parameter","datatype":"string","defaultValue":"authored-prompt","replaces":"PROMPTED"}""");
                File.WriteAllText(path, configuration.ToJsonString());
            }
            File.WriteAllText(Path.Combine(input, "content", "project", "Owner.App.proj"), "Owner.App|FRAMEWORK|GREETING|PROMPTED");
        }
        if (utf8Bom)
        {
            string path = Path.Combine(input, "content", "project", ".template.config", "template.json");
            File.WriteAllBytes(path, [.. new UTF8Encoding(true).GetPreamble(), .. File.ReadAllBytes(path)]);
        }
        string native = Path.Combine(_root, "policy-templates.nupkg");
        WritePolicyPackage(input, native, "Fixture.Policy.Templates", "2.0.0", "FuncTemplate");
        string runtime = Directory.CreateDirectory(Path.Combine(_root, "policy-runtime")).FullName;
        string tools = Directory.CreateDirectory(Path.Combine(runtime, "tools", "any")).FullName;
        File.Copy(Path.Combine(AppContext.BaseDirectory, "tools", "any", PolicyAssembly), Path.Combine(tools, PolicyAssembly));
        string deps = Path.ChangeExtension(PolicyAssembly, ".deps.json");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "tools", "any", deps), Path.Combine(tools, deps));
        Directory.CreateDirectory(Path.Combine(tools, "templates"));
        File.Copy(native, Path.Combine(tools, "templates", "policy.nupkg"));
        File.WriteAllText(Path.Combine(runtime, "workload.json"), $$$"""
            {"$schema":"https://aka.ms/func-workloads/package/v1/schema.json","kind":"workload",
             "entryPoint":{"assemblyPath":"tools/any/{{{PolicyAssembly}}}",
             "type":"Azure.Functions.Cli.Workloads.Tests.Fixtures.NativePolicy.PolicyWorkload"}}
            """);
        string digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(native)));
        BasicTemplateDefinition[] definitions =
        [
            new("content/project/.template.config/template.json", "Fixture.Project", "project"),
            new("content/item/.template.config/template.json", "Fixture.Item", "item"),
            new("content/helper/.template.config/template.json", "Fixture.Helper", "item"),
            new("content/compile/.template.config/template.json", "Fixture.Compile", "item"),
            new("content/editor/.template.config/template.json", "Fixture.Editor", "item"),
        ];
        File.WriteAllText(Path.Combine(runtime, "bundled-templates.json"),
            JsonSerializer.Serialize(new BundledTemplatesManifest(1, [new("tools/any/templates/policy.nupkg",
                "Fixture.Policy.Templates", "2.0.0", digest, definitions)]), BundledTemplateJsonContext.Default.BundledTemplatesManifest));
        string archive = Path.Combine(_root, "policy-runtime.nupkg");
        WritePolicyPackage(runtime, archive, PolicyRuntimeId, "1.0.0", "FuncCliWorkload");
        if (!fromCatalog)
        {
            return (await _installer.InstallFromPackageAsync(archive)).Entry;
        }
        const string feed = "https://fixtures.invalid/v3/index.json";
        _source.ResolveAsync(PolicyRuntimeId, NuGetVersion.Parse("1.0.0"), feed, true, true, Arg.Any<CancellationToken>())
            .Returns(new Azure.Functions.Cli.Workloads.Catalog.ResolvedPackage(PolicyRuntimeId, NuGetVersion.Parse("1.0.0"), new(feed)));
        _source.DownloadAsync(Arg.Any<Azure.Functions.Cli.Workloads.Catalog.ResolvedPackage>(),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>()).Returns(_ =>
            {
                string temporary = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".nupkg");
                File.Copy(archive, temporary);
                return new TemporaryWorkloadPackageFile(temporary);
            });
        return (await _installer.InstallFromCatalogAsync(PolicyRuntimeId, NuGetVersion.Parse("1.0.0"), feed, true, true, false)).Entry;
    }

    private static void WritePolicyPackage(string input, string output, string id, string version, string type)
    {
        PackageBuilder builder = new() { Id = id, Version = NuGetVersion.Parse(version), Description = "Native policy fixture" };
        builder.Authors.Add("Tests");
        builder.PackageTypes = [new PackageType(type, new Version(1, 0))];
        foreach (string file in Directory.GetFiles(input, "*", SearchOption.AllDirectories))
        {
            builder.Files.Add(new PhysicalPackageFile { SourcePath = file, TargetPath = Path.GetRelativePath(input, file).Replace('\\', '/') });
        }
        using FileStream stream = File.Create(output);
        builder.Save(stream);
    }

    private static void WritePolicyContent(string input)
    {
        void Write(string relative, string text)
        {
            string path = Path.Combine(input, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
        Write("content/layout.json", "{\"owned\":true}");
        Write("content/project/.template.config/template.json", """
            {"identity":"Fixture.Project","name":"Owned project","shortName":"fixture-project",
             "tags":{"type":"project","language":"Fixture"},"sourceName":"Owner.App",
             "sources":[{"modifiers":[{"rename":{"_ownerignore":".ownerignore"}}]}],
             "primaryOutputs":[{"path":"Owner.App.proj"}],
             "symbols":{"Framework":{"type":"parameter","datatype":"choice","defaultValue":"net8.0","replaces":"FRAMEWORK",
               "choices":[{"choice":"net8.0"},{"choice":"net9.0"},{"choice":"net10.0"}]}}}
            """);
        Write("content/project/Owner.App.proj", "Owner.App|FRAMEWORK");
        Write("content/project/host.json", "{\"version\":\"2.0\"}");
        Write("content/project/_ownerignore", "owner-ignore");
        Write("content/item/.template.config/template.json", """
            {"identity":"Fixture.Item","name":"Owned item","shortName":"fixture-item","sourceName":"FunctionName","tags":{"type":"item","language":"Fixture"}}
            """);
        Write("content/item/function_app.txt", "FunctionName");
        Write("content/helper/.template.config/template.json", """
            {"identity":"Fixture.Helper","name":"Owned helper","shortName":"fixture-helper","sourceName":"FunctionName",
             "tags":{"type":"item","language":"Fixture","func-role":"internal-expansion"}}
            """);
        Write("content/helper/.template.config/func.host.json", "{\"isHidden\":true}");
        Write("content/helper/snippet.txt", "\nFunctionName");
        Write("content/compile/.template.config/template.json", """
            {"identity":"Fixture.Compile","name":"Owned compile","shortName":"fixture-compile","tags":{"type":"item","language":"Fixture"},
             "primaryOutputs":[{"path":"Handler.fs"}]}
            """);
        Write("content/compile/Handler.fs", "module Handler");
        Write("content/editor/.template.config/template.json", $$$"""
            {"identity":"Fixture.Editor","name":"Owned editor no-op","shortName":"fixture-editor","tags":{"type":"item","language":"Fixture"},
             "postActions":[{"actionId":"{{{NativeTemplateEffectPlanner.EditorActionId}}}","continueOnError":true,"args":{"files":"0"}}]}
            """);
    }

    private static T GetFixtureState<T>(NativeTemplatePolicy policy, string property)
        => (T)(policy.GetType().GetProperty(property)!.GetValue(policy) ?? throw new InvalidOperationException($"Fixture did not capture {property}."));

    private sealed class ForeignPolicyStack : IProjectStack
    {
        public string Stack => "foreign";
        public IReadOnlyList<string> SupportedLanguages => ["Fixture"];
        public IReadOnlyDictionary<string, IReadOnlyList<string>> SupportedLanguageAliases => new Dictionary<string, IReadOnlyList<string>>();
    }

    private sealed class PolicyPromptInteraction : TestInteractionService, IInteractionService
    {
        public override bool IsInteractive => true;
        public int Prompts { get; private set; }

        Task<string> IInteractionService.PromptForInputAsync(string prompt, string? defaultValue, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Prompts++;
            return Task.FromResult("host-prompt-value");
        }
    }
}
