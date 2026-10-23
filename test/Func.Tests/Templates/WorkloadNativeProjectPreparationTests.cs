// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Runtime.Loader;
using System.Runtime.InteropServices;
using System.Text;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Native;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task LoadedPreparation_ActualOwner_PhasesOwnCopiedBytesAndCanonicalConfig(bool skip, bool dryRun)
    {
        WorkloadEntry entry = await InstallPolicyFixtureAsync(preparation: true, utf8Bom: true);
        using ServiceProvider provider = await CreatePolicyServicesAsync(configure: services => services.AddScoped<NativeTemplateWorkflowRegistry>());
        using IServiceScope scope = provider.CreateScope();
        NativeTemplatePolicy policy = SetPreparationMode(scope, "preparation-none");
        string target = Path.Combine(_root, "owned-preparation");

        int exit = await PreparationCommand(provider, scope, entry).ParseInvocation(
            [target, "--template", "Fixture.Project", "--name", "Created", "--SkipNpmInstall", skip ? "true" : "false",
             "--non-interactive", "--output", "json", .. dryRun ? new[] { "--dry-run" } : []]).InvokeAsync();

        exit.Should().Be(0, _interaction.AllOutput);
        Directory.Exists(target).Should().Be(!dryRun);
        GetFixtureState<int>(policy, "PreparationExecutions").Should().Be(dryRun || skip ? 0 : 1);
        GetFixtureState<int>(policy, "PreparationCompletions").Should().Be(dryRun ? 0 : 1);
        var preview = GetFixtureState<NativeTemplateCapturedOutput>(policy, "LastPreparationPreview");
        preview.Invocation.CanonicalValues["Framework"].Should().Be("net10.0");
        preview.Invocation.ExplicitParameterNames.Should().Contain("SkipNpmInstall");
        GetFixtureState<byte[]>(policy, "PreparationBytes").Should().Equal(Encoding.UTF8.GetBytes("Created|net10.0"));
        Func<Task> expired = () => preview.ReadFileAsync("Created.proj", CancellationToken.None);
        await expired.Should().ThrowAsync<ObjectDisposedException>();
        var factory = GetFixtureState<NativeTemplateSource>(policy, "FactorySource");
        Func<Task> expiredFactory = () => factory.RevalidateAsync(CancellationToken.None);
        await expiredFactory.Should().ThrowAsync<ObjectDisposedException>();
        if (!dryRun)
        {
            var created = GetFixtureState<NativeTemplateCreatedProject>(policy, "LastCreatedProject");
            created.Output.Should().NotBeSameAs(preview);
            created.Output.Invocation.Source.Should().NotBeSameAs(preview.Invocation.Source);
            created.Output.Invocation.DryRun.Should().BeFalse();
            created.ConfigurationPath.Should().Be(Path.Combine(target, ".func", "config.json"));
            GetFixtureState<byte[]>(policy, "ConfigurationBytes").Should().Equal(File.ReadAllBytes(created.ConfigurationPath));
            Func<Task> expiredConfig = () => created.ReadConfigurationAsync(CancellationToken.None);
            Func<Task> expiredSource = () => created.Output.Invocation.Source.RevalidateAsync(CancellationToken.None);
            await expiredConfig.Should().ThrowAsync<ObjectDisposedException>();
            await expiredSource.Should().ThrowAsync<ObjectDisposedException>();
            _interaction.AllOutput.Should().Contain("\"kind\":\"created\"").And.Contain("\"operation\":\"WorkloadPreparation\"")
                .And.Contain("\"skipped\":" + (skip ? "true" : "false"));
        }
    }

    [Theory]
    [InlineData("preparation-null")]
    [InlineData("preparation-unsupported")]
    [InlineData("preparation-refused")]
    [InlineData("preparation-drift")]
    [InlineData("preparation-factory-null")]
    [InlineData("preparation-factory-unsupported")]
    public async Task LoadedPreparation_RequiredRefusal_DoesNotFallBackOrCreateOutput(string mode)
    {
        WorkloadEntry entry = await InstallPolicyFixtureAsync(preparation: true);
        using ServiceProvider provider = await CreatePolicyServicesAsync();
        using IServiceScope scope = provider.CreateScope();
        NativeTemplatePolicy policy = SetPreparationMode(scope, mode);
        string target = Path.Combine(_root, "refused-preparation");

        int exit = await PreparationCommand(provider, scope, entry).ParseInvocation(
            [target, "--template", "Fixture.Project", "--name", "Created", "--non-interactive", "--output", "json"]).InvokeAsync();

        exit.Should().Be(1, _interaction.AllOutput);
        Directory.Exists(target).Should().BeFalse();
        GetFixtureState<int>(policy, "PreparationExecutions").Should().Be(0);
        _interaction.AllOutput.Should().NotContain("\"succeeded\":true");
    }

    [Theory]
    [InlineData("preparation-completion-null")]
    [InlineData("preparation-completion-unsupported")]
    [InlineData("preparation-completion-refused")]
    public async Task LoadedPreparation_CompletionRefusal_RetainsNativeAndConfig(string mode)
    {
        WorkloadEntry entry = await InstallPolicyFixtureAsync(preparation: true);
        using ServiceProvider provider = await CreatePolicyServicesAsync();
        using IServiceScope scope = provider.CreateScope();
        NativeTemplatePolicy policy = SetPreparationMode(scope, mode);
        string target = Path.Combine(_root, "partial-preparation");

        int exit = await PreparationCommand(provider, scope, entry).ParseInvocation(
            [target, "--template", "Fixture.Project", "--name", "Created", "--non-interactive", "--output", "json"]).InvokeAsync();

        exit.Should().Be(1, _interaction.AllOutput);
        File.ReadAllText(Path.Combine(target, "Created.proj")).Should().Be("Created|net10.0");
        File.Exists(Path.Combine(target, ".func", "config.json")).Should().BeTrue();
        GetFixtureState<int>(policy, "PreparationExecutions").Should().Be(0);
        _interaction.AllOutput.Should().Contain("\"kind\":\"partialPreparation\"");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task LoadedPreparation_ReturnedTypedOutcome_IsAuthoritativeThroughLateCancellation(bool success, bool lateCancellation)
    {
        WorkloadEntry entry = await InstallPolicyFixtureAsync(preparation: true);
        using CancellationTokenSource cancellation = new();
        using ServiceProvider provider = await CreatePolicyServicesAsync();
        using IServiceScope scope = provider.CreateScope();
        NativeTemplatePolicy policy = SetPreparationMode(scope, "preparation-diagnostics");
        string target = Path.Combine(_root, "returned-preparation");
        SetCompletionHook(policy, (created, plan, _) =>
        {
            File.Exists(created.ConfigurationPath).Should().BeTrue();
            plan.Diagnostics.Should().Equal("Owner preparation diagnostics.");
            if (lateCancellation) cancellation.Cancel();
            return Task.FromResult(new NativeTemplateProjectPreparationResult(success, ["Exact owner helper stderr. Error text is informational."], true));
        });

        int exit = await PreparationCommand(provider, scope, entry).ParseInvocation(
            [target, "--template", "Fixture.Project", "--name", "Created", "--non-interactive", "--output", "json"])
            .InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false }, cancellation.Token);

        exit.Should().Be(success ? 0 : 1, _interaction.AllOutput);
        GetFixtureState<int>(policy, "PreparationExecutions").Should().Be(1);
        _interaction.AllOutput.Should().Contain("Exact owner helper stderr.").And.Contain("\"targetMayHaveChanged\":true")
            .And.Contain(success ? "\"kind\":\"created\"" : "\"kind\":\"partialPreparation\"");
    }

    [Fact]
    public async Task LoadedPreparation_ThrownCancellation_RetainsOriginalExceptionAndOutputWarning()
    {
        WorkloadEntry entry = await InstallPolicyFixtureAsync(preparation: true);
        using CancellationTokenSource cancellation = new();
        OperationCanceledException original = new("Original workload cancellation.", cancellation.Token);
        using ServiceProvider provider = await CreatePolicyServicesAsync();
        using IServiceScope scope = provider.CreateScope();
        NativeTemplatePolicy policy = SetPreparationMode(scope, "preparation-none");
        string target = Path.Combine(_root, "canceled-preparation");
        SetCompletionHook(policy, (_, _, _) =>
        {
            cancellation.Cancel();
            return Task.FromException<NativeTemplateProjectPreparationResult>(original);
        });

        Func<Task> run = () => PreparationCommand(provider, scope, entry).ParseInvocation(
            [target, "--template", "Fixture.Project", "--name", "Created", "--non-interactive", "--output", "json"])
            .InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false }, cancellation.Token);

        var thrown = await run.Should().ThrowAsync<OperationCanceledException>();
        thrown.Which.Should().BeSameAs(original);
        thrown.Which.CancellationToken.Should().Be(cancellation.Token);
        thrown.Which.StackTrace.Should().Contain("Complete");
        _interaction.AllOutput.Should().Contain("were created before").And.Contain("No rollback").And.NotContain("\"succeeded\":true");
        File.Exists(Path.Combine(target, ".func", "config.json")).Should().BeTrue();
    }

    [Fact]
    public async Task LoadedPreparation_ConfigurationAndImageReads_AreIndependentCopies()
    {
        WorkloadEntry entry = await InstallPolicyFixtureAsync(preparation: true);
        using ServiceProvider provider = await CreatePolicyServicesAsync();
        using IServiceScope scope = provider.CreateScope();
        NativeTemplatePolicy policy = SetPreparationMode(scope, "preparation-none");
        string target = Path.Combine(_root, "copy-owned-preparation");
        SetCompletionHook(policy, async (created, _, token) =>
        {
            ReadOnlyMemory<byte> first = await created.ReadConfigurationAsync(token);
            byte[] original = first.ToArray();
            MemoryMarshal.TryGetArray(first, out ArraySegment<byte> exposed).Should().BeTrue();
            exposed.Array![exposed.Offset] ^= 1;
            (await created.ReadConfigurationAsync(token)).ToArray().Should().Equal(original);
            ReadOnlyMemory<byte> output = await created.Output.ReadFileAsync("Created.proj", token);
            MemoryMarshal.TryGetArray(output, out ArraySegment<byte> image).Should().BeTrue();
            image.Array![image.Offset] ^= 1;
            (await created.Output.ReadFileAsync("Created.proj", token)).ToArray().Should().Equal(Encoding.UTF8.GetBytes("Created|net10.0"));
            await created.RevalidateAsync(token);
            return new(true, []);
        });

        int exit = await PreparationCommand(provider, scope, entry).ParseInvocation(
            [target, "--template", "Fixture.Project", "--name", "Created", "--non-interactive", "--output", "json"]).InvokeAsync();

        exit.Should().Be(0, _interaction.AllOutput);
    }

    [Fact]
    public async Task LoadedPreparation_UnownedCapabilityAndRequestFallback_CannotAcquireExecution()
    {
        WorkloadEntry entry = await InstallPolicyFixtureAsync(preparation: true);
        using ServiceProvider provider = await CreatePolicyServicesAsync();
        using IServiceScope scope = provider.CreateScope();
        NativeTemplatePolicy policy = SetPreparationMode(scope, "preparation-none");
        var foreign = Substitute.For<INativeTemplateProjectPreparation>();
        policy.GetType().GetProperty("CapabilityOverride")!.SetValue(policy, foreign);
        string target = Path.Combine(_root, "foreign-preparation");

        int exit = await PreparationCommand(provider, scope, entry).ParseInvocation(
            [target, "--template", "Fixture.Project", "--name", "Created", "--non-interactive", "--output", "json"]).InvokeAsync();

        exit.Should().Be(1);
        _interaction.AllOutput.Should().Contain("actual loaded owning workload");
        Directory.Exists(target).Should().BeFalse();
        foreign.ReceivedCalls().Should().BeEmpty();
        var requestCapability = Substitute.For<INativeProjectPreparation>();
        InitExecutionResult result = await scope.ServiceProvider.GetRequiredService<IInitExecutionRunner>().ExecuteAsync(
            new(WorkingDirectory.FromExplicit(target), "Fixture.Project", "Created", null, null, false, true, false, true, [], new HashSet<string>())
            { ProjectPreparation = requestCapability }, Availability(entry), CancellationToken.None);
        result.Diagnostics.Should().ContainSingle().Which.Should().Contain("cannot be replaced");
        requestCapability.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("none")]
    [InlineData("raw-source")]
    [InlineData("file-kind")]
    [InlineData("file-bytes")]
    [InlineData("config-bytes")]
    [InlineData("configuration-proof")]
    [InlineData("target")]
    [InlineData("preserve")]
    [InlineData("preserve-changed-after-preview")]
    [InlineData("preserve-changed-after-create")]
    public async Task LoadedPreparation_CreatedProof_IsBoundOnceToExactSourceImageAndConfiguration(string change)
    {
        WorkloadEntry entry = await InstallPolicyFixtureAsync(preparation: true);
        using ServiceProvider provider = await CreatePolicyServicesAsync();
        using IServiceScope scope = provider.CreateScope();
        NativeTemplatePolicy policy = SetPreparationMode(scope, "preparation-none");
        string target = Path.Combine(_root, "bound-preparation");
        bool preserve = change.StartsWith("preserve", StringComparison.Ordinal);
        string preserved = Path.Combine(target, "Created.proj");
        if (preserve)
        {
            Directory.CreateDirectory(target);
            File.WriteAllText(preserved, "user-owned bytes");
        }
        var directory = WorkingDirectory.FromExplicit(target);
        using Templater templater = await scope.ServiceProvider.GetRequiredService<ITemplaterFactory>()
            .CreateProjectAsync(new(directory), Availability(entry), provider.GetRequiredService<TemplateProjectSupport>(), CancellationToken.None);
        ResolvedTemplate selected = (await templater.ResolveGroupAsync("Fixture.Project", TemplateType.Project)).Group![0];
        WorkloadNativeTemplatePolicyAdapter adapter = (await scope.ServiceProvider.GetRequiredService<WorkloadNativeTemplatePolicies>()
            .FindAsync(selected, CancellationToken.None))!;
        var preparation = (WorkloadNativeProjectPreparation)adapter.Preparation!;
        TemplateInvocationRequest request = await preparation.BindInvocationAsync(new(directory,
            new Dictionary<string, string?> { ["Framework"] = "net10.0", ["SkipNpmInstall"] = "false" }, "Created", DryRun: true)
        {
            PolicyCandidate = (await scope.ServiceProvider.GetRequiredService<WorkloadNativeTemplatePolicies>()
                .GetCandidateAsync(selected.Entry, selected.Configuration!, selected.ConfigurationSourceIdentity!, selected.PackageMountPoint,
                    selected.ReadPackageFileAsync, selected.ValidateConfigurationAsync, true, CancellationToken.None, selected.InvocationOwner))!,
            ExplicitParameterNames = new HashSet<string> { "Framework", "SkipNpmInstall" },
            PreserveExistingFiles = preserve,
        }, CancellationToken.None);
        TemplateInvocationResult preview = await selected.InvokeAsync(request);
        preview.Status.Should().Be(TemplateInvocationStatus.Success, preview.Diagnostic);
        NativeProjectPreparationContext context = new(selected, directory, request.Parameters, preview);
        NativeProjectPreparationPreview plan = await preparation.PreviewAsync(context, CancellationToken.None);
        plan.Succeeded.Should().BeTrue(string.Join(" ", plan.Diagnostics));
        if (change == "preserve-changed-after-preview")
        {
            File.WriteAllText(preserved, "changed after preview");
        }
        TemplateInvocationResult created = await selected.InvokeAsync(request with
        {
            DryRun = false,
            ExpectedPreparation = request.ProjectPreparationBinding!.ExpectedCapture,
        });
        if (change == "preserve-changed-after-preview")
        {
            created.Status.Should().Be(TemplateInvocationStatus.InvalidTemplate);
            created.Diagnostic.Should().Contain("Preserved project inputs changed");
            File.ReadAllText(preserved).Should().Be("changed after preview");
            File.Exists(Path.Combine(target, ".func", "config.json")).Should().BeFalse();
            GetFixtureState<int>(policy, "PreparationExecutions").Should().Be(0);
            return;
        }
        created.Status.Should().Be(TemplateInvocationStatus.Success, created.Diagnostic);
        if (preserve)
        {
            File.ReadAllText(preserved).Should().Be("user-owned bytes");
        }
        if (change == "preserve-changed-after-create") await File.WriteAllTextAsync(preserved, "changed after create");
        if (change == "raw-source") await File.AppendAllTextAsync(selected.PackageMountPoint!, "changed");
        if (change == "file-bytes") await File.WriteAllTextAsync(Path.Combine(target, "Created.proj"), "changed");
        if (change == "config-bytes") await File.AppendAllTextAsync(Path.Combine(target, ".func", "config.json"), " ");
        if (change == "file-kind")
        {
            File.Delete(Path.Combine(target, "Created.proj"));
            Directory.CreateDirectory(Path.Combine(target, "Created.proj"));
        }

        NativeProjectPreparationContext completionContext = context with
        {
            Created = change == "configuration-proof" ? created with { ConfiguredProjects = [] } : created,
            TargetDirectory = change == "target" ? WorkingDirectory.FromExplicit(Path.Combine(target, "foreign")) : directory,
        };
        NativeProjectPreparationResult result = await preparation.CompleteAsync(completionContext, plan, CancellationToken.None);
        NativeProjectPreparationResult replay = await preparation.CompleteAsync(context with { Created = created }, plan, CancellationToken.None);

        bool succeeds = change is "none" or "preserve";
        result.Succeeded.Should().Be(succeeds, string.Join(" ", result.Diagnostics));
        replay.Succeeded.Should().BeFalse();
        GetFixtureState<int>(policy, "PreparationExecutions").Should().Be(succeeds ? 1 : 0);
        created.HasCommittedOutput.Should().BeTrue();
        AssemblyLoadContext.GetLoadContext(policy.GetType().Assembly).Should()
            .BeSameAs(scope.ServiceProvider.GetRequiredService<WorkloadNativeTemplatePolicyRegistration>().Owner.LoadContext);
    }

    [Fact]
    public async Task LoadedPreparation_RequiredPhase_DirectCreateWithoutJourneyRefuses()
    {
        WorkloadEntry entry = await InstallPolicyFixtureAsync(preparation: true);
        using ServiceProvider provider = await CreatePolicyServicesAsync();
        using IServiceScope scope = provider.CreateScope();
        SetPreparationMode(scope, "preparation-none");
        var target = WorkingDirectory.FromExplicit(Path.Combine(_root, "direct-preparation"));
        using Templater templater = await scope.ServiceProvider.GetRequiredService<ITemplaterFactory>()
            .CreateProjectAsync(new(target), Availability(entry), provider.GetRequiredService<TemplateProjectSupport>(), CancellationToken.None);
        ResolvedTemplate selected = (await templater.ResolveGroupAsync("Fixture.Project", TemplateType.Project)).Group![0];

        TemplateInvocationResult result = await selected.InvokeAsync(new(target,
            new Dictionary<string, string?> { ["Framework"] = "net10.0", ["SkipNpmInstall"] = "false" }, "Created")
        { ExplicitParameterNames = new HashSet<string> { "Framework", "SkipNpmInstall" } });

        result.Status.Should().Be(TemplateInvocationStatus.InvalidTemplate);
        result.Diagnostic.Should().Contain("owner-bound project preparation");
        Directory.Exists(target.Info.FullName).Should().BeFalse();
    }

    [Fact]
    public async Task LoadedPreparation_PreCanceledCommand_NeverBeginsAnyPreparation()
    {
        WorkloadEntry entry = await InstallPolicyFixtureAsync(preparation: true);
        using ServiceProvider provider = await CreatePolicyServicesAsync();
        using IServiceScope scope = provider.CreateScope();
        NativeTemplatePolicy policy = SetPreparationMode(scope, "preparation-none");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        string target = Path.Combine(_root, "pre-canceled-preparation");

        Func<Task> run = () => PreparationCommand(provider, scope, entry).ParseInvocation(
            [target, "--template", "Fixture.Project", "--name", "Created", "--non-interactive", "--output", "json"])
            .InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false }, cancellation.Token);

        await run.Should().ThrowAsync<OperationCanceledException>();
        GetFixtureState<int>(policy, "PreparationCompletions").Should().Be(0);
        GetFixtureState<int>(policy, "PreparationExecutions").Should().Be(0);
        Directory.Exists(target).Should().BeFalse();
        _interaction.AllOutput.Should().NotContain("were created before");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadedPreparation_IncompleteNativeOrConfiguration_NeverCallsOwnerCompletion(bool configuration)
    {
        WorkloadEntry entry = await InstallPolicyFixtureAsync(preparation: true);
        using CancellationTokenSource cancellation = new();
        var recording = new PreparationCommitFiles(cancellation, configuration);
        using ServiceProvider provider = await CreatePolicyServicesAsync(configure: services => services.AddSingleton<IFileSystem>(recording));
        using IServiceScope scope = provider.CreateScope();
        NativeTemplatePolicy policy = SetPreparationMode(scope, "preparation-none");
        string target = Path.Combine(_root, "incomplete-preparation");

        int exit = await PreparationCommand(provider, scope, entry).ParseInvocation(
            [target, "--template", "Fixture.Project", "--name", "Created", "--non-interactive", "--output", "json"])
            .InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false }, cancellation.Token);

        exit.Should().Be(1, _interaction.AllOutput);
        GetFixtureState<int>(policy, "PreparationCompletions").Should().Be(0);
        GetFixtureState<int>(policy, "PreparationExecutions").Should().Be(0);
        Directory.Exists(target).Should().BeTrue();
        _interaction.AllOutput.Should().NotContain("\"kind\":\"created\"");
    }

    [Theory]
    [InlineData("unchanged")]
    [InlineData("extra-fields")]
    [InlineData("formatting")]
    public async Task LoadedPreparation_FirstConfigurationReceipt_RequiresActualWriterBytes(string mutation)
    {
        WorkloadEntry entry = await InstallPolicyFixtureAsync(preparation: true);
        var files = new PreparationConfigurationSwapFiles(mutation);
        using ServiceProvider provider = await CreatePolicyServicesAsync(configure: services => services.AddSingleton<IFileSystem>(files));
        using IServiceScope scope = provider.CreateScope();
        NativeTemplatePolicy policy = SetPreparationMode(scope, "preparation-none");
        string target = Path.Combine(_root, "first-receipt-preparation");
        var request = new InitExecutionRequest(WorkingDirectory.FromExplicit(target), "Fixture.Project", "Created",
            null, null, false, true, false, true, [], new HashSet<string>());

        InitExecutionResult result = await scope.ServiceProvider.GetRequiredService<IInitExecutionRunner>()
            .ExecuteAsync(request, Availability(entry), CancellationToken.None);
        int exit = provider.GetRequiredService<InitExecutionRenderer>().Render(result, jsonOutput: true);

        bool unchanged = mutation == "unchanged";
        exit.Should().Be(unchanged ? 0 : 1, _interaction.AllOutput);
        result.Kind.Should().Be(unchanged ? "created" : "partialInitialization");
        result.Invocation!.HasCommittedOutput.Should().BeTrue();
        result.Invocation.Status.Should().Be(unchanged ? TemplateInvocationStatus.Success : TemplateInvocationStatus.ConfigurationFailure);
        result.Invocation.ConfiguredProjects.Should().Equal(target);
        result.Invocation.Changes.Should().Contain(change => Path.GetFullPath(Path.Combine(target, change.Path))
            == Path.Combine(target, ".func", "config.json") && change.Kind == TemplateFileChangeKind.Create);
        File.ReadAllText(Path.Combine(target, "Created.proj")).Should().Be("Created|net10.0");
        GetFixtureState<int>(policy, "PreparationCompletions").Should().Be(unchanged ? 1 : 0);
        GetFixtureState<int>(policy, "PreparationExecutions").Should().Be(unchanged ? 1 : 0);
        files.SubmittedBytes.Should().NotBeNull();
        if (!unchanged)
        {
            File.ReadAllBytes(Path.Combine(target, ".func", "config.json")).Should().NotEqual(files.SubmittedBytes!);
            result.Invocation.Diagnostic.Should().Contain("writer");
            result.Preparation!.Completion.Should().BeNull();
        }
    }

    [Fact]
    public async Task LoadedPreparation_WriterReceipt_OwnsCanonicalBytesAndKeepsCreateOnlyContracts()
    {
        string target = Directory.CreateDirectory(Path.Combine(_root, "writer-receipt")).FullName;
        string primary = Path.Combine(target, "Primary.proj");
        File.WriteAllText(primary, "source");
        TemplateProjectConfigurationPlan plan = new(primary, "fixture", "Fixture");
        TemplateProjectConfigurationWriter writer = new(_files);
        Action nullPlan = () => writer.WriteAsync(null!, CancellationToken.None);
        nullPlan.Should().Throw<ArgumentNullException>().WithParameterName("plan");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Action canceled = () => writer.WriteAsync(plan, cancellation.Token);
        canceled.Should().Throw<OperationCanceledException>();

        TemplateProjectConfigurationWriteReceipt written = await writer.WriteAsync(plan, CancellationToken.None);

        written.ConfigurationPath.Should().Be(plan.ConfigurationPath);
        byte[] actual = File.ReadAllBytes(plan.ConfigurationPath);
        written.Content.ToArray().Should().Equal(actual);
        MemoryMarshal.TryGetArray(written.Content, out ArraySegment<byte> exposed).Should().BeTrue();
        exposed.Array![exposed.Offset] ^= 1;
        written.Content.ToArray().Should().Equal(actual);
        Action occupied = () => writer.WriteAsync(plan, CancellationToken.None);
        occupied.Should().Throw<InvalidOperationException>();
        File.ReadAllBytes(plan.ConfigurationPath).Should().Equal(actual);
        Action missingPath = () => new TemplateProjectConfigurationWriteReceipt(null!, actual);
        missingPath.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(true, true, false, false, false, true)]
    [InlineData(true, true, false, false, true, false)]
    [InlineData(true, false, false, false, false, false)]
    [InlineData(false, false, true, false, false, false)]
    [InlineData(true, true, false, true, false, false)]
    [InlineData(false, false, false, false, true, true)]
    [InlineData(false, false, false, false, false, true)]
    public void PreparationOutputPolicy_AllowsOnlyControlledCleanupNormalization(
        bool previewForce, bool previewCleanup, bool actualForce, bool actualCleanup, bool dryRun, bool expected)
    {
        TemplateInvocationRequest preview = new(WorkingDirectory.FromCwd(), new Dictionary<string, string?>())
        {
            Force = previewForce,
            PreviewAfterDirectoryCleanup = previewCleanup,
        };
        TemplateInvocationRequest current = preview with { Force = actualForce, PreviewAfterDirectoryCleanup = actualCleanup, DryRun = dryRun };

        NativeProjectPreparationCapture.MatchesOutputPolicy(preview, current).Should().Be(expected);
        NativeProjectPreparationCapture.MatchesOutputPolicy(preview, current with { PreserveExistingFiles = true }).Should().BeFalse();
    }

    private static NativeTemplatePolicy SetPreparationMode(IServiceScope scope, string mode)
    {
        NativeTemplatePolicy policy = scope.ServiceProvider.GetRequiredService<WorkloadNativeTemplatePolicyRegistration>().Policy;
        policy.GetType().GetProperty("Mode")!.SetValue(policy, mode);
        return policy;
    }

    private static void SetCompletionHook(NativeTemplatePolicy policy,
        Func<NativeTemplateCreatedProject, NativeTemplateProjectPreparationPlan, CancellationToken, Task<NativeTemplateProjectPreparationResult>> hook)
        => policy.GetType().GetProperty("CompleteHook")!.SetValue(policy, hook);

    private static TemplateConstraintEvaluationSnapshot Availability(WorkloadEntry entry)
        => new([new(entry.PackageId, NuGetVersion.Parse(entry.PackageVersion), entry.Aliases, true)]);

    private static InitExecutionCommand PreparationCommand(ServiceProvider provider, IServiceScope scope, WorkloadEntry entry)
        => new(scope.ServiceProvider.GetRequiredService<IInitExecutionRunner>(), provider.GetRequiredService<InitExecutionRenderer>(), Availability(entry));

    private sealed class PreparationCommitFiles(CancellationTokenSource cancellation, bool configuration) : PhysicalFileSystem
    {
        public override void MoveFile(string sourcePath, string destinationPath, bool overwrite = false)
        {
            base.MoveFile(sourcePath, destinationPath, overwrite);
            bool config = Path.GetFileName(destinationPath) == "config.json" && Path.GetFileName(Path.GetDirectoryName(destinationPath)) == ".func";
            if (config == configuration)
            {
                cancellation.Cancel();
            }
        }
    }

    private sealed class PreparationConfigurationSwapFiles(string mutation) : PhysicalFileSystem
    {
        public byte[]? SubmittedBytes { get; private set; }

        public override void MoveFile(string sourcePath, string destinationPath, bool overwrite = false)
        {
            base.MoveFile(sourcePath, destinationPath, overwrite);
            if (Path.GetFileName(destinationPath) != "config.json" || Path.GetFileName(Path.GetDirectoryName(destinationPath)) != ".func")
            {
                return;
            }
            SubmittedBytes = File.ReadAllBytes(destinationPath);
            if (mutation == "extra-fields")
            {
                var edited = System.Text.Json.Nodes.JsonNode.Parse(SubmittedBytes)!;
                edited["profiles"] = System.Text.Json.Nodes.JsonNode.Parse("""["external"]""");
                edited["custom"] = "changed after commit";
                File.WriteAllText(destinationPath, edited.ToJsonString());
            }
            if (mutation == "formatting")
            {
                File.AppendAllText(destinationPath, "\n ");
            }
        }
    }
}
