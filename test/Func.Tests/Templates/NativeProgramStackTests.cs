// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Tests.Commands.Setup;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class NativeProgramStackTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("func-native-program-").FullName;
    private bool _loadedRuntime;

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("Node", "node", "js", "JavaScript", "bundled-node-artifacts")]
    [InlineData("Node", "node", "ts", "TypeScript", "bundled-node-artifacts")]
    [InlineData("Python", "python", "py", "Python", "bundled-python-artifacts")]
    [InlineData("DotNet", "dotnet", "csharp", "C#", "bundled-dotnet-artifacts")]
    [InlineData("DotNet", "dotnet", "fsharp", "F#", "bundled-dotnet-artifacts")]
    [InlineData("Go", "go", "golang", "Go", "bundled-project-artifacts")]
    [InlineData("Java", "java", "Java", "Java", "bundled-project-artifacts")]
    [InlineData("PowerShell", "powershell", "pwsh", "PowerShell", "bundled-project-artifacts")]
    public async Task Program_DefaultEntry_UsesNativeInstalledProjectAndSupportedItemWorkflows(
        string package, string stack, string alias, string language, string artifacts)
    {
        WorkloadPathsOptions workloads = new(Path.Combine(_root, "workloads"));
        CliConfigurationPathsOptions configuration = new(Path.Combine(_root, "cli"));
        var builder = CliHostFactory.CreateBuilder(new TestInteractionService());
        builder.Services.AddSingleton(workloads);
        builder.Services.AddSingleton<IWorkloadPaths>(workloads);
        builder.Services.AddSingleton(configuration);
        using (var installer = builder.Build())
        {
            string archive = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, artifacts),
                $"Azure.Functions.Cli.Workloads.{package}.*.nupkg").Single();
            await installer.Services.GetRequiredService<IWorkloadInstaller>().InstallFromPackageAsync(archive, cancellationToken: CancellationToken.None);
            if (stack == "python")
            {
                string nativeArchive = Path.Combine(_root, "python-native.nupkg");
                using (var outer = new NuGet.Packaging.PackageArchiveReader(archive))
                using (var input = outer.GetStream("tools/any/templates/python.nupkg"))
                using (var output = File.Create(nativeArchive))
                {
                    input.CopyTo(output);
                }
                await installer.Services.GetRequiredService<IWorkloadInstaller>().InstallFromPackageAsync(
                    BundledPythonTemplateTests.BuildMetadataOnlyPythonBundle(_root, nativeArchive), cancellationToken: CancellationToken.None);
            }
        }
        if (stack == "dotnet")
        {
            DirectoryInfo repository = new(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(repository.FullName, "NuGet.Config")))
            {
                repository = repository.Parent ?? throw new InvalidOperationException("Repository NuGet.Config not found.");
            }
            File.Copy(Path.Combine(repository.FullName, "NuGet.Config"), Path.Combine(_root, "NuGet.Config"));
        }
        string project = Path.Combine(_root, "project");
        Directory.CreateDirectory(project);
        string[] options = stack switch
        {
            "node" => ["--skip-npm-install"],
            "go" => ["--skip-go-mod-tidy"],
            "powershell" => ["--runtime-version", "7.6"],
            _ => [],
        };
        string processHome = Path.Combine(_root, "process");
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(5));

        string[] bundleOptions = stack is "dotnet" or "python" ? [] : ["--no-bundles"];
        var initialized = await SetupProgramAdvisoryTests.RunCliAsync(processHome, configuration.Home, workloads.Home, project,
            ["init", project, "-s", stack, "-l", alias, "--name", "NativeProgramApp", .. bundleOptions, .. options], timeout.Token);

        initialized.ExitCode.Should().Be(0, initialized.StandardOutput + initialized.StandardError);
        initialized.StandardOutput.Should().Contain("Initialized project template").And.NotContain("Project initialized for");
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(project, ".func", "config.json")));
        config.RootElement.GetProperty("stack").GetProperty("runtime").GetString().Should().Be(stack);
        config.RootElement.GetProperty("stack").GetProperty("language").GetString().Should().Be(language);
        File.Exists(Path.Combine(project, "host.json")).Should().BeTrue();
        if (stack is not ("node" or "python" or "dotnet"))
        {
            string witness = stack switch { "go" => "main.go", "java" => "pom.xml", _ => "profile.ps1" };
            File.Exists(Path.Combine(project, witness)).Should().BeTrue();
            if (stack == "go")
            {
                File.ReadAllText(Path.Combine(project, "go.mod")).Should().StartWith("module nativeprogramapp");
                foreach (string directory in new[] { "go", "toolchain" })
                {
                    string target = Path.Combine(_root, directory);
                    var reserved = await SetupProgramAdvisoryTests.RunCliAsync(processHome, configuration.Home, workloads.Home, project,
                        ["init", target, "-s", "go", "--skip-go-mod-tidy", "--no-bundles"], timeout.Token);
                    reserved.ExitCode.Should().Be(0, reserved.StandardOutput + reserved.StandardError);
                    File.ReadAllText(Path.Combine(target, "go.mod")).Should().StartWith("module app");
                }
            }
            return;
        }

        string[] itemOptions = stack switch
        {
            "dotnet" => ["--AccessRights", "Anonymous"],
            "python" => ["--http-trigger-auth-level", "ANONYMOUS"],
            _ => ["--authlevel", "anonymous"],
        };
        string template = stack == "python" ? "HttpTrigger-Python" : "http";
        var created = await SetupProgramAdvisoryTests.RunCliAsync(processHome, configuration.Home, workloads.Home, project,
            ["new", project, "-t", template, "--name", "NativeGreeting", "--non-interactive", "--output=json", .. itemOptions], timeout.Token);
        created.ExitCode.Should().Be(0, created.StandardOutput + created.StandardError);
        created.StandardError.Should().BeEmpty();
        using var item = JsonDocument.Parse(created.StandardOutput);
        item.RootElement.GetProperty("kind").GetString().Should().Be("created");
        item.RootElement.GetProperty("selectedTemplate").GetProperty("identity").GetString().Should().NotBeNullOrWhiteSpace();
        JsonElement invocation = item.RootElement.GetProperty("invocation");
        if (stack == "python")
        {
            invocation.GetProperty("changes").GetArrayLength().Should().Be(0);
            invocation.GetProperty("effects").GetProperty("completed").EnumerateArray()
                .Should().Contain(effect => effect.GetProperty("kind").GetString() == "TemplateAppendFileEffect");
        }
        else
        {
            invocation.GetProperty("changes").GetArrayLength().Should().BeGreaterThan(0);
        }
        if (stack == "node")
        {
            string extension = language == "TypeScript" ? ".ts" : ".js";
            File.ReadAllText(Path.Combine(project, "src", "functions", "NativeGreeting" + extension)).Should().Contain("app.http('NativeGreeting'");
        }
        else if (stack == "python")
        {
            File.ReadAllText(Path.Combine(project, "function_app.py")).Should().Contain("def NativeGreeting(");
            Directory.GetFiles(project, "function.json", SearchOption.AllDirectories).Should().BeEmpty();
        }
        else
        {
            string extension = language == "F#" ? ".fs" : ".cs";
            File.Exists(Path.Combine(project, "NativeGreeting" + extension)).Should().BeTrue();
            if (language == "F#")
            {
                string fsproj = File.ReadAllText(Directory.GetFiles(project, "*.fsproj").Single());
                fsproj.IndexOf("NativeGreeting.fs", StringComparison.Ordinal).Should().BeLessThan(fsproj.IndexOf("Program.fs", StringComparison.Ordinal));
            }
            string configPath = Path.Combine(project, ".func", "config.json");
            var incomplete = JsonNode.Parse(File.ReadAllText(configPath))!;
            incomplete["stack"]!["runtime"] = "dotnet-isolated";
            incomplete["stack"]!.AsObject().Remove("language");
            incomplete["userKey"] = 42;
            File.WriteAllText(configPath, incomplete.ToJsonString());
            var healed = await SetupProgramAdvisoryTests.RunCliAsync(processHome, configuration.Home, workloads.Home, project,
                ["init", project], timeout.Token);
            healed.ExitCode.Should().Be(0, healed.StandardOutput + healed.StandardError);
            var repaired = JsonNode.Parse(File.ReadAllText(configPath))!;
            repaired["stack"]!["runtime"]!.GetValue<string>().Should().Be("dotnet-isolated");
            repaired["stack"]!["language"]!.GetValue<string>().Should().Be(language);
            repaired["userKey"]!.GetValue<int>().Should().Be(42);
            var forced = await SetupProgramAdvisoryTests.RunCliAsync(processHome, configuration.Home, workloads.Home, project,
                ["init", project, "-s", "dotnet", "-l", alias, "--name", "ReinitializedApp", "--force"], timeout.Token);
            forced.ExitCode.Should().Be(0, forced.StandardOutput + forced.StandardError);
            File.Exists(Path.Combine(project, "NativeGreeting" + extension)).Should().BeFalse();
            File.Exists(Path.Combine(project, "ReinitializedApp" + (language == "F#" ? ".fsproj" : ".csproj"))).Should().BeTrue();
            var reinitialized = JsonNode.Parse(File.ReadAllText(configPath))!;
            reinitialized["stack"]!["runtime"]!.GetValue<string>().Should().Be("dotnet");
            reinitialized.AsObject().Should().NotContainKey("userKey");
        }
    }

    [Theory]
    [InlineData("Go", "go", "Go")]
    [InlineData("Java", "java", "Java")]
    [InlineData("PowerShell", "powershell", "PowerShell")]
    public async Task LegacyControl_ShippedProjectOnlyStacks_HaveNoInstalledItemWorkflow(string package, string stack, string language)
    {
        WorkloadPathsOptions workloads = new(Path.Combine(_root, "workloads"));
        CliConfigurationPathsOptions configuration = new(Path.Combine(_root, "cli"));
        TestInteractionService output = new();
        var bootstrap = CliHostFactory.CreateBuilder(output);
        bootstrap.Services.AddSingleton(workloads);
        bootstrap.Services.AddSingleton<IWorkloadPaths>(workloads);
        bootstrap.Services.AddSingleton(configuration);
        using (var installer = bootstrap.Build())
        {
            string archive = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "bundled-project-artifacts"),
                $"Azure.Functions.Cli.Workloads.{package}.*.nupkg").Single();
            await installer.Services.GetRequiredService<IWorkloadInstaller>().InstallFromPackageAsync(archive, cancellationToken: CancellationToken.None);
        }
        string project = Path.Combine(_root, "legacy-project");
        Directory.CreateDirectory(Path.Combine(project, ".func"));
        File.WriteAllText(Path.Combine(project, ".func", "config.json"), ProjectConfigurationJson.Create(stack, language));
        File.WriteAllText(Path.Combine(project, "host.json"),
            """{"version":"2.0","extensionBundle":{"id":"Microsoft.Azure.Functions.ExtensionBundle","version":"[4.*, 5.0.0)"}}""");
        File.WriteAllText(Path.Combine(project, stack switch { "go" => "go.mod", "java" => "pom.xml", _ => "profile.ps1" }),
            stack == "java" ? "<project />" : string.Empty);
        var builder = CliHostFactory.CreateBuilder(output);
        builder.Services.AddSingleton(workloads);
        builder.Services.AddSingleton<IWorkloadPaths>(workloads);
        builder.Services.AddSingleton(configuration);
        builder.Services.AddSingleton<ICliConfigurationProvider>(provider =>
            new CliConfigurationProvider(provider.GetRequiredService<ILocalSettingsProvider>(), configuration));
        await WorkloadRegistration.RegisterWorkloadsAsync(builder.Services, workloads, output, CancellationToken.None);
        _loadedRuntime = true;
        using var host = builder.Build();
        try
        {
            var parsed = Parser.CreateCommand(host.Services).Parse(["new", project, "-t", "http", "--non-interactive"]);
            int exit = await parsed.InvokeAsync(new() { EnableDefaultExceptionHandler = false }, CancellationToken.None);

            exit.Should().Be(1, output.AllOutput);
            output.AllOutput.ToLowerInvariant().Should().Contain("templates").And.Contain(stack);
            var installed = await host.Services.GetRequiredService<IInstalledTemplatesWorkloads>().ListInstalledAsync(stack, CancellationToken.None);
            installed.Should().BeEmpty();
        }
        finally
        {
            foreach (var workload in host.Services.GetRequiredService<IWorkloadProvider>().GetRuntimeWorkloads())
            {
                workload.LoadContext.Unload();
            }
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (UnauthorizedAccessException) when (_loadedRuntime)
        {
            // Windows can retain the legacy-control workload mappings until the test process exits.
        }
    }
}
