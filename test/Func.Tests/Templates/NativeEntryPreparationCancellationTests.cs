// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Console.Theme;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Node;
using Azure.Functions.Cli.Tests.Console;
using Azure.Functions.Cli.Workloads.Node;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    private const string RetainedPreparationWarning =
        "Native project files and CLI configuration were created before stack preparation was canceled. "
        + "No rollback was attempted; preparation may be incomplete.";

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task StagedReview_CancelAfterRealConfigurationCommitBeforePreparation_RetainsBytesWarnsAndStops(bool fullEntry, bool skip)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        string project = Path.Combine(_root, "review-preparation");
        using CancellationTokenSource cancellation = new();
        ReviewCancelAtConfigurationCommit files = new(project, cancellation);
        var npm = Substitute.For<NodeNpmInstaller>();
        ReviewPreparationObserver observer = new();
        using BufferedConsole stdout = new(false, false, noColor: true);
        using BufferedConsole stderr = new(false, false, noColor: true);
        SpectreInteractionService interaction = new(new DefaultTheme(), stdout, stderr);
        int? entryExit = null;
        Exception? failure = null;
        void Configure(IServiceCollection services)
        {
            TrackEntryContexts(services);
            services.AddSingleton<IFileSystem>(files);
            services.AddSingleton(npm);
            services.RemoveAll<INativeProjectWorkflowProvider>();
            services.AddScoped<INativeProjectWorkflowProvider>(provider =>
                new ReviewObservedProjectProvider(provider.GetRequiredService<NodeTemplateWorkflowProvider>(), observer));
        }

        if (fullEntry)
        {
            string[] skipTokens = skip ? ["--skip-npm-install"] : [];
            entryExit = await new NativeCliEntryPoint().ExecuteAsync(
                ["init", project, "-s", "node", "-l", "js", "--no-bundles", .. skipTokens],
                interaction, _paths, new(Home), cancellation.Token, Configure, _ => Task.FromResult<string?>(null));
        }
        else
        {
            using var host = await new NativeTemplateWorkflowHostFactory().CreateAsync(interaction, _paths, new(Home), services =>
            {
                services.AddFirstPartyNativeWorkflows();
                Configure(services);
            }, enableBundleBindingConstraints: true, CancellationToken.None);
            await host.StartAsync(CancellationToken.None);
            using var invocation = host.CreateInvocation();
            string[] tokens = ["--no-bundles", "true", "--skip-npm-install", skip ? "true" : "false"];
            Func<Task> execute = () => invocation.Composer.ExecuteProjectAsync(NodeComposedInit(project, "js", tokens), cancellation.Token);
            failure = await Record.ExceptionAsync(execute);
        }

        files.CommittedBytes.Should().NotBeEmpty();
        Directory.GetFiles(project, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes)
            .Should().BeEquivalentTo(files.CommittedBytes, "post-create cancellation must not roll back or change committed bytes");
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(project, ".func", "config.json")));
        config.RootElement.GetProperty("stack").GetProperty("runtime").GetString().Should().Be("node");
        config.RootElement.GetProperty("stack").GetProperty("language").GetString().Should().Be("JavaScript");
        using var package = JsonDocument.Parse(File.ReadAllText(Path.Combine(project, "package.json")));
        package.RootElement.GetProperty("dependencies").GetProperty("@azure/functions").GetString().Should().Be("^4.0.0");
        File.Exists(Path.Combine(project, "host.json")).Should().BeTrue();
        observer.Previews.Should().Be(1);
        observer.Completions.Should().Be(0);
        await npm.DidNotReceive().InstallAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        if (fullEntry)
        {
            entryExit.Should().Be(130);
        }
        else
        {
            var thrown = failure.Should().BeOfType<OperationCanceledException>().Which;
            thrown.CancellationToken.Should().Be(cancellation.Token);
            thrown.StackTrace.Should().Contain("ResolvedTemplate.ValidateConfigurationAsync");
        }

        stdout.Output.Should().BeEmpty();
        string.Join(' ', stderr.Output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Should().Contain(RetainedPreparationWarning);
        stderr.Output.Should().NotContain("unexpected error");
    }

    private sealed class ReviewCancelAtConfigurationCommit(string project, CancellationTokenSource cancellation) : PhysicalFileSystem
    {
        public IReadOnlyDictionary<string, byte[]> CommittedBytes { get; private set; } = new Dictionary<string, byte[]>();

        public override void MoveFile(string sourcePath, string destinationPath, bool overwrite = false)
        {
            base.MoveFile(sourcePath, destinationPath, overwrite);
            if (destinationPath == Path.Combine(project, ".func", "config.json"))
            {
                CommittedBytes = Directory.GetFiles(project, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
                cancellation.Cancel();
            }
        }
    }

    private sealed class ReviewPreparationObserver
    {
        public int Previews { get; set; }
        public int Completions { get; set; }
    }

    private sealed class ReviewObservedProjectProvider(NodeTemplateWorkflowProvider inner, ReviewPreparationObserver observer)
        : INativeProjectWorkflowProvider
    {
        public string Stack => inner.Stack;
        public INativeProjectPreparation Preparation { get; } = new ReviewObservedPreparation(inner.Preparation, observer);
        public bool Handles(TemplateCatalogEntry entry) => inner.Handles(entry);

        public Task<DotNetProjectLayoutQualification?> QualifyLayoutAsync(NativeProjectTemplateSource source, CancellationToken cancellationToken)
            => ((INativeProjectWorkflowProvider)inner).QualifyLayoutAsync(source, cancellationToken);

        public Task<NativeTemplateInvocationPreparation> PrepareAsync(NativeProjectWorkflowContext context, CancellationToken cancellationToken)
            => ((INativeProjectWorkflowProvider)inner).PrepareAsync(context, cancellationToken);
    }

    private sealed class ReviewObservedPreparation(INativeProjectPreparation inner, ReviewPreparationObserver observer) : INativeProjectPreparation
    {
        public Task<NativeProjectPreparationPreview> PreviewAsync(NativeProjectPreparationContext context, CancellationToken cancellationToken)
        {
            observer.Previews++;
            return inner.PreviewAsync(context, cancellationToken);
        }

        public Task<NativeProjectPreparationResult> CompleteAsync(NativeProjectPreparationContext context,
            NativeProjectPreparationPreview preview, CancellationToken cancellationToken)
        {
            observer.Completions++;
            return inner.CompleteAsync(context, preview, cancellationToken);
        }
    }
}
