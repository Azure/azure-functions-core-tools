// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Tests.Console;
using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledPythonTemplateTests
{
    [Fact]
    public async Task PythonFullEntry_PublicGrammarAndAutomaticProviders_InitThenRepeatedNewPreservesAppAndOriginalValidation()
    {
        var (paths, home, source) = await InstallPythonRuntimeAsync("entry");
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

    [Fact]
    public async Task PythonFullEntry_RegisteredPolicyDoesNotAuthorizeUnownedTemplateOrFallBackToPlainCreation()
    {
        var (paths, home, source) = await InstallPythonRuntimeAsync("unowned");
        await TemplateFixtures.InstallAsync(BuildUnownedPythonItem(), Path.Combine(home, "templates"), CancellationToken.None);
        var project = Path.Combine(_root, "unowned-source-project");
        NativeCliEntryPoint entry = new();
        TestInteractionService initialized = new();
        var initExit = await entry.ExecuteAsync(["init", project, "-s", "python", "-l", "py"],
            initialized, paths, new(home), CancellationToken.None, TrackPythonEntry, _ => Task.FromResult<string?>(null));
        initExit.Should().Be(0, initialized.AllOutput);
        var app = Path.Combine(project, "function_app.py");
        var before = File.ReadAllBytes(app);
        TestInteractionService rejected = new();

        var exit = await entry.ExecuteAsync(["new", project, "-t", "Fixture.Unowned.Python", "--output=json", "--non-interactive"],
            rejected, paths, new(home), CancellationToken.None, TrackPythonEntry, _ => Task.FromResult<string?>(null));

        exit.Should().Be(1, rejected.AllOutput);
        using var result = JsonDocument.Parse(rejected.Lines.Single(line => line.StartsWith("JSON: ", StringComparison.Ordinal))["JSON: ".Length..]);
        result.RootElement.GetProperty("kind").GetString().Should().Be("workflowUnavailable");
        result.RootElement.GetProperty("succeeded").GetBoolean().Should().BeFalse();
        File.Exists(Path.Combine(project, "unowned.py")).Should().BeFalse();
        File.ReadAllBytes(app).Should().Equal(before);
        source.ReceivedCalls().Should().BeEmpty();
    }

    private void TrackPythonEntry(IServiceCollection services) =>
        _loadedBootWorkload |= services.Any(service => service.ImplementationInstance is RuntimeWorkloadInfo);
}
