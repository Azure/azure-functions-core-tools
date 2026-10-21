// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates;

/// <summary>
/// Shares the controlled Node project setup with tests and the external smoke script.
/// Public export access is required by the file-based smoke script.
/// </summary>
public sealed class NodeProjectFixture : IDisposable
{
    private readonly Templater _templater;

    internal NodeProjectFixture(Templater templater, ResolvedTemplate template, WorkingDirectory output)
    {
        _templater = templater ?? throw new ArgumentNullException(nameof(templater));
        Template = template ?? throw new ArgumentNullException(nameof(template));
        Output = output ?? throw new ArgumentNullException(nameof(output));
    }

    internal ResolvedTemplate Template { get; }

    internal WorkingDirectory Output { get; }

    internal TemplateInvocationRequest Request => new(Output, new Dictionary<string, string?> { ["Greeting"] = "Hello" }, "GeneratedApp");

    public static async Task<string> ExportAsync(string parentDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        if (Directory.Exists(parentDirectory) && Directory.EnumerateFileSystemEntries(parentDirectory).Any())
        {
            throw new ArgumentException("The smoke fixture requires an empty output directory.", nameof(parentDirectory));
        }

        using NodeProjectFixture fixture = await CreateAsync(parentDirectory, cancellationToken);
        return await fixture.ExportAsync(cancellationToken);
    }

    public void Dispose() => _templater.Dispose();

    internal static async Task<NodeProjectFixture> CreateAsync(
        string parentDirectory, CancellationToken cancellationToken, string aliasKey = "JavaScript")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(aliasKey);
        cancellationToken.ThrowIfCancellationRequested();
        string home = Path.Combine(parentDirectory, "hive");
        string hive = Path.Combine(home, "templates");
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteRunnableNodeProjectPackage(parentDirectory), hive, cancellationToken);
        var stack = Substitute.For<IProjectStack>();
        stack.Stack.Returns("node");
        stack.DisplayName.Returns("Node.js");
        stack.SupportedLanguages.Returns(["JavaScript"]);
        stack.SupportedLanguageAliases.Returns(new Dictionary<string, IReadOnlyList<string>> { [aliasKey] = ["js"] });
        var output = WorkingDirectory.FromExplicit(Path.Combine(parentDirectory, "generated"));
        TemplateProjectConfigurationPlanner planner = new(new([stack]));
        PhysicalFileSystem fileSystem = new();
        TemplateStore store = new(new(home), fileSystem, new FileTemplateStoreLock(), NullLoggerFactory.Instance);
        ITemplaterFactory factory = new TemplaterFactory(store, fileSystem);
        TemplateConstraintEvaluationSnapshot snapshot = new([new("node-package", NuGetVersion.Parse("1.0"), ["node"], true)]);
        Templater templater = await factory.CreateProjectAsync(new(output), snapshot, new(planner, new(fileSystem)), cancellationToken);
        try
        {
            TemplateResolutionResult resolution = await templater.ResolveGroupAsync(
                "Func.Tests.RunnableNodeProject", TemplateType.Project, cancellationToken);
            if (resolution.Status != TemplateResolutionStatus.Success || resolution.Group is not { Count: 1 })
            {
                throw new InvalidOperationException($"Smoke project selection failed: {resolution.Status}.");
            }

            return new(templater, resolution.Group[0], output);
        }
        catch
        {
            templater.Dispose();
            throw;
        }
    }

    internal async Task<string> ExportAsync(CancellationToken cancellationToken)
    {
        TemplateInvocationResult preview = await Template.InvokeAsync(Request with { DryRun = true }, cancellationToken);
        if (preview.Status != TemplateInvocationStatus.Success || Directory.Exists(Output.Info.FullName))
        {
            throw new InvalidOperationException($"Smoke project preview failed: {preview.Diagnostic ?? preview.Status.ToString()}.");
        }

        TemplateInvocationResult created = await Template.InvokeAsync(Request, cancellationToken);
        if (created.Status != TemplateInvocationStatus.Success)
        {
            throw new InvalidOperationException($"Smoke project generation failed: {created.Diagnostic ?? created.Status.ToString()}.");
        }

        return Output.Info.FullName;
    }
}
