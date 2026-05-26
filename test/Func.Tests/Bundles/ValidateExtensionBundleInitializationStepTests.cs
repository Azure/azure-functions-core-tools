// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Commands.Start.Initialization;
using Azure.Functions.Cli.Commands.Start.Initialization.Rendering;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Hosting.Dashboard.Rendering;
using Azure.Functions.Cli.Profiles;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NuGet.Versioning;
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
        ExtensionBundleResolution resolution = new ExtensionBundleResolution.WorkloadMissing("MISSING-HINT");
        resolver.ResolveAsync(Arg.Any<ExtensionBundleProjectContext>(), Arg.Any<CancellationToken>())
            .Returns(resolution);

        ValidateExtensionBundleInitializationStep step = NewStep(resolver);
        StartInitializationStepContext context = NewContext();

        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => step.ExecuteAsync(context, CancellationToken.None));
        Assert.Equal("MISSING-HINT", ex.Message);
        Assert.True(ex.IsUserError);
    }

    [Fact]
    public async Task BundleDeclared_ResolverResolves_PopulatesEnvVars()
    {
        WriteHostJson(new { extensionBundle = new { id = "Microsoft.Azure.Functions.ExtensionBundle", version = "[4.*, 5.0.0)" } });

        IExtensionBundleResolver resolver = Substitute.For<IExtensionBundleResolver>();
        ExtensionBundleResolution resolution = new ExtensionBundleResolution.Resolved(
            "Microsoft.Azure.Functions.ExtensionBundle",
            "4.35.0",
            "/install/4.35.0/tools/any",
            RuntimeWarning: null);
        resolver.ResolveAsync(Arg.Any<ExtensionBundleProjectContext>(), Arg.Any<CancellationToken>())
            .Returns(resolution);

        ValidateExtensionBundleInitializationStep step = NewStep(resolver);
        StartInitializationStepContext context = NewContext();

        StartInitializationStepResult result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal("Bundle Microsoft.Azure.Functions.ExtensionBundle 4.35.0", result.Message);
        Assert.Equal("/install/4.35.0/tools/any", context.State.BundleDownloadPath);
        Assert.Equal("/install/4.35.0/tools/any", context.State.BundleEnvVarsForHost[DownloadPathEnvVar]);
        Assert.Equal("false", context.State.BundleEnvVarsForHost[EnsureLatestEnvVar]);
    }

    [Fact]
    public async Task BundleDeclared_ProfileRangeIsForwardedToResolver()
    {
        WriteHostJson(new { extensionBundle = new { id = "Microsoft.Azure.Functions.ExtensionBundle", version = "[4.*, 5.0.0)" } });
        ResolvedProfile profile = CreateProfileWithBundleRange("[3.0.0, 5.0.0)");
        IExtensionBundleResolver resolver = Substitute.For<IExtensionBundleResolver>();
        ExtensionBundleResolution resolution = new ExtensionBundleResolution.Resolved(
            "Microsoft.Azure.Functions.ExtensionBundle",
            "4.35.0",
            "/install/4.35.0/tools/any",
            RuntimeWarning: null);
        resolver.ResolveAsync(Arg.Any<ExtensionBundleProjectContext>(), Arg.Any<CancellationToken>())
            .Returns(resolution);
        ValidateExtensionBundleInitializationStep step = NewStep(resolver);
        StartInitializationStepContext context = NewContext(profile: profile);

        await step.ExecuteAsync(context, CancellationToken.None);

        await resolver.Received(1).ResolveAsync(
            Arg.Is<ExtensionBundleProjectContext>(bundleContext =>
                bundleContext.ProfileName == "flex"
                && bundleContext.ProfileBundleVersionRange == "[3.0.0, 5.0.0)"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BundleDeclared_ResolverFails_ThrowsWithResolverHint()
    {
        WriteHostJson(new { extensionBundle = new { id = "Microsoft.Azure.Functions.ExtensionBundle", version = "[4.*, 5.0.0)" } });

        IExtensionBundleResolver resolver = Substitute.For<IExtensionBundleResolver>();
        ExtensionBundleResolution resolution = new ExtensionBundleResolution.NoCompatibleInstall(
            "[4.*, 5.0.0)",
            [],
            "RESOLVER-HINT");
        resolver.ResolveAsync(Arg.Any<ExtensionBundleProjectContext>(), Arg.Any<CancellationToken>())
            .Returns(resolution);

        ValidateExtensionBundleInitializationStep step = NewStep(resolver);
        StartInitializationStepContext context = NewContext();

        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => step.ExecuteAsync(context, CancellationToken.None));
        Assert.Equal("RESOLVER-HINT", ex.Message);
        Assert.True(ex.IsUserError);
    }

    [Fact]
    public async Task BundleDeclared_UsesResolvedProjectDirectoryForHostJson()
    {
        string optionsDirectory = Path.Combine(_projectDir, "options");
        string projectDirectory = Path.Combine(_projectDir, "project");
        Directory.CreateDirectory(optionsDirectory);
        Directory.CreateDirectory(projectDirectory);
        WriteHostJson(
            new { extensionBundle = new { id = "Microsoft.Azure.Functions.ExtensionBundle", version = "[4.*, 5.0.0)" } },
            projectDirectory);

        IExtensionBundleResolver resolver = Substitute.For<IExtensionBundleResolver>();
        ExtensionBundleResolution resolution = new ExtensionBundleResolution.Resolved(
            "Microsoft.Azure.Functions.ExtensionBundle",
            "4.35.0",
            "/install/4.35.0/tools/any",
            RuntimeWarning: null);
        resolver.ResolveAsync(Arg.Any<ExtensionBundleProjectContext>(), Arg.Any<CancellationToken>())
            .Returns(resolution);
        ValidateExtensionBundleInitializationStep step = NewStep(resolver);
        StartInitializationStepContext context = NewContext(projectDirectory: projectDirectory, optionsDirectory: optionsDirectory);

        await step.ExecuteAsync(context, CancellationToken.None);

        await resolver.Received(1).ResolveAsync(
            Arg.Is<ExtensionBundleProjectContext>(bundleContext =>
                bundleContext.BundleId == "Microsoft.Azure.Functions.ExtensionBundle"
                && bundleContext.HostJsonVersionRange == "[4.*, 5.0.0)"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MalformedHostJson_ThrowsGracefulException()
    {
        File.WriteAllText(Path.Combine(_projectDir, "host.json"), "{ invalid");
        IExtensionBundleResolver resolver = Substitute.For<IExtensionBundleResolver>();
        ValidateExtensionBundleInitializationStep step = NewStep(resolver);
        StartInitializationStepContext context = NewContext();

        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => step.ExecuteAsync(context, CancellationToken.None));

        Assert.True(ex.IsUserError);
        Assert.Contains("host.json is not valid JSON", ex.Message);
        await resolver.DidNotReceiveWithAnyArgs().ResolveAsync(default!, default);
    }

    [Fact]
    public async Task IncompleteBundleSection_ThrowsGracefulException()
    {
        WriteHostJson(new { extensionBundle = new { id = "Microsoft.Azure.Functions.ExtensionBundle" } });
        IExtensionBundleResolver resolver = Substitute.For<IExtensionBundleResolver>();
        ValidateExtensionBundleInitializationStep step = NewStep(resolver);
        StartInitializationStepContext context = NewContext();

        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => step.ExecuteAsync(context, CancellationToken.None));

        Assert.True(ex.IsUserError);
        Assert.Contains("host.json extensionBundle must include", ex.Message);
        await resolver.DidNotReceiveWithAnyArgs().ResolveAsync(default!, default);
    }

    private static ValidateExtensionBundleInitializationStep NewStep(IExtensionBundleResolver resolver)
    {
        var bundleSectionReader = new HostJsonBundleSectionReader();
        return new ValidateExtensionBundleInitializationStep(
            resolver,
            bundleSectionReader,
            NullLogger<ValidateExtensionBundleInitializationStep>.Instance);
    }

    private StartInitializationStepContext NewContext(
        ResolvedProfile? profile = null,
        string? projectDirectory = null,
        string? optionsDirectory = null)
    {
        string resolvedProjectDirectory = projectDirectory ?? _projectDir;
        string resolvedOptionsDirectory = optionsDirectory ?? resolvedProjectDirectory;
        var options = new StartCommandOptions(
            WorkingDirectory.FromExplicit(resolvedOptionsDirectory),
            Port: null, Cors: [], CorsCredentials: false, Functions: [],
            NoBuild: false, EnableAuth: false, RequestedProfileName: null, RequestedHostVersion: null,
            Offline: false, OutputMode: OutputMode.Plain, NoTui: true, LogFilePath: null,
            DemoFunctionCount: 0, DemoSpeedMultiplier: 1.0, DemoAutoExit: true);

        var init = new StartInitializationContext(options, "5.0.0-test", IsInteractive: false, CanPrompt: false);
        var state = new StartInitializationState
        {
            Project = new BundleSupportingTestProject(resolvedProjectDirectory),
            ResolvedProfile = profile,
            ProfileName = profile?.Name ?? "none",
        };
        IStartInitializationRenderer renderer = Substitute.For<IStartInitializationRenderer>();
        IStartInitializationStep stepStub = Substitute.For<IStartInitializationStep>();
        stepStub.Id.Returns("test");
        var stepContext = new StartInitializationStepContext(init, state, stepStub, renderer, TimeProvider.System);
        return stepContext;
    }

    private static ResolvedProfile CreateProfileWithBundleRange(string bundleRange)
    {
        var source = new ProfileSourceInfo(ProfileSourceKind.BuiltIn, "bundled");
        Dictionary<string, VersionRange> workerVersionRanges = new(StringComparer.OrdinalIgnoreCase);

        return new ResolvedProfile(
            "flex",
            source,
            Sku: "flex",
            ProfileStatus.Stable,
            DeprecationUrl: null,
            HostVersionRange: VersionRange.Parse("[1.8.1, 4.1048.200)"),
            WorkerVersionRanges: workerVersionRanges,
            ExtensionBundleVersionRange: VersionRange.Parse(bundleRange),
            SupportedRuntimes: ["node"],
            Notes: null);
    }

    private void WriteHostJson(object payload, string? directory = null)
    {
        string path = Path.Combine(directory ?? _projectDir, "host.json");
        var options = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(payload, options);

        File.WriteAllText(path, json);
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
