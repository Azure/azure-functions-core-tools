// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Commands.Start.Initialization;
using Azure.Functions.Cli.Commands.Start.Initialization.Rendering;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Hosting.Dashboard.Rendering;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Bundles.Tests;

public class ValidateExtensionBundleInitializationStepTests : IDisposable
{
    private const string DownloadPathEnvVar = "AzureFunctionsJobHost__extensionBundle__downloadPath";
    private const string EnsureLatestEnvVar = "AzureFunctionsJobHost__extensionBundle__ensureLatest";

    private readonly string _projectDir;

    public ValidateExtensionBundleInitializationStepTests()
    {
        _projectDir = Path.Combine(Path.GetTempPath(), "bundle-step-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_projectDir))
        {
            Directory.Delete(_projectDir, recursive: true);
        }
    }

    [Fact]
    public async Task NoHostJson_SkipsResolution()
    {
        IExtensionBundleResolver resolver = Substitute.For<IExtensionBundleResolver>();
        ValidateExtensionBundleInitializationStep step = NewStep(resolver);
        StartInitializationStepContext context = NewContext();

        StartInitializationStepResult result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal("No extension bundle declared", result.Message);
        Assert.Empty(context.State.BundleEnvVarsForHost);
        await resolver.DidNotReceiveWithAnyArgs().ResolveAsync(default!, default);
    }

    [Fact]
    public async Task HostJsonWithoutBundle_SkipsResolution()
    {
        WriteHostJson(new { version = "2.0" });
        IExtensionBundleResolver resolver = Substitute.For<IExtensionBundleResolver>();
        ValidateExtensionBundleInitializationStep step = NewStep(resolver);
        StartInitializationStepContext context = NewContext();

        StartInitializationStepResult result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal("No extension bundle declared", result.Message);
        await resolver.DidNotReceiveWithAnyArgs().ResolveAsync(default!, default);
    }

    [Fact]
    public async Task ResolverReportsWorkloadMissing_ThrowsWithHint()
    {
        WriteHostJson(new { extensionBundle = new { id = "Microsoft.Azure.Functions.ExtensionBundle", version = "[4.*, 5.0.0)" } });
        IExtensionBundleResolver resolver = Substitute.For<IExtensionBundleResolver>();
        resolver.ResolveAsync(Arg.Any<ExtensionBundleProjectContext>(), Arg.Any<CancellationToken>())
            .Returns(new ExtensionBundleResolution.WorkloadMissing("MISSING-HINT"));

        ValidateExtensionBundleInitializationStep step = NewStep(resolver);
        StartInitializationStepContext context = NewContext();

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => step.ExecuteAsync(context, CancellationToken.None));
        Assert.Equal("MISSING-HINT", ex.Message);
    }

    [Fact]
    public async Task BundleDeclared_ResolverResolves_PopulatesEnvVars()
    {
        WriteHostJson(new { extensionBundle = new { id = "Microsoft.Azure.Functions.ExtensionBundle", version = "[4.*, 5.0.0)" } });

        IExtensionBundleResolver resolver = Substitute.For<IExtensionBundleResolver>();
        resolver.ResolveAsync(Arg.Any<ExtensionBundleProjectContext>(), Arg.Any<CancellationToken>())
            .Returns(new ExtensionBundleResolution.Resolved(
                "Microsoft.Azure.Functions.ExtensionBundle", "4.35.0", "/install/4.35.0/tools/any",
                RuntimeWarning: null));

        ValidateExtensionBundleInitializationStep step = NewStep(resolver);
        StartInitializationStepContext context = NewContext();

        StartInitializationStepResult result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal("Bundle Microsoft.Azure.Functions.ExtensionBundle 4.35.0", result.Message);
        Assert.Equal("/install/4.35.0/tools/any", context.State.BundleDownloadPath);
        Assert.Equal("/install/4.35.0/tools/any", context.State.BundleEnvVarsForHost[DownloadPathEnvVar]);
        Assert.Equal("false", context.State.BundleEnvVarsForHost[EnsureLatestEnvVar]);
    }

    [Fact]
    public async Task BundleDeclared_ResolverFails_ThrowsWithResolverHint()
    {
        WriteHostJson(new { extensionBundle = new { id = "Microsoft.Azure.Functions.ExtensionBundle", version = "[4.*, 5.0.0)" } });

        IExtensionBundleResolver resolver = Substitute.For<IExtensionBundleResolver>();
        resolver.ResolveAsync(Arg.Any<ExtensionBundleProjectContext>(), Arg.Any<CancellationToken>())
            .Returns(new ExtensionBundleResolution.NoCompatibleInstall(
                "[4.*, 5.0.0)", [], "RESOLVER-HINT"));

        ValidateExtensionBundleInitializationStep step = NewStep(resolver);
        StartInitializationStepContext context = NewContext();

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => step.ExecuteAsync(context, CancellationToken.None));
        Assert.Equal("RESOLVER-HINT", ex.Message);
    }

    private static ValidateExtensionBundleInitializationStep NewStep(IExtensionBundleResolver resolver) =>
        new(resolver, NullLogger<ValidateExtensionBundleInitializationStep>.Instance);

    private StartInitializationStepContext NewContext()
    {
        var options = new StartCommandOptions(
            WorkingDirectory.FromExplicit(_projectDir),
            Port: null, Cors: [], CorsCredentials: false, Functions: [],
            NoBuild: false, EnableAuth: false, RequestedHostVersion: null,
            OutputMode: OutputMode.Plain, NoTui: true, LogFilePath: null,
            DemoFunctionCount: 0, DemoSpeedMultiplier: 1.0, DemoAutoExit: true);

        var init = new StartInitializationContext(options, "5.0.0-test", IsInteractive: false, CanPrompt: false);
        var state = new StartInitializationState
        {
            Project = new BundleSupportingTestProject(_projectDir),
        };
        IStartInitializationRenderer renderer = Substitute.For<IStartInitializationRenderer>();
        IStartInitializationStep stepStub = Substitute.For<IStartInitializationStep>();
        stepStub.Id.Returns("test");
        return new StartInitializationStepContext(init, state, stepStub, renderer, TimeProvider.System);
    }

    private void WriteHostJson(object payload)
    {
        File.WriteAllText(
            Path.Combine(_projectDir, "host.json"),
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class BundleSupportingTestProject(string directory) : FunctionsProject
    {
        public override WorkingDirectory WorkingDirectory { get; } = WorkingDirectory.FromExplicit(directory);

        public override string StackName => "node";

        public override string StackDisplayName => "Node.js";

        public override bool SupportsExtensionBundles => true;

        public override IFunctionsWorker Worker { get; } = Substitute.For<IFunctionsWorker>();
    }
}
