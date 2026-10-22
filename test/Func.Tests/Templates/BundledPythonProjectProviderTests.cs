// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Python;
using Azure.Functions.Cli.Workloads.Python;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledPythonTemplateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PythonProjectProvider_ActualComposerAndInitRunner_QualifiesBasicWithoutPipOrLegacyInitializer(bool noBundles)
    {
        await InstallAsync();
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "production-basic"));
        var invocation = new InitExecutionRequest(output, "basic", "QualifiedPython", "python", "py",
            false, true, true, false, noBundles ? ["--no-bundles", "true"] : [], new HashSet<string>());
        var preview = await RunPythonProjectWorkflowAsync(invocation);
        preview.Succeeded.Should().BeTrue(string.Join(" ", preview.Diagnostics));
        Directory.Exists(output.Info.FullName).Should().BeFalse();

        var created = await RunPythonProjectWorkflowAsync(invocation with { DryRun = false });

        created.Succeeded.Should().BeTrue(string.Join(" ", created.Diagnostics));
        File.Exists(Path.Combine(output.Info.FullName, "function_app.py")).Should().BeTrue();
        File.Exists(Path.Combine(output.Info.FullName, "requirements.txt")).Should().BeTrue();
        File.ReadAllText(Path.Combine(output.Info.FullName, ".func", "config.json")).Should().Contain("\"python\"", "\"Python\"");
        Directory.Exists(Path.Combine(output.Info.FullName, ".venv")).Should().BeFalse();
        Directory.Exists(Path.Combine(output.Info.FullName, "site-packages")).Should().BeFalse();
        if (noBundles)
        {
            File.ReadAllText(Path.Combine(output.Info.FullName, "host.json")).Should().NotContain("extensionBundle");
        }
        else
        {
            File.ReadAllText(Path.Combine(output.Info.FullName, "host.json")).Should().Contain("Microsoft.Azure.Functions.ExtensionBundle");
        }
    }

    [Fact]
    public async Task PythonProjectProvider_ChangedOwnedRequirements_RefusesBeforeNativeCreation()
    {
        using (var archive = System.IO.Compression.ZipFile.Open(_archive, System.IO.Compression.ZipArchiveMode.Update))
        {
            archive.GetEntry("content/project/requirements.txt")!.Delete();
            using StreamWriter writer = new(archive.CreateEntry("content/project/requirements.txt").Open());
            writer.Write("changed dependency contract");
        }
        await InstallAsync();
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "changed-basic"));

        var result = await RunPythonProjectWorkflowAsync(new(output, "basic", "QualifiedPython", "python", "py",
            false, true, false, false, [], new HashSet<string>()));

        result.Succeeded.Should().BeFalse();
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    private async Task<InitExecutionResult> RunPythonProjectWorkflowAsync(InitExecutionRequest request)
    {
        var catalog = new InstalledProjectStackCatalog([new PythonProjectStack()]);
        var availability = Substitute.For<INativeTemplateAvailability>();
        availability.CaptureAsync(Arg.Any<CancellationToken>())
            .Returns(new TemplateConstraintEvaluationSnapshot([new("python-stack", NuGetVersion.Parse("1.0.0"), ["python"], true)]));
        var factory = Substitute.For<ITemplaterFactory>();
        factory.CreateProjectAsync(Arg.Any<TemplateEngineContext>(), Arg.Any<TemplateConstraintEvaluationSnapshot>(),
            Arg.Any<TemplateProjectSupport>(), Arg.Any<CancellationToken>(), Arg.Any<Action<TemplateStoreWaitReason>?>())
            .Returns(call => Task.FromResult(TemplateFixtures.CreateTemplater(
                new(call.ArgAt<TemplateEngineContext>(0), Hive, call.ArgAt<TemplateConstraintEvaluationSnapshot>(1)),
                projectSupport: call.ArgAt<TemplateProjectSupport>(2))));
        ServiceCollection services = new();
        services.AddSingleton<IInteractionService>(Substitute.For<IInteractionService>());
        services.AddSingleton<IFileSystem, PhysicalFileSystem>();
        services.AddSingleton(factory);
        services.AddSingleton(availability);
        services.AddSingleton(catalog);
        services.AddSingleton<Func<InstalledProjectStackCatalog>>(_ => () => catalog);
        services.AddSingleton<ITemplateArgumentParser, TemplateArgumentParser>();
        services.AddSingleton<TemplateExecutionInteraction>();
        services.AddSingleton<InitExecutionRenderer>();
        services.AddSingleton<NewExecutionRenderer>();
        services.AddSingleton<InitExecutionTargetInspector>();
        services.AddSingleton<TemplateProjectConfigurationPlanner>();
        services.AddSingleton<TemplateProjectConfigurationWriter>();
        services.AddSingleton<ILocalSettingsProvider, LocalSettingsProvider>();
        var projects = Substitute.For<IFunctionsProjectResolver>();
        services.AddSingleton(projects);
        services.AddScoped<PythonTemplateSourceReader>();
        services.AddScoped<INativeProjectWorkflowProvider, PythonProjectWorkflowProvider>();
        services.AddNativeTemplateWorkflowComposition();
        services.AddSingleton(Substitute.For<INewExecutionRunner>());
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var selectedProvider = scope.ServiceProvider.GetRequiredService<INativeProjectWorkflowProvider>();
        selectedProvider.Preparation.Should().BeNull();

        var result = await scope.ServiceProvider.GetRequiredService<NativeTemplateWorkflowComposer>().ExecuteProjectAsync(request, CancellationToken.None);

        projects.ReceivedCalls().Should().BeEmpty("new project generation must not dispatch to legacy adoption or a second initializer");
        await availability.Received(1).CaptureAsync(Arg.Any<CancellationToken>());
        await factory.Received(1).CreateProjectAsync(Arg.Any<TemplateEngineContext>(), Arg.Any<TemplateConstraintEvaluationSnapshot>(),
            Arg.Any<TemplateProjectSupport>(), Arg.Any<CancellationToken>(), Arg.Any<Action<TemplateStoreWaitReason>?>());
        return result;
    }
}
