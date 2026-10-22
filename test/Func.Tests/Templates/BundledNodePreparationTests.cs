// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Node;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads.Node;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Versioning;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    [Theory]
    [InlineData("js", false)]
    [InlineData("js", true)]
    [InlineData("ts", false)]
    [InlineData("ts", true)]
    public async Task Preparation_ActualNativeProject_PreviewDoesNotRunAndSkipIsExplicit(string language, bool skip)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        var npm = Substitute.For<NodeNpmInstaller>();
        using var bridge = await CreateNodeBridgeAsync(preparation: new NodeProjectPreparation(_files, npm));
        string project = Path.Combine(_root, "project");
        string[] skipTokens = skip ? ["--skip-npm-install"] : [];
        string[] tokens = [project, "-s", "node", "-l", language, "--no-bundles", .. skipTokens];

        (await InvokeNodeBridgeAsync(bridge.Adapter.ParseInitInvocation(bridge.InitCommand(), tokens,
            new("basic", NonInteractive: true, DryRun: true, JsonOutput: true)))).Should().Be(0, _interaction.AllOutput);

        bridge.Init.Last!.Kind.Should().Be("previewed");
        bridge.Init.Last.Preparation!.Preview.Operations.Should().ContainSingle()
            .Which.Should().Be(new NativeProjectPreparationStep(NativeProjectPreparationOperation.NodeNpmInstall, skip));
        bridge.Init.Last.Preparation.Completion.Should().BeNull();
        Directory.Exists(project).Should().BeFalse();
        await npm.DidNotReceive().InstallAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        if (skip)
        {
            (await InvokeNodeBridgeAsync(bridge.Adapter.ParseInitInvocation(bridge.InitCommand(), tokens,
                new("basic", NonInteractive: true, JsonOutput: true)))).Should().Be(0, _interaction.AllOutput);
            bridge.Init.Last!.Preparation!.Completion!.Succeeded.Should().BeTrue();
            bridge.Init.Last.Preparation.Completion.Diagnostics.Should().Contain("Skipped npm installation.");
            File.Exists(Path.Combine(project, ".func", "config.json")).Should().BeTrue();
            await npm.DidNotReceive().InstallAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        }
    }

    [Theory]
    [InlineData("js", 0, "")]
    [InlineData("ts", 0, "npm warning preserved")]
    [InlineData("js", 7, "npm controlled failure stderr")]
    [InlineData("ts", -1, "npm executable could not start")]
    public async Task Preparation_ControlledHelper_RunsAfterActualNativeFilesAndConfigurationAndReportsOutcome(
        string language, int exitCode, string stderr)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        string project = Path.Combine(_root, "project");
        var npm = Substitute.For<NodeNpmInstaller>();
        npm.InstallAsync(project, Arg.Any<CancellationToken>()).Returns(call =>
        {
            call.ArgAt<string>(0).Should().Be(project);
            File.Exists(Path.Combine(project, "host.json")).Should().BeTrue();
            using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(project, ".func", "config.json")));
            config.RootElement.GetProperty("stack").GetProperty("runtime").GetString().Should().Be("node");
            config.RootElement.GetProperty("stack").GetProperty("language").GetString()
                .Should().Be(language == "js" ? "JavaScript" : "TypeScript");
            using var package = JsonDocument.Parse(File.ReadAllText(Path.Combine(project, "package.json")));
            package.RootElement.GetProperty("dependencies").GetProperty("@azure/functions").GetString().Should().Be("^4.0.0");
            package.RootElement.GetProperty("main").GetString().Should().Be(language == "js" ? "src/functions/*.js" : "dist/src/functions/*.js");
            File.Exists(Path.Combine(project, "tsconfig.json")).Should().Be(language == "ts");
            Directory.GetFiles(Path.Combine(project, "src", "functions")).Should().BeEmpty();
            File.WriteAllText(Path.Combine(project, "controlled-preparation.txt"), "controlled helper, not real npm execution");
            return (exitCode, stderr);
        });
        using var bridge = await CreateNodeBridgeAsync(preparation: new NodeProjectPreparation(_files, npm));

        int result = await InvokeNodeBridgeAsync(bridge.Adapter.ParseInitInvocation(bridge.InitCommand(),
            [project, "-s", "node", "-l", language], new("basic", NonInteractive: true, JsonOutput: true)));

        result.Should().Be(exitCode == 0 ? 0 : 1, _interaction.AllOutput);
        bridge.Init.Last!.Kind.Should().Be(exitCode == 0 ? "created" : "partialPreparation");
        bridge.Init.Last.Preparation!.Completion!.Succeeded.Should().Be(exitCode == 0);
        File.Exists(Path.Combine(project, "controlled-preparation.txt")).Should().BeTrue();
        File.Exists(Path.Combine(project, ".func", "config.json")).Should().BeTrue();
        if (stderr.Length > 0)
        {
            bridge.Init.Last.Preparation.Completion.Diagnostics.Should().Contain(stderr);
            _interaction.AllOutput.Should().Contain(stderr);
        }
        if (exitCode != 0)
        {
            bridge.Init.Last.Succeeded.Should().BeFalse();
            bridge.Init.Last.Diagnostics.Should().Contain(diagnostic => diagnostic.Contains("retained", StringComparison.Ordinal));
            _interaction.AllOutput.Should().NotContain("\"kind\":\"created\"").And.NotContain("SUCCESS:");
        }
        await npm.Received(1).InstallAsync(project, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Preparation_ControlledCancellation_PropagatesOriginalTokenAndRetainsNativeProject(bool helperThrows)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        string project = Path.Combine(_root, "project");
        using var cancellation = new CancellationTokenSource();
        OperationCanceledException? original = null;
        CancellationToken received = default;
        var npm = Substitute.For<NodeNpmInstaller>();
        npm.InstallAsync(project, Arg.Any<CancellationToken>()).Returns(call =>
        {
            received = call.Arg<CancellationToken>();
            received.CanBeCanceled.Should().BeTrue();
            File.Exists(Path.Combine(project, ".func", "config.json")).Should().BeTrue();
            cancellation.Cancel();
            original = new OperationCanceledException("controlled npm cancellation", received);
            return helperThrows ? Task.FromException<(int ExitCode, string Stderr)>(original)
                : Task.FromResult((-1, "legacy helper converted cancellation to a result"));
        });
        using var bridge = await CreateNodeBridgeAsync(preparation: new NodeProjectPreparation(_files, npm));
        var parsed = bridge.Adapter.ParseInitInvocation(bridge.InitCommand(), [project, "-s", "node", "-l", "js"],
            new("basic", NonInteractive: true, JsonOutput: true));

        Func<Task> invoke = () => parsed.InvokeAsync(new() { EnableDefaultExceptionHandler = false }, cancellation.Token);
        var thrown = (await invoke.Should().ThrowAsync<OperationCanceledException>()).Which;

        if (helperThrows)
        {
            thrown.Should().BeSameAs(original);
        }
        thrown.CancellationToken.Should().Be(received);
        File.Exists(Path.Combine(project, "host.json")).Should().BeTrue();
        File.Exists(Path.Combine(project, "package.json")).Should().BeTrue();
        File.Exists(Path.Combine(project, ".func", "config.json")).Should().BeTrue();
        _interaction.AllOutput.Should().Contain("Native project files and CLI configuration were created before stack preparation was canceled.")
            .And.Contain("No rollback was attempted; preparation may be incomplete.");
        _interaction.AllOutput.Should().NotContain("SUCCESS:").And.NotContain("\"kind\":\"created\"");
    }

    [Fact]
    public void Preparation_NullDependencies_RejectProgrammerErrors()
    {
        Action files = () => new NodeProjectPreparation(null!, new NodeNpmInstaller());
        Action npm = () => new NodeProjectPreparation(_files, null!);
        files.Should().Throw<ArgumentNullException>().WithParameterName("fileSystem");
        npm.Should().Throw<ArgumentNullException>().WithParameterName("npmInstaller");
    }

    [Fact]
    public async Task Preparation_ActualSourcePreview_RefusesWrongTargetAndCannotRunBeforeNativeCreation()
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        using var services = await CreateServicesAsync();
        var target = WorkingDirectory.FromExplicit(Path.Combine(_root, "project"));
        var availability = new TemplateConstraintEvaluationSnapshot([new("node-stack", NuGetVersion.Parse("1.0.0"), ["node"], true)]);
        using var templater = await services.GetRequiredService<ITemplaterFactory>().CreateProjectAsync(new(target), availability,
            services.GetRequiredService<TemplateProjectSupport>(), CancellationToken.None);
        var selected = (await templater.ResolveGroupAsync("Azure.Functions.Node.Basic.JavaScript", TemplateType.Project, CancellationToken.None)).Group!.Single();
        var values = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(), [])).ValuesByCanonicalName;
        var native = await selected.InvokeAsync(new(target, values, DryRun: true), CancellationToken.None);
        var context = new NativeProjectPreparationContext(selected, target, values, native);
        var npm = Substitute.For<NodeNpmInstaller>();
        var preparation = new NodeProjectPreparation(_files, npm);

        var preview = await preparation.PreviewAsync(context, CancellationToken.None);
        var early = await preparation.CompleteAsync(context, preview, CancellationToken.None);
        var wrongTarget = await preparation.PreviewAsync(context with
        {
            TargetDirectory = WorkingDirectory.FromExplicit(Path.Combine(_root, "other-target")),
        }, CancellationToken.None);

        preview.Succeeded.Should().BeTrue();
        early.Succeeded.Should().BeFalse();
        early.Diagnostics.Should().ContainSingle().Which.Should().Contain("must exist before npm");
        wrongTarget.Succeeded.Should().BeFalse();
        Directory.Exists(target.Info.FullName).Should().BeFalse();
        await npm.DidNotReceive().InstallAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        Func<Task> noContext = () => preparation.PreviewAsync(null!, CancellationToken.None);
        await noContext.Should().ThrowAsync<ArgumentNullException>();
        Func<Task> noPreview = () => preparation.CompleteAsync(context, null!, CancellationToken.None);
        await noPreview.Should().ThrowAsync<ArgumentNullException>();
    }
}
