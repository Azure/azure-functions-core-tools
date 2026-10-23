// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using System.Text.Json;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Tests.Console;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared = Azure.Functions.Cli.Templates.DotNet.Shared;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class DotNetNativeFullEntryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("func-dotnet-full-entry-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Windows can retain a mapped collectible workload assembly until process shutdown.
        }
        catch (UnauthorizedAccessException)
        {
            // The installed read-only workload assembly can remain mapped after its host is disposed.
        }
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("csharp", "C#", ".csproj", ".cs")]
    [InlineData("fsharp", "F#", ".fsproj", ".fs")]
    public async Task StagedFullEntry_OriginalPublicArguments_InitializesAndCreatesNativeHttpWithRealDependenciesAndBuildMetadata(
        string alias, string language, string projectExtension, string sourceExtension)
    {
        WorkloadPathsOptions workloads = new(Path.Combine(_root, "workloads"));
        CliConfigurationPathsOptions configuration = new(Path.Combine(_root, "cli"));
        TestInteractionService installationOutput = new();
        HostApplicationBuilder bootstrap = CliHostFactory.CreateBuilder(installationOutput);
        bootstrap.Services.AddSingleton(workloads);
        bootstrap.Services.AddSingleton<IWorkloadPaths>(workloads);
        bootstrap.Services.AddSingleton(configuration);
        bootstrap.Services.AddSingleton<ICliConfigurationProvider>(provider =>
            new CliConfigurationProvider(provider.GetRequiredService<ILocalSettingsProvider>(), configuration));
        string archive = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "bundled-dotnet-artifacts"), "*.nupkg").Single();
        using (IHost installer = bootstrap.Build())
        {
            await installer.Services.GetRequiredService<IWorkloadInstaller>().InstallFromPackageAsync(
                archive, cancellationToken: CancellationToken.None);
        }
        DirectoryInfo repository = new(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(repository.FullName, "NuGet.Config")))
        {
            repository = repository.Parent ?? throw new InvalidOperationException("Repository NuGet.Config not found.");
        }
        File.Copy(Path.Combine(repository.FullName, "NuGet.Config"), Path.Combine(_root, "NuGet.Config"));
        string target = Path.Combine(_root, alias);
        TestInteractionService initialized = new();
        NativeCliEntryPoint entry = new();
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(3));

        int initExit = await entry.ExecuteAsync(["init", target, "-s", "dotnet", "-l", alias],
            initialized, workloads, configuration, timeout.Token, versionCheck: _ => Task.FromResult<string?>(null));

        initExit.Should().Be(0, initialized.AllOutput);
        initialized.AllOutput.Should().NotContain("INPUT:").And.NotContain("SELECT:");
        using var cliConfiguration = JsonDocument.Parse(File.ReadAllText(Path.Combine(target, ".func", "config.json")));
        cliConfiguration.RootElement.GetProperty("stack").GetProperty("runtime").GetString().Should().Be("dotnet");
        cliConfiguration.RootElement.GetProperty("stack").GetProperty("language").GetString().Should().Be(language);
        string project = Directory.GetFiles(target, "*" + projectExtension).Single();
        File.ReadAllText(project).Should().Contain("<TargetFramework>net10.0</TargetFramework>");
        TestInteractionService created = new();
        int advisoryCalls = 0;
        int itemExit = await entry.ExecuteAsync(
            ["new", target, "-t", "http", "-n", "Greeting", "--AccessRights", "Anonymous", "--non-interactive", "--format", "json"],
            created, workloads, configuration, timeout.Token, versionCheck: _ =>
            {
                advisoryCalls++;
                return Task.FromResult<string?>("9999.0.0");
            });

        itemExit.Should().Be(0, created.AllOutput);
        advisoryCalls.Should().Be(0);
        created.AllOutput.Should().NotContain("INPUT:").And.NotContain("SELECT:").And.NotContain("9999.0.0");
        using var result = JsonDocument.Parse(created.Lines.Single(line => line.StartsWith("JSON: ", StringComparison.Ordinal))["JSON: ".Length..]);
        JsonElement json = result.RootElement;
        json.GetProperty("kind").GetString().Should().Be("created");
        json.GetProperty("succeeded").GetBoolean().Should().BeTrue();
        json.GetProperty("stack").GetString().Should().Be("dotnet");
        json.GetProperty("language").GetString().Should().Be(language);
        json.GetProperty("executionDirectory").GetString().Should().Be(target);
        json.GetProperty("outputDirectory").GetString().Should().Be(target);
        JsonElement arguments = json.GetProperty("argumentResults").EnumerateArray().Single().GetProperty("valuesByCanonicalName");
        arguments.GetProperty("AccessRights").GetString().Should().Be("Anonymous");
        arguments.GetProperty("namespace").GetString().Should().Be(alias);
        File.ReadAllText(Path.Combine(target, "Greeting" + sourceExtension)).Should().Contain("namespace " + alias, "Greeting");
        Shared.DotnetCliRunner runner = new(new Shared.DotnetPathResolver());
        StringBuilder evaluated = new();
        await runner.RunStreamingAsync(["msbuild", project, "-nologo", "-getProperty:TargetFramework", "-getItem:Compile"], target,
            line => evaluated.AppendLine(line), null, timeout.Token);
        using var items = JsonDocument.Parse(evaluated.ToString());
        items.RootElement.GetProperty("Properties").GetProperty("TargetFramework").GetString().Should().Be("net10.0");
        string?[] compile = [.. items.RootElement.GetProperty("Items").GetProperty("Compile").EnumerateArray()
            .Select(item => item.GetProperty("Identity").GetString())];
        compile.Should().Contain("Greeting" + sourceExtension);
        if (language == "F#")
        {
            compile.Should().Equal("Greeting.fs", "Program.fs");
        }
        await runner.RunAsync(["build", project, "--no-restore", "-m:1", "-v:minimal", "-p:NuGetAudit=false"], target, timeout.Token);
        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(target, "bin", "Debug", "net10.0", "functions.metadata")));
        metadata.RootElement.EnumerateArray().Select(function => function.GetProperty("name").GetString()).Should().Contain("Greeting");
    }
}
