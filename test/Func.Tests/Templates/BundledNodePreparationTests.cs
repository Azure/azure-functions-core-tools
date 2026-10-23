// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.Loader;
using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Native;
using Azure.Functions.Cli.Workloads.Node;
using Azure.Functions.Cli.Workloads.Node.Native;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    [Theory]
    [InlineData("missing-skip")]
    [InlineData("invalid-skip")]
    [InlineData("item")]
    [InlineData("foreign-package")]
    [InlineData("language-alias")]
    [InlineData("language-mismatch")]
    [InlineData("wrong-identity")]
    public async Task NodePreparation_DirectOwnerPolicy_RefusesUnqualifiedSourceOrSkipBeforeHelper(string corruption)
    {
        var source = Substitute.For<NativeTemplateSource>();
        source.IsProject.Returns(corruption != "item");
        source.PackageId.Returns(corruption == "foreign-package" ? "Foreign" : "Azure.Functions.Templates.Node.Basic");
        source.Language.Returns(corruption == "language-alias" ? "js" : corruption == "language-mismatch" ? "TypeScript" : "JavaScript");
        source.Identity.Returns(corruption == "wrong-identity" ? "Foreign" : "Azure.Functions.Node.Basic.JavaScript");
        var invocation = Substitute.For<NativeTemplateInvocationContext>();
        invocation.Source.Returns(source);
        Dictionary<string, string?> values = [];
        if (corruption != "missing-skip")
        {
            values["SkipNpmInstall"] = corruption == "invalid-skip" ? "invalid" : "false";
        }
        invocation.CanonicalValues.Returns(values);
        var captured = Substitute.For<NativeTemplateCapturedOutput>();
        captured.Invocation.Returns(invocation);
        var npm = Substitute.For<NodeNpmInstaller>();

        var result = await new NodeProjectPreparation(npm).PreviewAsync(captured, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().NotBeEmpty();
        npm.ReceivedCalls().Should().BeEmpty();
        await source.DidNotReceive().ReadPackageFileAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NodePreparation_DirectOwnerPolicy_ValidatesNullAndCancellationContracts()
    {
        var preparation = new NodeProjectPreparation(Substitute.For<NodeNpmInstaller>());
        Func<Task> missingPreview = () => preparation.PreviewAsync(null!, CancellationToken.None);
        Func<Task> missingCreated = () => preparation.CompleteAsync(null!, new(true, false, []), CancellationToken.None);
        Func<Task> missingPlan = () => preparation.CompleteAsync(Substitute.For<NativeTemplateCreatedProject>(), null!, CancellationToken.None);
        await missingPreview.Should().ThrowAsync<ArgumentNullException>();
        await missingCreated.Should().ThrowAsync<ArgumentNullException>();
        await missingPlan.Should().ThrowAsync<ArgumentNullException>();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        var captured = Substitute.For<NativeTemplateCapturedOutput>();

        Func<Task> canceled = () => preparation.PreviewAsync(captured, cancellation.Token);

        await canceled.Should().ThrowAsync<OperationCanceledException>();
        captured.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Preparation_ActualSourcePreview_RefusesWrongTargetAndCannotRunBeforeNativeCreation()
    {
        WorkloadEntry entry = await InstallAssociationStackAsync();
        int calls = 0;
        using var services = await CreateNodePreparationServicesAsync((_, _) =>
        {
            calls++;
            return Task.FromResult((0, ""));
        });
        using var scope = services.CreateScope();
        var target = WorkingDirectory.FromExplicit(Path.Combine(_root, "project"));
        using var templater = await scope.ServiceProvider.GetRequiredService<ITemplaterFactory>().CreateProjectAsync(new(target),
            Availability(entry), services.GetRequiredService<TemplateProjectSupport>(), CancellationToken.None);
        var selected = (await templater.ResolveGroupAsync("Azure.Functions.Node.Basic.JavaScript", TemplateType.Project, CancellationToken.None)).Group!.Single();
        var arguments = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(), []));
        var provider = (await scope.ServiceProvider.GetRequiredService<WorkloadNativeTemplatePolicies>().FindAsync(selected, CancellationToken.None))!;
        var preparation = (WorkloadNativeProjectPreparation)provider.Preparation!;
        var prepared = await provider.PrepareAsync(new NativeProjectWorkflowContext(templater, selected, arguments,
            NodePreparationRequest(target.Info.FullName), new(target, arguments.ValuesByCanonicalName, DryRun: true)), CancellationToken.None);
        var request = await preparation.BindInvocationAsync(prepared.Request!, CancellationToken.None);
        var native = await selected.InvokeAsync(request, CancellationToken.None);
        native.Status.Should().Be(TemplateInvocationStatus.Success, native.Diagnostic);
        var context = new NativeProjectPreparationContext(selected, target, request.Parameters, native);

        var preview = await preparation.PreviewAsync(context, CancellationToken.None);
        var early = await preparation.CompleteAsync(context, preview, CancellationToken.None);
        var wrongTarget = await preparation.PreviewAsync(context with
        {
            TargetDirectory = WorkingDirectory.FromExplicit(Path.Combine(_root, "other-target")),
        }, CancellationToken.None);

        preview.Succeeded.Should().BeTrue(string.Join(" ", preview.Diagnostics));
        early.Succeeded.Should().BeFalse();
        early.Diagnostics.Should().ContainSingle().Which.Should().Contain("successful native and mandatory configuration");
        wrongTarget.Succeeded.Should().BeFalse();
        Directory.Exists(target.Info.FullName).Should().BeFalse();
        calls.Should().Be(0);
        Func<Task> noContext = () => preparation.PreviewAsync(null!, CancellationToken.None);
        await noContext.Should().ThrowAsync<ArgumentNullException>();
        Func<Task> noPreview = () => preparation.CompleteAsync(context, null!, CancellationToken.None);
        await noPreview.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData("JavaScript", false, false)]
    [InlineData("JavaScript", true, false)]
    [InlineData("JavaScript", false, true)]
    [InlineData("JavaScript", true, true)]
    [InlineData("TypeScript", false, false)]
    [InlineData("TypeScript", true, false)]
    [InlineData("TypeScript", false, true)]
    [InlineData("TypeScript", true, true)]
    public async Task NodePreparation_ActualLoadedCapability_PreservesPreviewSkipAndConfigBeforeHelper(string language, bool skip, bool dryRun)
    {
        WorkloadEntry entry = await InstallAssociationStackAsync();
        string target = Path.Combine(_root, "owned-node");
        int calls = 0;
        using ServiceProvider provider = await CreateNodePreparationServicesAsync((path, token) =>
        {
            calls++;
            path.Should().Be(target);
            token.ThrowIfCancellationRequested();
            File.Exists(Path.Combine(path, "package.json")).Should().BeTrue();
            File.Exists(Path.Combine(path, "host.json")).Should().BeTrue();
            using var configuration = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(path, ".func", "config.json")));
            configuration.RootElement.GetProperty("stack").GetProperty("runtime").GetString().Should().Be("node");
            configuration.RootElement.GetProperty("stack").GetProperty("language").GetString().Should().Be(language);
            return Task.FromResult((0, "owned deterministic stderr"));
        });
        using IServiceScope scope = provider.CreateScope();
        var registration = scope.ServiceProvider.GetServices<WorkloadNativeTemplatePolicyRegistration>().Single(value => value.Policy.Stack == "node");
        AssemblyLoadContext.GetLoadContext(registration.Policy.GetType().Assembly).Should().BeSameAs(registration.Owner.LoadContext);
        registration.Policy.GetType().Assembly.GetName().Name.Should().Be("Azure.Functions.Cli.Workloads.Node");

        InitExecutionResult result = await scope.ServiceProvider.GetRequiredService<IInitExecutionRunner>().ExecuteAsync(
            NodePreparationRequest(target, language, skip, dryRun), Availability(entry), CancellationToken.None);

        result.Succeeded.Should().BeTrue(string.Join(" ", result.Diagnostics));
        result.Preparation!.Preview.Operations.Should().BeEmpty();
        result.Preparation.Preview.OwnedPlan.Should().NotBeNull();
        result.Preparation.Preview.OwnedPlan!.Skipped.Should().Be(skip);
        var capability = result.Preview!.ProjectPreparationCapture!.Owner.Capability;
        capability.GetType().Assembly.Should().BeSameAs(registration.Policy.GetType().Assembly);
        AssemblyLoadContext.GetLoadContext(capability.GetType().Assembly).Should().BeSameAs(registration.Owner.LoadContext);
        Type helperType = capability.GetType().Assembly.GetType("Azure.Functions.Cli.Workloads.Node.NodeNpmInstaller", throwOnError: true)!;
        object helper = scope.ServiceProvider.GetRequiredService(helperType);
        ((int)helper.GetType().GetProperty("Calls")!.GetValue(helper)!).Should().Be(dryRun || skip ? 0 : 1);
        calls.Should().Be(dryRun || skip ? 0 : 1);
        Directory.Exists(target).Should().Be(!dryRun);
        if (dryRun)
        {
            result.Preparation.Completion.Should().BeNull();
        }
        else
        {
            result.Invocation!.ConfiguredProjects.Should().Equal(target);
            result.Preparation.Completion!.OwnedCompletion.Should().NotBeNull();
            result.Preparation.Completion.OwnedCompletion!.TargetMayHaveChanged.Should().Be(!skip);
            result.Preparation.Completion.Diagnostics.Should().Contain(skip ? "Skipped npm installation." : "owned deterministic stderr");
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(7)]
    public async Task NodePreparation_NonzeroOwnedHelper_RetainsNativeAndConfigurationWithPartialLedger(int exitCode)
    {
        WorkloadEntry entry = await InstallAssociationStackAsync();
        using ServiceProvider provider = await CreateNodePreparationServicesAsync((_, _) => Task.FromResult((exitCode, "owned helper failure")));
        using IServiceScope scope = provider.CreateScope();
        string target = Path.Combine(_root, "partial-node");

        InitExecutionResult result = await scope.ServiceProvider.GetRequiredService<IInitExecutionRunner>().ExecuteAsync(
            NodePreparationRequest(target), Availability(entry), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Kind.Should().Be("partialPreparation");
        result.Invocation!.HasCommittedOutput.Should().BeTrue();
        result.Invocation.ConfiguredProjects.Should().Equal(target);
        result.Preparation!.Completion!.OwnedCompletion!.TargetMayHaveChanged.Should().BeTrue();
        result.Preparation.Completion.Diagnostics.Should().Contain("owned helper failure");
        File.Exists(Path.Combine(target, "package.json")).Should().BeTrue();
        File.Exists(Path.Combine(target, ".func", "config.json")).Should().BeTrue();
    }

    [Fact]
    public async Task NodePreparation_RequiredHelperCancellation_PropagatesAndPreservesCommittedOutput()
    {
        WorkloadEntry entry = await InstallAssociationStackAsync();
        using CancellationTokenSource cancellation = new();
        OperationCanceledException original = new("owned npm cancellation", cancellation.Token);
        using ServiceProvider provider = await CreateNodePreparationServicesAsync((_, _) =>
        {
            cancellation.Cancel();
            return Task.FromException<(int, string)>(original);
        });
        using IServiceScope scope = provider.CreateScope();
        string target = Path.Combine(_root, "canceled-node");

        Func<Task> run = () => scope.ServiceProvider.GetRequiredService<IInitExecutionRunner>().ExecuteAsync(
            NodePreparationRequest(target), Availability(entry), cancellation.Token);

        (await run.Should().ThrowAsync<OperationCanceledException>()).Which.Should().BeSameAs(original);
        File.Exists(Path.Combine(target, "package.json")).Should().BeTrue();
        File.Exists(Path.Combine(target, ".func", "config.json")).Should().BeTrue();
        _interaction.AllOutput.Should().Contain("were created before").And.Contain("No rollback");
    }

    [Theory]
    [InlineData("extra-fields")]
    [InlineData("formatting")]
    public async Task NodePreparation_ActualWriterReceiptMutation_RefusesBeforeOwnedHelper(string mutation)
    {
        WorkloadEntry entry = await InstallAssociationStackAsync();
        int calls = 0;
        using ServiceProvider provider = await CreateNodePreparationServicesAsync((_, _) =>
        {
            calls++;
            return Task.FromResult((0, ""));
        }, services => services.AddSingleton<IFileSystem>(new PreparationConfigurationSwapFiles(mutation)));
        using IServiceScope scope = provider.CreateScope();
        string target = Path.Combine(_root, "config-mutated-node");

        InitExecutionResult result = await scope.ServiceProvider.GetRequiredService<IInitExecutionRunner>().ExecuteAsync(
            NodePreparationRequest(target), Availability(entry), CancellationToken.None);

        result.Kind.Should().Be("partialInitialization");
        result.Succeeded.Should().BeFalse();
        result.Invocation!.HasCommittedOutput.Should().BeTrue();
        result.Invocation.ConfiguredProjects.Should().Equal(target);
        result.Preparation!.Completion.Should().BeNull();
        calls.Should().Be(0);
        File.Exists(Path.Combine(target, "package.json")).Should().BeTrue();
        File.Exists(Path.Combine(target, ".func", "config.json")).Should().BeTrue();
    }

    [Fact]
    public async Task NodePreparation_DirectCreateAndRequestFallback_CannotSkipRequiredOwnerPhase()
    {
        WorkloadEntry entry = await InstallAssociationStackAsync();
        int calls = 0;
        using ServiceProvider provider = await CreateNodePreparationServicesAsync((_, _) =>
        {
            calls++;
            return Task.FromResult((0, ""));
        });
        using IServiceScope scope = provider.CreateScope();
        var target = WorkingDirectory.FromExplicit(Path.Combine(_root, "unbound-node"));
        using var templater = await scope.ServiceProvider.GetRequiredService<ITemplaterFactory>().CreateProjectAsync(
            new(target), Availability(entry), provider.GetRequiredService<TemplateProjectSupport>(), CancellationToken.None);
        var selected = (await templater.ResolveGroupAsync("Azure.Functions.Node.Basic.JavaScript", TemplateType.Project, CancellationToken.None)).Group!.Single();

        var direct = await selected.InvokeAsync(new(target, new Dictionary<string, string?> { ["SkipNpmInstall"] = "true" }, "Owned"), CancellationToken.None);
        var fallback = Substitute.For<INativeProjectPreparation>();
        var result = await scope.ServiceProvider.GetRequiredService<IInitExecutionRunner>().ExecuteAsync(
            NodePreparationRequest(target.Info.FullName) with { ProjectPreparation = fallback }, Availability(entry), CancellationToken.None);

        direct.Status.Should().Be(TemplateInvocationStatus.InvalidTemplate);
        direct.Diagnostic.Should().Contain("owner-bound project preparation");
        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle().Which.Should().Contain("cannot be replaced");
        fallback.ReceivedCalls().Should().BeEmpty();
        calls.Should().Be(0);
        Directory.Exists(target.Info.FullName).Should().BeFalse();
    }

    private static InitExecutionRequest NodePreparationRequest(string target, string language = "JavaScript", bool skip = false, bool dryRun = false)
        => new(WorkingDirectory.FromExplicit(target), "basic", "OwnedNode", "node", language, false, true, dryRun, true,
            ["--skip-npm-install", skip ? "true" : "false"], new HashSet<string>());

    private Task<ServiceProvider> CreateNodePreparationServicesAsync(
        Func<string, CancellationToken, Task<(int ExitCode, string Stderr)>> run, Action<IServiceCollection>? configure = null,
        IBasicCompanionCatalog? catalog = null)
        => CreateServicesAsync(services =>
        {
            ConfigureNodePreparationHelper(services, run);
            services.AddScoped<NativeTemplateWorkflowRegistry>();
            configure?.Invoke(services);
        }, nativeCatalog: catalog);

    private object ConfigureNodePreparationHelper(
        IServiceCollection services, Func<string, CancellationToken, Task<(int ExitCode, string Stderr)>> run)
    {
        Type helperType = services.Single(descriptor => descriptor.ServiceType.FullName
            == "Azure.Functions.Cli.Workloads.Node.NodeNpmInstaller").ServiceType;
        var owner = AssemblyLoadContext.GetLoadContext(helperType.Assembly)!;
        var testAssembly = owner.LoadFromAssemblyPath(Path.Combine(AppContext.BaseDirectory, "Azure.Functions.Cli.Workloads.Node.Tests.dll"));
        Type substituteType = testAssembly.GetType("Azure.Functions.Cli.Workloads.Node.Tests.LoadedNodeNpmInstaller", throwOnError: true)!;
        object helper = Activator.CreateInstance(substituteType, run)!;
        helper.GetType().BaseType.Should().Be(helperType);
        helperType.IsInstanceOfType(helper).Should().BeTrue();
        AssemblyLoadContext.GetLoadContext(helper.GetType().Assembly).Should().BeSameAs(owner);
        helper.GetType().Assembly.Should().NotBeSameAs(helperType.Assembly);
        services.AddSingleton(helperType, helper);
        return helper;
    }
}
