// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Python;
using Azure.Functions.Cli.Templates.Python.Shared;
using Azure.Functions.Cli.Workloads.Python;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NuGet.Versioning;
using HostPythonAppendExecutor = Azure.Functions.Cli.Templates.Python.Shared.PythonTemplateAppendExecutor;
using HostPythonAppendInterface = Azure.Functions.Cli.Templates.Python.Shared.IPythonTemplateAppendExecutor;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledPythonTemplateTests
{
    [Theory]
    [InlineData("HttpTrigger-Python")]
    [InlineData("DurableFunctionsEntityTrigger-Python")]
    [InlineData("DurableFunctionsOrchestration-Python")]
    [InlineData("EventGridTrigger-Python")]
    [InlineData("ServiceBusTopicTrigger-Python")]
    public async Task PythonWorkflow_RealComposerRunnerAndProductionProvider_CreateThenAppendWithoutRequestCapabilities(string id)
    {
        await InstallAsync();
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "workflow-app"));
        var created = await RunPythonWorkflowAsync(new(output, id, null, null, false, true, false, false, false, [], new HashSet<string>()));

        created.Succeeded.Should().BeTrue(string.Join(" ", created.Diagnostics));
        created.ItemName!.Origin.Should().Be("authoredDefault");
        var app = Path.Combine(output.Info.FullName, "function_app.py");
        File.Exists(app).Should().BeTrue();
        File.Exists(Path.Combine(output.Info.FullName, "function_app.py.py")).Should().BeFalse();
        File.AppendAllText(app, "\n# user-owned\nimport user_dependency\ndef existing_helper():\n    return 'kept'\n");
        var before = File.ReadAllBytes(app);
        var request = new NewExecutionRequest(output, id, "AddedFunction", null, false, true, true, false, false, [], new HashSet<string>());
        request.ItemCompatibility.Should().BeNull();
        request.SelectedItemEffects.Should().BeNull();

        var preview = await RunPythonWorkflowAsync(request);
        File.ReadAllBytes(app).Should().Equal(before);
        var appended = await RunPythonWorkflowAsync(request with { DryRun = false });

        preview.Succeeded.Should().BeTrue(string.Join(" ", preview.Diagnostics));
        preview.Invocation!.Effects!.Kind.Should().Be("previewed");
        appended.Succeeded.Should().BeTrue(string.Join(" ", appended.Diagnostics));
        appended.Invocation!.Effects!.Kind.Should().Be("applied");
        appended.ItemName!.Origin.Should().Be("explicit");
        File.ReadAllBytes(app).Take(before.Length).Should().Equal(before);
        File.ReadAllText(app).Should().Contain("AddedFunction", "def existing_helper():");
        Directory.GetFiles(output.Info.FullName).Should().ContainSingle().Which.Should().Be(app);
    }

    [Theory]
    [InlineData("HttpTrigger-Python", "bad-name", null, "invalidLegacyInput")]
    [InlineData("TimerTrigger-Python", "Greeting", "not-a-cron", "invalidLegacyInput")]
    [InlineData("Azure.Functions.Python.HttpTrigger.Append", "Greeting", null, "internalTemplate")]
    [InlineData("Unknown.Python.Item", "Greeting", null, "NotFound")]
    public async Task PythonWorkflow_DefaultRegexHelperOrUnknownIdentity_RefusesThroughActualRunner(
        string id, string name, string? schedule, string expected)
    {
        await InstallAsync();
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "workflow-refusal"));
        Directory.CreateDirectory(output.Info.FullName);
        var app = Path.Combine(output.Info.FullName, "function_app.py");
        File.WriteAllText(app, "user-owned\n");
        string[] tokens = schedule is null ? [] : ["--timer-trigger-schedule", schedule];

        var result = await RunPythonWorkflowAsync(new(output, id, name, null, false, true, false, false, false, tokens, new HashSet<string>()));

        result.Kind.Should().Be(expected, string.Join(" ", result.Diagnostics));
        result.Succeeded.Should().BeFalse();
        File.ReadAllText(app).Should().Be("user-owned\n");
        Directory.GetFiles(output.Info.FullName).Should().ContainSingle();
    }

    [Theory]
    [InlineData("helperBody")]
    [InlineData("promptBytes")]
    [InlineData("unknownProperty")]
    public async Task PythonWorkflow_ChangedRealOwnedArchive_RefusesInsteadOfFallingBackToPlainNativeCreate(string corruption)
    {
        CorruptPythonProviderArchive(corruption);
        await InstallAsync();
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "workflow-stale"));
        Directory.CreateDirectory(output.Info.FullName);
        var app = Path.Combine(output.Info.FullName, "function_app.py");
        File.WriteAllText(app, "user-owned\n");

        var result = await RunPythonWorkflowAsync(new(output, "HttpTrigger-Python", "Greeting", null,
            false, true, false, false, false, [], new HashSet<string>()));

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().NotBeEmpty();
        File.ReadAllText(app).Should().Be("user-owned\n");
    }

    [Fact]
    public async Task PythonWorkflow_UnconfirmedProductionCapability_DoesNotAppend()
    {
        await InstallAsync();
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "workflow-decline"));
        Directory.CreateDirectory(output.Info.FullName);
        var app = Path.Combine(output.Info.FullName, "function_app.py");
        File.WriteAllText(app, "user-owned\n");

        var result = await RunPythonWorkflowAsync(new(output, "HttpTrigger-Python", "Greeting", null,
            false, true, false, false, false, [], new HashSet<string>()), confirmed: false);

        result.Succeeded.Should().BeFalse();
        result.Invocation!.Effects!.Kind.Should().Be("declined");
        File.ReadAllText(app).Should().Be("user-owned\n");
    }

    [Fact]
    public async Task PythonWorkflow_ListingUsesUserInventory_NotInternalHelpers()
    {
        await InstallAsync();
        var result = await RunPythonWorkflowAsync(new(WorkingDirectory.FromExplicit(Path.Combine(_root, "listing")),
            null, null, null, false, true, false, true, false, [], new HashSet<string>()));

        result.Succeeded.Should().BeTrue();
        result.Templates.Should().HaveCount(16).And.OnlyContain(entry => entry.DeclaredInternalRole == null && !entry.IsHidden);
        Directory.Exists(Path.Combine(_root, "listing")).Should().BeFalse();
    }

    private async Task<NewExecutionResult> RunPythonWorkflowAsync(NewExecutionRequest request, bool confirmed = true, string channel = "stable")
    {
        var bundle = await ReadBundleAsync(channel);
        using var templater = CreateTemplater(bundle);
        var interaction = Substitute.For<IInteractionService>();
        var contexts = Substitute.For<INewExecutionContextResolver>();
        contexts.ResolveAsync(Arg.Any<NewExecutionRequest>(), false, Arg.Any<CancellationToken>())
            .Returns(new NewExecutionContextResult(templater.Context, null));
        var factory = Substitute.For<ITemplaterFactory>();
        factory.CreateAsync(Arg.Any<TemplateEngineContext>(), Arg.Any<TemplateConstraintEvaluationSnapshot>(),
            Arg.Any<CancellationToken>(), Arg.Any<Action<TemplateStoreWaitReason>?>()).Returns(templater);
        var availability = Substitute.For<INativeTemplateAvailability>();
        availability.CaptureAsync(Arg.Any<CancellationToken>())
            .Returns(new TemplateConstraintEvaluationSnapshot([new("python-stack", NuGetVersion.Parse("1.0.0"), ["python"], true)]));
        ServiceCollection services = new();
        services.AddSingleton<IInteractionService>(interaction);
        services.AddSingleton(contexts);
        services.AddSingleton(factory);
        services.AddSingleton(availability);
        services.AddSingleton(new InstalledProjectStackCatalog([new PythonProjectStack()]));
        services.AddSingleton<ITemplateArgumentParser, TemplateArgumentParser>();
        services.AddSingleton<TemplateExecutionInteraction>();
        services.AddSingleton<NewExecutionRenderer>();
        services.AddSingleton<InitExecutionRenderer>();
        services.AddScoped<PythonTemplateSourceReader>();
        services.AddScoped<PythonItemCompatibility>();
        services.AddScoped<HostPythonAppendInterface, HostPythonAppendExecutor>();
        services.AddSingleton<IFileSystem, PhysicalFileSystem>();
        services.AddScoped(provider => new PythonSelectedItemEffects(provider.GetRequiredService<IFileSystem>(),
            provider.GetRequiredService<PythonTemplateSourceReader>(), provider.GetRequiredService<HostPythonAppendInterface>(), confirmed));
        services.AddScoped<INativeItemWorkflowProvider, PythonItemWorkflowProvider>();
        services.AddNativeTemplateWorkflowComposition();
        services.AddSingleton(Substitute.For<IInitExecutionRunner>());
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<NativeTemplateWorkflowComposer>().ExecuteItemAsync(request, CancellationToken.None);

        await availability.Received(1).CaptureAsync(Arg.Any<CancellationToken>());
        await factory.Received(1).CreateAsync(templater.Context with { RequireCleanStore = request.DryRun || request.List },
            Arg.Any<TemplateConstraintEvaluationSnapshot>(),
            Arg.Any<CancellationToken>(), Arg.Any<Action<TemplateStoreWaitReason>?>());
        return result;
    }
}
