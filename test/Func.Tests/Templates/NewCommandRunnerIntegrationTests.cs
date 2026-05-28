// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Profiles;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.V2;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Templates;

/// <summary>
/// Integration tests that exercise NewCommandRunner end-to-end against
/// substituted IFunctionsProjectResolver / IExtensionBundleResolver /
/// IHostJsonBundleSectionReader plus a real V2EngineProvider + fixture
/// workload payload on disk. Verifies the §6 pipeline lights up: channel
/// match (§4.8.1), bundle gates (§4.8.2), template resolution, engine
/// dispatch, file writes.
/// </summary>
public class NewCommandRunnerIntegrationTests : IDisposable
{
    private readonly string _projectDir;
    private readonly string _workloadHome;
    private readonly string _stableInstallDir;
    private readonly TestInteractionService _interaction;

    public NewCommandRunnerIntegrationTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "func-new-pr4-integ-" + Guid.NewGuid().ToString("N"));
        _projectDir = Path.Combine(root, "project");
        _workloadHome = Path.Combine(root, "workloads");
        _stableInstallDir = Path.Combine(_workloadHome, "node-1.0.0");
        Directory.CreateDirectory(_projectDir);
        Directory.CreateDirectory(_workloadHome);
        Directory.CreateDirectory(_stableInstallDir);
        _interaction = new TestInteractionService();
    }

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(_projectDir)!, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public async Task ExecuteAsync_Node_StableChannel_Scaffolds_File()
    {
        WriteFixturePayload(_stableInstallDir, version: "1.0.0");

        // host.json declares the stable bundle id.
        File.WriteAllText(Path.Combine(_projectDir, "host.json"), """
        {
          "version": "2.0",
          "extensionBundle": {
            "id": "Microsoft.Azure.Functions.ExtensionBundle",
            "version": "[4.0.0, 5.0.0)"
          }
        }
        """);

        NewCommandRunner runner = BuildRunner(
            installed: [new InstalledTemplatesWorkload("node", "1.0.0", _stableInstallDir)],
            stack: "node",
            language: "javascript");

        var invocation = new NewInvocation(
            new WorkingDirectory(new DirectoryInfo(_projectDir), false),
            RequestedTemplate: "HttpTrigger-JavaScript",
            RequestedFunctionName: "MyFn",
            Force: false,
            NonInteractive: true);

        int exit = await runner.ExecuteAsync(invocation, CancellationToken.None);

        // Surface the recorded interaction lines on failure so diagnosis
        // doesn't require a rerun.
        Assert.True(
            exit == 0,
            $"Expected exit=0; got {exit}. Output:{Environment.NewLine}{string.Join(Environment.NewLine, _interaction.Lines)}");
        string expected = Path.Combine(_projectDir, "src", "functions", "MyFn.js");
        Assert.True(
            File.Exists(expected),
            $"Expected scaffolded file at {expected}. Output:{Environment.NewLine}{string.Join(Environment.NewLine, _interaction.Lines)}");
    }

    [Fact]
    public async Task ExecuteAsync_PreviewBundle_Without_PreviewWorkload_Fails_With_Hint()
    {
        WriteFixturePayload(_stableInstallDir, version: "1.0.0");

        // host.json declares the preview bundle id; no preview templates pkg installed.
        File.WriteAllText(Path.Combine(_projectDir, "host.json"), """
        {
          "extensionBundle": {
            "id": "Microsoft.Azure.Functions.ExtensionBundle.Preview",
            "version": "[4.0.0, 5.0.0)"
          }
        }
        """);

        NewCommandRunner runner = BuildRunner(
            installed: [new InstalledTemplatesWorkload("node", "1.0.0", _stableInstallDir)],
            stack: "node",
            language: "javascript");

        var invocation = new NewInvocation(
            new WorkingDirectory(new DirectoryInfo(_projectDir), false),
            RequestedTemplate: "HttpTrigger-JavaScript",
            RequestedFunctionName: "MyFn",
            Force: false,
            NonInteractive: true);

        int exit = await runner.ExecuteAsync(invocation, CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains(_interaction.Lines,
            l => l.Contains("does not match", StringComparison.OrdinalIgnoreCase)
              || l.Contains("preview", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ListAsync_Returns_Templates_For_Resolved_Stack()
    {
        WriteFixturePayload(_stableInstallDir, version: "1.0.0");
        File.WriteAllText(Path.Combine(_projectDir, "host.json"), """
        {
          "extensionBundle": {
            "id": "Microsoft.Azure.Functions.ExtensionBundle",
            "version": "[4.0.0, 5.0.0)"
          }
        }
        """);

        NewCommandRunner runner = BuildRunner(
            installed: [new InstalledTemplatesWorkload("node", "1.0.0", _stableInstallDir)],
            stack: "node",
            language: "javascript");

        var invocation = new NewInvocation(
            new WorkingDirectory(new DirectoryInfo(_projectDir), false),
            RequestedTemplate: null,
            RequestedFunctionName: null,
            Force: false,
            NonInteractive: true);

        int exit = await runner.ListAsync(invocation, CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains(_interaction.Lines, l => l.Contains("HttpTrigger-JavaScript", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteAsync_Missing_Template_Argument_NonInteractive_Errors()
    {
        WriteFixturePayload(_stableInstallDir, version: "1.0.0");
        File.WriteAllText(Path.Combine(_projectDir, "host.json"), """
        {
          "extensionBundle": {
            "id": "Microsoft.Azure.Functions.ExtensionBundle",
            "version": "[4.0.0, 5.0.0)"
          }
        }
        """);

        NewCommandRunner runner = BuildRunner(
            installed: [new InstalledTemplatesWorkload("node", "1.0.0", _stableInstallDir)],
            stack: "node",
            language: "javascript");

        var invocation = new NewInvocation(
            new WorkingDirectory(new DirectoryInfo(_projectDir), false),
            RequestedTemplate: null,
            RequestedFunctionName: null,
            Force: false,
            NonInteractive: true);

        int exit = await runner.ExecuteAsync(invocation, CancellationToken.None);

        Assert.Equal(1, exit);
    }

    private NewCommandRunner BuildRunner(
        IReadOnlyList<InstalledTemplatesWorkload> installed,
        string stack,
        string language)
    {
        // Project resolver returns a stub project for the supplied stack.
        FunctionsProject project = Substitute.For<FunctionsProject>();
        project.StackName.Returns(stack);
        project.WorkingDirectory.Returns(new WorkingDirectory(new DirectoryInfo(_projectDir), false));

        IFunctionsProjectResolver projectResolver = Substitute.For<IFunctionsProjectResolver>();
        projectResolver.ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(new ProjectResolutionResult.Resolved(project, "ok"));

        // Profile resolver returns "no active profile" (PR4's profile-gate
        // observation is unused beyond running the resolver).
        IProfileResolver profileResolver = Substitute.For<IProfileResolver>();
        profileResolver.ResolveAsync(Arg.Any<ProfileResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(new ProfileResolution.None([]));

        // StackOptions for the project dir: the resolved language flows
        // through StackOptions.Language.
        IOptionsMonitor<StackOptions> stackOptions = Substitute.For<IOptionsMonitor<StackOptions>>();
        stackOptions.Get(Arg.Any<string>()).Returns(new StackOptions { Runtime = stack, Language = language });

        IInstalledTemplatesWorkloads installedTemplatesWorkloads = Substitute.For<IInstalledTemplatesWorkloads>();
        installedTemplatesWorkloads.ListInstalledAsync(stack, Arg.Any<CancellationToken>()).Returns(installed);

        // Real V2 engine over the fixture payload on disk.
        var v2 = new V2EngineProvider(installedTemplatesWorkloads);
        var registry = new TemplateEngineProviderRegistry([v2]);

        // Bundle resolver: report a resolved bundle inside the host.json
        // range. PR4 plumbs this through the §4.8.2 gate.
        IExtensionBundleResolver bundleResolver = Substitute.For<IExtensionBundleResolver>();
        bundleResolver.ResolveAsync(Arg.Any<ExtensionBundleProjectContext>(), Arg.Any<CancellationToken>())
            .Returns(new ExtensionBundleResolution.Resolved("Microsoft.Azure.Functions.ExtensionBundle", "4.20.0", "/fake", null));

        // Real host.json reader over the on-disk project file.
        var hostJsonReader = new HostJsonBundleSectionReader();

        return new NewCommandRunner(
            _interaction,
            projectResolver,
            profileResolver,
            stackOptions,
            [],
            installedTemplatesWorkloads,
            registry,
            new TemplateOptionHydrator([]),
            new TemplatePicker(_interaction),
            new NewCommandRenderer(_interaction),
            hostJsonReader,
            bundleResolver);
    }

    private static void WriteFixturePayload(string installDir, string version)
    {
        string v2 = Path.Combine(installDir, "tools", "any", "content", "v2");
        Directory.CreateDirectory(Path.Combine(v2, "templates"));
        Directory.CreateDirectory(Path.Combine(v2, "bindings"));

        File.WriteAllText(Path.Combine(v2, "templates", "templates.json"), """
        [
          {
            "id": "HttpTrigger-JavaScript",
            "name": "HTTP trigger",
            "description": "An HTTP-triggered function.",
            "language": "javascript",
            "triggerType": "http",
            "jobs": [
              { "type": "CreateNewApp", "inputs": [ { "paramId": "trigger-functionName", "assignTo": "$(FUNCTION_NAME_INPUT)", "defaultValue": "HttpTrigger" } ] }
            ],
            "actions": [
              { "type": "WriteToFile", "filePath": "src/functions/$(FUNCTION_NAME_INPUT).js", "fileContent": "// hello $(FUNCTION_NAME_INPUT)" }
            ]
          }
        ]
        """);

        File.WriteAllText(Path.Combine(v2, "bindings", "userPrompts.json"), """
        [ { "id": "trigger-functionName", "label": "Function name", "defaultValue": "HttpTrigger" } ]
        """);

        string contentRoot = Path.Combine(installDir, "tools", "any", "content");
        File.WriteAllText(
            Path.Combine(contentRoot, "templates-workload.json"),
            """{ "minBundleVersion": "[4.0.0, )" }""");

        _ = version; // recorded for future per-version assertions
    }
}
