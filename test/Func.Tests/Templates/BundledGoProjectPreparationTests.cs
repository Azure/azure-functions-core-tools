// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads.Go;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledRemainingProjectTemplateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GoPreparation_DryRun_PreviewsOwnedTidyOrSkipWithoutToolExecution(bool skip)
    {
        using var services = await InstallAsync("Go");
        var runner = PreparationRunner(services);
        ControlledGoPreparation owned = new();
        OwnedGoPreparationCallback callback = new(owned);
        var request = GoPreparationRequest(callback, skip, dryRun: true);

        var result = await runner.ExecuteAsync(request, Snapshot("go"), CancellationToken.None);

        result.Kind.Should().Be("previewed");
        result.Succeeded.Should().BeTrue();
        callback.Previews.Should().Be(1);
        callback.Completions.Should().Be(0);
        callback.Preview!.Operations.Should().ContainSingle().Which.Should()
            .Be(new NativeProjectPreparationStep(NativeProjectPreparationOperation.GoModTidy, skip));
        result.Preparation!.Preview.Should().BeSameAs(callback.Preview);
        result.Preparation.Completion.Should().BeNull();
        owned.ToolCalls.Should().Be(0);
        Directory.Exists(request.WorkingDirectory.Info.FullName).Should().BeFalse();
    }

    [Theory]
    [InlineData(false, 0, "")]
    [InlineData(true, 0, "")]
    [InlineData(false, 17, "module resolution failed")]
    [InlineData(false, -1, "Go executable could not be started")]
    public async Task GoPreparation_Create_UsesOwnedResultAfterNativeConfigurationWithoutIgnoringFailure(
        bool skip, int exitCode, string stderr)
    {
        using var services = await InstallAsync("Go");
        var runner = PreparationRunner(services);
        ControlledGoPreparation owned = new() { Outcome = (exitCode, stderr) };
        OwnedGoPreparationCallback callback = new(owned);
        var request = GoPreparationRequest(callback, skip);

        var result = await runner.ExecuteAsync(request, Snapshot("go"), CancellationToken.None);

        bool success = skip || exitCode == 0;
        result.Succeeded.Should().Be(success);
        result.Kind.Should().Be(success ? "created" : "partialPreparation");
        callback.Previews.Should().Be(1);
        callback.Completions.Should().Be(1);
        callback.Outcome!.Skipped.Should().Be(skip);
        callback.Outcome.ExitCode.Should().Be(skip ? null : exitCode);
        callback.Outcome.StandardError.Should().Be(skip ? string.Empty : stderr);
        owned.ToolCalls.Should().Be(skip ? 0 : 1);
        string root = request.WorkingDirectory.Info.FullName;
        File.ReadAllText(Path.Combine(root, "go.mod")).Should().StartWith("module goapp");
        File.ReadAllText(Path.Combine(root, "main.go")).Should().Contain("worker.Start(app)");
        var config = JsonNode.Parse(File.ReadAllText(Path.Combine(root, ".func", "config.json")))!;
        config["stack"]!["runtime"]!.GetValue<string>().Should().Be("go");
        config["stack"]!["language"]!.GetValue<string>().Should().Be("Go");
        result.Invocation!.ConfiguredProjects.Should().Equal(root);
        result.Preparation!.Completion!.Succeeded.Should().Be(success);
        TestInteractionService rendered = new();
        new InitExecutionRenderer(rendered).Render(result, jsonOutput: true).Should().Be(success ? 0 : 1);
        rendered.AllOutput.Should().Contain("\"operation\":\"GoModTidy\"", skip ? "\"skipped\":true" : "\"skipped\":false");
        if (!success)
        {
            result.Diagnostics.Should().Contain(diagnostic => diagnostic.Contains(stderr, StringComparison.Ordinal));
            File.Exists(Path.Combine(root, "host.json")).Should().BeTrue("failed preparation must not hide or roll back native project files");
            TestInteractionService plain = new();
            new InitExecutionRenderer(plain).Render(result, jsonOutput: false).Should().Be(1);
            plain.AllOutput.Should().Contain(stderr).And.NotContain("SUCCESS:");
        }
    }

    [Fact]
    public async Task GoPreparation_CancellationDuringOwnedTidy_PropagatesAndPreservesAlreadyCreatedProject()
    {
        using var services = await InstallAsync("Go");
        var runner = PreparationRunner(services);
        using CancellationTokenSource cancellation = new();
        ControlledGoPreparation owned = new() { DuringTool = cancellation.Cancel };
        OwnedGoPreparationCallback callback = new(owned);
        var request = GoPreparationRequest(callback, skip: false);

        Func<Task> action = () => runner.ExecuteAsync(request, Snapshot("go"), cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        owned.ToolCalls.Should().Be(1);
        callback.Completions.Should().Be(1);
        File.Exists(Path.Combine(request.WorkingDirectory.Info.FullName, "main.go")).Should().BeTrue();
        File.Exists(Path.Combine(request.WorkingDirectory.Info.FullName, ".func", "config.json")).Should().BeTrue();
    }

    [Fact]
    public async Task GoPreparation_MismatchedOwnedSourceHash_RefusesBeforeNativeWriteOrTidy()
    {
        using var services = await InstallAsync("Go");
        var runner = PreparationRunner(services);
        ControlledGoPreparation owned = new();
        OwnedGoPreparationCallback callback = new(owned) { ExpectedSourceSha256 = new string('0', 64) };
        var request = GoPreparationRequest(callback, skip: false);

        var result = await runner.ExecuteAsync(request, Snapshot("go"), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Contains("source", StringComparison.Ordinal));
        Directory.Exists(request.WorkingDirectory.Info.FullName).Should().BeFalse();
        callback.Completions.Should().Be(0);
        owned.ToolCalls.Should().Be(0);
    }

    private InitExecutionRunner PreparationRunner(ServiceProvider services)
    {
        InstalledProjectStackCatalog catalog = new(services.GetServices<IProjectStack>());
        TemplateProjectSupport support = new(new(catalog), new(_files));
        TemplaterFactory factory = new(new TemplateStore(new(Home), _files, new FileTemplateStoreLock(), NullLoggerFactory.Instance), _files);
        TemplateArgumentParser arguments = new();
        TestInteractionService interaction = new();
        return new(factory, arguments, new(arguments, interaction), interaction, new(interaction), new(_files),
            () => catalog, () => support, _files, Substitute.For<ILocalSettingsProvider>(),
            services.GetRequiredService<IFunctionsProjectResolver>());
    }

    private InitExecutionRequest GoPreparationRequest(INativeProjectPreparation callback, bool skip, bool dryRun = false)
        => new(WorkingDirectory.FromExplicit(Path.Combine(_root, "go-prepared")), "Azure.Functions.Go.Basic", "GoApp", "go", "golang",
            Force: false, NonInteractive: true, DryRun: dryRun, JsonOutput: true,
            skip ? ["--skip-go-mod-tidy", "true"] : [], new HashSet<string>())
        {
            ProjectPreparation = callback,
        };

    private sealed class OwnedGoPreparationCallback(GoProjectPreparation owned) : INativeProjectPreparation
    {
        private string? _sourceHash;
        private string? _configurationHash;

        public string ExpectedSourceSha256 { get; init; } = Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(ProjectFiles.ReadTemplate(typeof(GoProjectInitializer).Assembly, "go.mod"))));
        public int Previews { get; private set; }
        public int Completions { get; private set; }
        public NativeProjectPreparationPreview? Preview { get; private set; }
        public GoProjectPreparationResult? Outcome { get; private set; }

        public async Task<NativeProjectPreparationPreview> PreviewAsync(
            NativeProjectPreparationContext context, CancellationToken cancellationToken)
        {
            Previews++;
            context.SelectedTemplate.Entry.Identity.Should().Be("Azure.Functions.Go.Basic");
            context.NativePreview.ProjectConfigurations.Should().ContainSingle().Which.Root.Should().Be(context.TargetDirectory.Info.FullName);
            ReadOnlyMemory<byte> source = await context.SelectedTemplate.ReadPackageFileAsync("content/project/go.mod", cancellationToken);
            _sourceHash = Convert.ToHexStringLower(SHA256.HashData(source.Span));
            _configurationHash = context.SelectedTemplate.ConfigurationSha256;
            bool skip = bool.Parse(context.Parameters["SkipGoModTidy"]!);
            bool sourceMatches = _sourceHash == ExpectedSourceSha256;
            Preview = new(sourceMatches, [new(NativeProjectPreparationOperation.GoModTidy, skip)],
                sourceMatches ? [] : ["The selected Go preparation source hash changed; no project or tool execution was attempted."]);
            return Preview;
        }

        public async Task<NativeProjectPreparationResult> CompleteAsync(NativeProjectPreparationContext context,
            NativeProjectPreparationPreview preview, CancellationToken cancellationToken)
        {
            Completions++;
            ReadOnlyMemory<byte> source = await context.SelectedTemplate.ReadPackageFileAsync("content/project/go.mod", cancellationToken);
            if (context.SelectedTemplate.ConfigurationSha256 != _configurationHash
                || Convert.ToHexStringLower(SHA256.HashData(source.Span)) != _sourceHash)
            {
                return new(false, ["The selected Go preparation source changed after native creation."]);
            }
            preview.Should().BeSameAs(Preview);
            Outcome = await owned.PrepareAsync(context.TargetDirectory.Info.FullName,
                preview.Operations.Single().Skipped, cancellationToken);
            return new(Outcome.Skipped || Outcome.ExitCode == 0, Outcome.Skipped || Outcome.ExitCode == 0
                ? [] : [$"'go mod tidy' failed (exit {Outcome.ExitCode}): {Outcome.StandardError}"]);
        }
    }

    private sealed class ControlledGoPreparation : GoProjectPreparation
    {
        public (int ExitCode, string Stderr) Outcome { get; init; }
        public Action? DuringTool { get; init; }
        public int ToolCalls { get; private set; }

        public override Task<(int ExitCode, string Stderr)> RunGoModTidyAsync(string workingDirectory, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Exists(Path.Combine(workingDirectory, "go.mod")).Should().BeTrue();
            File.Exists(Path.Combine(workingDirectory, "main.go")).Should().BeTrue();
            File.Exists(Path.Combine(workingDirectory, ".func", "config.json")).Should().BeTrue();
            ToolCalls++;
            DuringTool?.Invoke();
            return Task.FromResult(Outcome);
        }
    }
}
