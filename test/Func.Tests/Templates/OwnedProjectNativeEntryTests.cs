// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Nodes;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Go;
using Azure.Functions.Cli.Templates.Java;
using Azure.Functions.Cli.Templates.PowerShell;
using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledRemainingProjectTemplateTests
{
    [Theory]
    [InlineData("Go", "go", "golang")]
    [InlineData("PowerShell", "powershell", "pwsh")]
    [InlineData("Java", "java", "Java")]
    public async Task OwnedFullEntry_OriginalPublicInitGrammar_UsesAutomaticProductionProvidersForActualInstalledStack(
        string stack, string runtime, string language)
    {
        using var installed = await InstallAsync(stack);
        string project = Path.Combine(_root, "full-entry-" + runtime);
        string[] options = runtime switch
        {
            "go" => ["--skip-go-mod-tidy"],
            "powershell" => ["--runtime-version", "7.6"],
            _ => [],
        };
        string[] arguments = ["init", project, "-s", runtime, "-l", language, "-n", "FullEntryApp", "--no-bundles", .. options];
        TestInteractionService interaction = new();
        bool observedAutomaticRegistration = false;

        int exit = await new NativeCliEntryPoint().ExecuteAsync(arguments, interaction, new(Path.Combine(_root, "workloads")),
            new(Home), CancellationToken.None, configure: services =>
            {
                Type expectedProvider = runtime switch
                {
                    "go" => typeof(GoProjectWorkflowProvider),
                    "powershell" => typeof(PowerShellProjectWorkflowProvider),
                    _ => typeof(JavaProjectWorkflowProvider),
                };
                services.Should().ContainSingle(service => service.ServiceType == typeof(INativeProjectWorkflowProvider)
                    && service.ImplementationType == expectedProvider);
                foreach (RuntimeWorkloadInfo workload in services.Select(service => service.ImplementationInstance).OfType<RuntimeWorkloadInfo>())
                {
                    _contexts.Add(workload.LoadContext);
                }
                observedAutomaticRegistration = true;
            }, versionCheck: _ => Task.FromResult<string?>(null));

        exit.Should().Be(0, interaction.AllOutput);
        observedAutomaticRegistration.Should().BeTrue();
        arguments.Should().Equal(["init", project, "-s", runtime, "-l", language, "-n", "FullEntryApp", "--no-bundles", .. options]);
        var config = JsonNode.Parse(File.ReadAllText(Path.Combine(project, ".func", "config.json")))!;
        config["stack"]!["runtime"]!.GetValue<string>().Should().Be(runtime);
        config["stack"]!["language"]!.GetValue<string>().Should().Be(stack);
        var host = JsonNode.Parse(File.ReadAllText(Path.Combine(project, "host.json")))!.AsObject();
        host["version"]!.GetValue<string>().Should().Be("2.0");
        host.Should().NotContainKey("extensionBundle");
        var settings = JsonNode.Parse(File.ReadAllText(Path.Combine(project, "local.settings.json")))!;
        settings["Values"]!["FUNCTIONS_WORKER_RUNTIME"]!.GetValue<string>().Should().Be(runtime == "go" ? "native" : runtime);
        if (runtime == "go")
        {
            File.ReadAllText(Path.Combine(project, "go.mod")).Should().StartWith("module fullentryapp").And.NotContain("require");
            File.ReadAllText(Path.Combine(project, "main.go")).Should().Contain("worker.Start(app)");
            File.Exists(Path.Combine(project, "go.sum")).Should().BeFalse();
            interaction.AllOutput.Should().Contain("GoModTidy: Skipped");
        }
        else if (runtime == "powershell")
        {
            settings["Values"]!["FUNCTIONS_WORKER_RUNTIME_VERSION"]!.GetValue<string>().Should().Be("7.6");
            File.Exists(Path.Combine(project, "profile.ps1")).Should().BeTrue();
            File.Exists(Path.Combine(project, "requirements.psd1")).Should().BeFalse();
            host.Should().NotContainKey("managedDependency");
            interaction.AllOutput.Should().Contain("PowerShellManagedDependencies: Skipped");
        }
        else
        {
            File.ReadAllText(Path.Combine(project, "pom.xml")).Should().Contain("<artifactId>fullentryapp</artifactId>");
            File.Exists(Path.Combine(project, "src", "main", "java", "com", "function", "Function.java")).Should().BeTrue();
            Directory.Exists(Path.Combine(project, "target")).Should().BeFalse();
            interaction.AllOutput.Should().NotContain("Running mvn", "GoModTidy", "PowerShellManagedDependencies");
        }
    }
}
