// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class NativeWorkflowHostAuthorityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "native-host-authority-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExplicitFactory_FailedConfigureAfterStackContribution_DoesNotOfferThatStackOrGenerateProject()
    {
        string home = Path.Combine(_root, "workloads");
        string cliHome = Path.Combine(_root, "cli");
        string content = Path.Combine(home, "workloads", "Partial", "1.0.0", "tools", "any");
        Directory.CreateDirectory(content);
        foreach (string file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "tools", "any")))
        {
            File.Copy(file, Path.Combine(content, Path.GetFileName(file)));
        }

        WorkloadRegistry registry = new()
        {
            Workloads =
            [
                new()
                {
                    PackageId = "Partial", PackageVersion = "1.0.0", Aliases = ["partial"],
                    EntryPoint = new()
                    {
                        AssemblyPath = "Azure.Functions.Cli.Workloads.Tests.Fixtures.WithCommand.dll",
                        Type = "Azure.Functions.Cli.Workloads.Tests.Fixtures.WithCommand.PartiallyConfiguredStackWorkload",
                    },
                },
            ],
        };
        File.WriteAllText(Path.Combine(home, WorkloadPathsOptions.WorkloadRegistryFileName),
            JsonSerializer.Serialize(registry, WorkloadJsonContext.Default.WorkloadRegistry));
        string package = Path.Combine(_root, "package");
        Directory.CreateDirectory(Path.Combine(package, ".template.config"));
        File.WriteAllText(Path.Combine(package, "host.json"), """{"version":"2.0"}""");
        File.WriteAllText(Path.Combine(package, ".template.config", "template.json"), $$$"""
            {"identity":"Partial.Project","name":"Partial project without workload constraint","shortName":"partial-project",
             "tags":{"type":"project","language":"Partial"},"constraints":{"host":{"type":"host","args":[{"hostname":"func"}]}},
             "primaryOutputs":[{"path":"host.json"}],"postActions":[
               {"actionId":"{{{TemplateProjectConfigurationPlanner.ConfigurationActionId}}}",
                "args":{"primaryOutputIndex":"0","stack":"partial","language":"Partial"}}]}
            """);
        await TemplateFixtures.InstallAsync(package, Path.Combine(cliHome, "templates"), CancellationToken.None);
        TestInteractionService interaction = new();
        using var host = await new NativeTemplateWorkflowHostFactory().CreateAsync(interaction, new(home), new(cliHome),
            _ => { }, enableBundleBindingConstraints: false, CancellationToken.None);
        using var invocation = host.CreateInvocation();
        var target = WorkingDirectory.FromExplicit(Path.Combine(_root, "target"));

        var result = await invocation.Composer.ExecuteProjectAsync(new(target, "Partial.Project", null, "partial", "Partial",
            false, true, false, true, [], new HashSet<string>()), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Kind.Should().Be("noInstalledStacks");
        result.Templates.Should().BeEmpty();
        Directory.Exists(target.Info.FullName).Should().BeFalse();
        interaction.AllOutput.Should().Contain("failed to initialize");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // A collectible workload assembly can remain mapped until after the test process releases it.
            }
            catch (UnauthorizedAccessException)
            {
                // Windows may retain a mapped workload DLL until the test process exits.
            }
        }
    }
}
