// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Tests.Console;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledPythonTemplateTests
{
    [Fact]
    public async Task PythonFullEntry_PublicGrammarAndAutomaticProviders_InitThenRepeatedNewPreservesAppAndOriginalValidation()
    {
        PhysicalFileSystem files = new();
        WorkloadPathsOptions paths = new(Path.Combine(_root, "entry-workloads"));
        var home = Path.Combine(_root, "entry-cli");
        FileWorkloadOperationCoordinator coordinator = new(paths);
        WorkloadStore workloads = new(paths, coordinator);
        TemplateStore templates = new(new(home), files, new FileTemplateStoreLock(), NullLoggerFactory.Instance);
        BundledTemplateInstaller bundles = new(templates, new BasicArtifactInspector(files), files, new(home));
        WorkloadMetadataReader metadata = new();
        var runtime = Substitute.For<IWorkloadRuntimeIdentifierProvider>();
        runtime.Current.Returns("win-x64");
        WorkloadRidPackageSelector selector = new(runtime);
        var source = Substitute.For<IWorkloadPackageSource>();
        WorkloadInstaller installer = new(source, new WorkloadPackageInspector(metadata, selector), selector,
            new WorkloadDeploymentService(paths, workloads, metadata, coordinator, files, bundles), coordinator, workloads, files);
        await installer.InstallFromPackageAsync(BuiltArchive);
        await installer.InstallFromPackageAsync(BuildMetadataOnlyPythonBundle());
        var project = Path.Combine(_root, "full-entry-project");
        var app = Path.Combine(project, "function_app.py");
        NativeCliEntryPoint entry = new();
        TestInteractionService initialized = new();

        var initExit = await entry.ExecuteAsync(["init", project, "-s", "python", "-l", "py"],
            initialized, paths, new(home), CancellationToken.None, TrackPythonEntry, _ => Task.FromResult<string?>(null));

        initExit.Should().Be(0, initialized.AllOutput);
        _loadedBootWorkload.Should().BeTrue();
        File.Exists(Path.Combine(project, ".func", "config.json")).Should().BeTrue();
        File.Exists(app).Should().BeTrue();
        Directory.Exists(Path.Combine(project, ".venv")).Should().BeFalse();
        File.AppendAllText(app, "\n# user-owned full entry\nimport user_dependency\ndef retained_helper():\n    return 'kept'\n");
        foreach (var functionName in new string?[] { null, "SecondGreeting" })
        {
            var before = File.ReadAllBytes(app);
            TestInteractionService output = new();
            string[] name = functionName is null ? [] : ["--name", functionName];
            var advisoryCalls = 0;

            var exit = await entry.ExecuteAsync(["new", project, "-t", "HttpTrigger-Python", "--non-interactive",
                "--output=json", "--http-trigger-auth-level", "ANONYMOUS", .. name],
                output, paths, new(home), CancellationToken.None, TrackPythonEntry, _ =>
                {
                    advisoryCalls++;
                    return Task.FromResult<string?>("9999.0.0");
                });

            exit.Should().Be(0, output.AllOutput);
            advisoryCalls.Should().Be(0);
            File.ReadAllBytes(app).Take(before.Length).Should().Equal(before);
            var jsonLine = output.Lines.Should().ContainSingle(line => line.StartsWith("JSON: ", StringComparison.Ordinal)).Subject;
            using var result = JsonDocument.Parse(jsonLine["JSON: ".Length..]);
            result.RootElement.GetProperty("kind").GetString().Should().Be("created");
            result.RootElement.GetProperty("succeeded").GetBoolean().Should().BeTrue();
            result.RootElement.GetProperty("executionDirectory").GetString().Should().Be(project);
            result.RootElement.GetProperty("outputDirectory").GetString().Should().Be(project);
            output.AllOutput.Should().NotContain("INPUT:").And.NotContain("SELECT:").And.NotContain("9999.0.0");
        }

        File.ReadAllText(app).Should().Contain("def http_trigger(", "def SecondGreeting(", "func.AuthLevel.ANONYMOUS",
            "# user-owned full entry", "def retained_helper():");
        File.Exists(Path.Combine(project, "function_app.py.py")).Should().BeFalse();
        var beforeRefusal = File.ReadAllBytes(app);
        TestInteractionService invalid = new();
        var rejected = await entry.ExecuteAsync(["new", project, "-t", "HttpTrigger-Python", "--non-interactive",
            "--output=json", "--name", "bad-name", "--http-trigger-auth-level", "ANONYMOUS"],
            invalid, paths, new(home), CancellationToken.None, TrackPythonEntry, _ => Task.FromResult<string?>(null));
        rejected.Should().Be(1, invalid.AllOutput);
        using (var result = JsonDocument.Parse(invalid.Lines.Single(line => line.StartsWith("JSON: ", StringComparison.Ordinal))["JSON: ".Length..]))
        {
            result.RootElement.GetProperty("kind").GetString().Should().Be("invalidLegacyInput");
            result.RootElement.GetProperty("succeeded").GetBoolean().Should().BeFalse();
        }
        File.ReadAllBytes(app).Should().Equal(beforeRefusal);
        TestInteractionService help = new();
        var helped = await entry.ExecuteAsync(["new", project, "-t", "HttpTrigger-Python", "--help"],
            help, paths, new(home), CancellationToken.None, TrackPythonEntry, _ => Task.FromResult<string?>(null));
        helped.Should().Be(0, help.AllOutput);
        help.AllOutput.Should().Contain("[path]", "--http-trigger-auth-level", "ANONYMOUS").And.NotContain("--dry-run");
        File.ReadAllBytes(app).Should().Equal(beforeRefusal);
        source.ReceivedCalls().Should().BeEmpty();
    }

    private void TrackPythonEntry(IServiceCollection services) =>
        _loadedBootWorkload |= services.Any(service => service.ImplementationInstance is RuntimeWorkloadInfo);
}
