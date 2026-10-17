// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.DotNet;
using Azure.Functions.Cli.Tests.Console;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared = Azure.Functions.Cli.Templates.DotNet.Shared;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class DotNetTemplateWorkflowHostFactoryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("func-dotnet-workflow-boot-").FullName;
    private readonly TestInteractionService _interaction = new();

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Windows can retain a mapped collectible workload assembly until the test process releases it.
        }
        catch (UnauthorizedAccessException)
        {
            // The native loader can keep its read-only workload assembly mapped until process shutdown.
        }
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("csharp", "C#", ".csproj", ".cs")]
    [InlineData("fsharp", "F#", ".fsproj", ".fs")]
    public async Task ActualInstalledStack_ExplicitHostFactory_ComposesOwnedProviderForBothRunnersWithoutRequestCapabilities(
        string alias, string language, string projectExtension, string sourceExtension)
    {
        WorkloadPathsOptions paths = new(Path.Combine(_root, "workloads"));
        CliConfigurationPathsOptions configurationPaths = new(Path.Combine(_root, "cli"));
        HostApplicationBuilder bootstrap = CliHostFactory.CreateBuilder(_interaction);
        bootstrap.Services.AddSingleton(paths);
        bootstrap.Services.AddSingleton<IWorkloadPaths>(paths);
        bootstrap.Services.AddSingleton(configurationPaths);
        bootstrap.Services.AddSingleton<ICliConfigurationProvider>(provider =>
            new CliConfigurationProvider(provider.GetRequiredService<ILocalSettingsProvider>(), configurationPaths));
        string archive = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "bundled-dotnet-artifacts"), "*.nupkg").Single();
        using (IHost installation = bootstrap.Build())
        {
            await installation.Services.GetRequiredService<IWorkloadInstaller>().InstallFromPackageAsync(archive, cancellationToken: CancellationToken.None);
        }
        DirectoryInfo repository = new(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(repository.FullName, "NuGet.Config")))
        {
            repository = repository.Parent ?? throw new InvalidOperationException("Repository NuGet.Config not found.");
        }
        File.Copy(Path.Combine(repository.FullName, "NuGet.Config"), Path.Combine(_root, "NuGet.Config"));
        using NativeTemplateWorkflowHost host = await new NativeTemplateWorkflowHostFactory().CreateAsync(
            _interaction, paths, configurationPaths, services =>
            {
                services.AddSingleton<Shared.IDotnetPathResolver, Shared.DotnetPathResolver>();
                services.AddSingleton<Shared.IDotnetCliRunner, Shared.DotnetCliRunner>();
                services.AddSingleton<Shared.FSharpProjectFiles>();
                services.AddScoped<DotNetTemplateWorkflowComposer>();
                services.AddScoped(provider => provider.GetRequiredService<DotNetTemplateWorkflowComposer>().Create());
                services.AddScoped<INativeItemWorkflowProvider>(provider => provider.GetRequiredService<DotNetTemplateWorkflowProvider>());
                services.AddScoped<INativeProjectWorkflowProvider>(provider => provider.GetRequiredService<DotNetTemplateWorkflowProvider>());
            }, enableBundleBindingConstraints: true, CancellationToken.None);
        string target = Path.Combine(_root, alias);
        InitExecutionRequest project = new(WorkingDirectory.FromExplicit(target), "func", "BootApp", "dotnet", alias,
            false, true, false, false, [], new HashSet<string>());
        project.ProjectPreparation.Should().BeNull();
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(3));
        using (NativeTemplateWorkflowInvocation invocation = host.CreateInvocation())
        {
            InitExecutionResult preview = await invocation.Composer.ExecuteProjectAsync(project with { DryRun = true }, timeout.Token);
            preview.Succeeded.Should().BeTrue(string.Join(" ", preview.Diagnostics) + preview.Preview?.Diagnostic);
            Directory.Exists(target).Should().BeFalse();
            InitExecutionResult initialized = await invocation.Composer.ExecuteProjectAsync(project, timeout.Token);
            initialized.Succeeded.Should().BeTrue(string.Join(" ", initialized.Diagnostics) + initialized.Invocation?.Diagnostic);
            initialized.SelectedTemplate!.Language.Should().Be(language);
        }
        NewExecutionRequest item = new(WorkingDirectory.FromExplicit(target), "http", "Greeting", null, false, true,
            false, false, false, ["--AccessRights", "Anonymous"], new HashSet<string>());
        item.ItemCompatibility.Should().BeNull();
        item.SelectedItemEffects.Should().BeNull();
        using (NativeTemplateWorkflowInvocation invocation = host.CreateInvocation())
        {
            NewExecutionResult created = await invocation.Composer.ExecuteItemAsync(item, timeout.Token);
            created.Succeeded.Should().BeTrue(string.Join(" ", created.Diagnostics) + created.Invocation?.Diagnostic);
            created.Invocation!.Effects!.Succeeded.Should().BeTrue();
        }
        File.Exists(Path.Combine(target, "Greeting" + sourceExtension)).Should().BeTrue();
        using var configuration = JsonDocument.Parse(File.ReadAllText(Path.Combine(target, ".func", "config.json")));
        configuration.RootElement.GetProperty("stack").GetProperty("language").GetString().Should().Be(language);
        string projectFile = Directory.GetFiles(target, "*" + projectExtension).Single();
        await new Shared.DotnetCliRunner(new Shared.DotnetPathResolver()).RunAsync(
            ["build", projectFile, "--no-restore", "-m:1", "-v:minimal", "-p:NuGetAudit=false"], target, timeout.Token);
        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(target, "bin", "Debug", "net10.0", "functions.metadata")));
        metadata.RootElement.EnumerateArray().Select(function => function.GetProperty("name").GetString()).Should().Contain("Greeting");
    }
}
