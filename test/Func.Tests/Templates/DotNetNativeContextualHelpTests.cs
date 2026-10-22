// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.DotNet;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Tests.Console;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Shared = Azure.Functions.Cli.Templates.DotNet.Shared;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class DotNetNativeContextualHelpTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("func-dotnet-context-help-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Windows can keep the installed collectible workload assembly mapped until test process shutdown.
        }
    }

    [Theory]
    [InlineData("C#", ".csproj")]
    [InlineData("F#", ".fsproj")]
    public async Task SelectedFullEntryHelp_UsesEvaluatedProjectNamespace_WhileGenericHelpDoesNotEvaluateSdk(string language, string extension)
    {
        WorkloadPathsOptions paths = new(Path.Combine(_root, "workloads"));
        CliConfigurationPathsOptions configuration = new(Path.Combine(_root, "cli"));
        TestInteractionService installationOutput = new();
        HostApplicationBuilder bootstrap = CliHostFactory.CreateBuilder(installationOutput);
        bootstrap.Services.AddSingleton(paths);
        bootstrap.Services.AddSingleton<IWorkloadPaths>(paths);
        bootstrap.Services.AddSingleton(configuration);
        bootstrap.Services.AddSingleton<ICliConfigurationProvider>(provider =>
            new CliConfigurationProvider(provider.GetRequiredService<ILocalSettingsProvider>(), configuration));
        string archive = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "bundled-dotnet-artifacts"), "*.nupkg").Single();
        using (IHost installer = bootstrap.Build())
        {
            await installer.Services.GetRequiredService<IWorkloadInstaller>().InstallFromPackageAsync(archive, cancellationToken: CancellationToken.None);
        }
        string projectRoot = Path.Combine(_root, "selected");
        Directory.CreateDirectory(projectRoot);
        File.WriteAllText(Path.Combine(projectRoot, "host.json"), """{"version":"2.0"}""");
        File.WriteAllText(Path.Combine(projectRoot, "local.settings.json"),
            """{"IsEncrypted":false,"Values":{"FUNCTIONS_WORKER_RUNTIME":"dotnet-isolated"}}""");
        File.WriteAllText(Path.Combine(projectRoot, "properties.props"),
            "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework><RootNamespace>ContextHelp.Functions</RootNamespace></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(projectRoot, "Selected" + extension),
            """<Project Sdk="Microsoft.NET.Sdk"><Import Project="properties.props" /></Project>""");
        Directory.CreateDirectory(Path.Combine(projectRoot, ".func"));
        File.WriteAllText(Path.Combine(projectRoot, ".func", "config.json"),
            System.Text.Json.JsonSerializer.Serialize(new { stack = new { runtime = "dotnet", language } }));
        HelpRunner runner = new();
        NativeCliEntryPoint entry = new();
        TestInteractionService selected = new();

        int exit = await entry.ExecuteAsync(["new", projectRoot, "-t", "http", "--help"], selected, paths, configuration,
            CancellationToken.None, services => services.AddSingleton<Shared.IDotnetCliRunner>(runner),
            _ => Task.FromResult<string?>(null));

        exit.Should().Be(0, selected.AllOutput);
        selected.AllOutput.Should().Contain("--namespace").And.Contain("ContextHelp.Functions").And.NotContain("Company.Function");
        runner.Evaluations.Should().BeGreaterThan(0);
        int evaluated = runner.Evaluations;
        TestInteractionService generic = new();
        int genericExit = await entry.ExecuteAsync(["new", projectRoot, "--help"], generic, paths, configuration,
            CancellationToken.None, services => services.AddSingleton<Shared.IDotnetCliRunner>(runner),
            _ => Task.FromResult<string?>(null));
        genericExit.Should().Be(0, generic.AllOutput);
        runner.Evaluations.Should().Be(evaluated);
        Directory.GetFiles(projectRoot, language == "F#" ? "*.fs" : "*.cs").Should().BeEmpty();
        Directory.Exists(Path.Combine(projectRoot, "obj")).Should().BeFalse();
        Directory.Exists(Path.Combine(projectRoot, "bin")).Should().BeFalse();
    }

    [Theory]
    [InlineData("unqualified")]
    [InlineData("changed")]
    [InlineData("unavailableProject")]
    public async Task DescribeHelp_UnqualifiedSourceOrProject_RemovesUnevaluatedDefaultWithoutSdkWork(string failure)
    {
        IFunctionsProjectResolver projects = Substitute.For<IFunctionsProjectResolver>();
        Shared.IDotnetCliRunner runner = Substitute.For<Shared.IDotnetCliRunner>();
        DotNetTemplateWorkflowProvider provider = new DotNetTemplateWorkflowComposer(projects, new PhysicalFileSystem(),
            runner, new Shared.FSharpProjectFiles()).Create();
        TemplateParameterDefinition parameter = new("namespace", "namespace", null, "Original namespace", "string",
            [], "Company.Function", false, false, false);
        TemplateCatalogEntry entry = new("Azure.Function.CSharp.Isolated.HttpTrigger.3.x", "HTTP", null, TemplateType.Item,
            ["http"], null, "C#", 0, new("Azure.Functions.Templates.DotNet", "1.0.0"), false, [parameter], null)
        {
            ConstraintEvaluation = new(TemplateConstraintEvaluationStatus.Eligible, []),
        };
        string fingerprint = failure == "unqualified" ? new string('0', 64)
            : "65B1EC29BE5F6176FF0C8ECC1755683970E3D2F69089A709AFB3E593924F6CD7";
        ISelectedTemplateInvoker invoker = Substitute.For<ISelectedTemplateInvoker>();
        invoker.InvokeAsync(Arg.Any<TemplateInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns<TemplateInvocationResult>(_ => throw new InvalidOperationException("Help must not invoke the selected item."));
        invoker.ValidateConfigurationAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TemplateInvocationResult?>(failure == "changed"
                ? new(TemplateInvocationStatus.TemplateChanged, [], "changed") : null));
        ResolvedTemplate selected = new(entry, invoker, configurationSha256: fingerprint);
        projects.ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(ProjectResolutionResults.NotResolved("The selected project is unavailable."));
        var directory = WorkingDirectory.FromExplicit(_root);

        NativeItemHelpProjection result = await provider.DescribeHelpAsync(new(selected, new(directory, new(directory, "dotnet", "C#"))),
            CancellationToken.None);

        result.Diagnostics.Should().ContainSingle();
        result.Parameters.Should().ContainSingle().Which.Should().Be(parameter with
        {
            DefaultValue = null,
            Description = "The contextual default is unevaluated. Supply an explicit value or repair the selected .NET project and retry help.",
        });
        if (failure != "unavailableProject")
        {
            projects.ReceivedCalls().Should().BeEmpty();
        }
        runner.ReceivedCalls().Should().BeEmpty();
    }

    private sealed class HelpRunner : Shared.IDotnetCliRunner
    {
        private readonly Shared.DotnetCliRunner _real = new(new Shared.DotnetPathResolver());

        public int Evaluations { get; private set; }

        public Task RunAsync(IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Help must not execute dependency or mutation commands.");

        public Task RunStreamingAsync(IReadOnlyList<string> arguments, string? workingDirectory, Action<string>? onOutputLine,
            Action<string>? onErrorLine, CancellationToken cancellationToken)
        {
            arguments[0].Should().Be("msbuild");
            Evaluations++;
            return _real.RunStreamingAsync(arguments, workingDirectory, onOutputLine, onErrorLine, cancellationToken);
        }
    }
}
