// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.DotNet;
using Azure.Functions.Cli.Templates.DotNet.Shared;
using Azure.Functions.Cli.Templates.Engine;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class DotNetInvocationProjectResolverTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("func-dotnet-resolve-").FullName;
    private readonly IFunctionsProjectResolver _projects = Substitute.For<IFunctionsProjectResolver>();
    private readonly IDotnetCliRunner _runner = Substitute.For<IDotnetCliRunner>();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData("C#", ".csproj")]
    [InlineData("F#", ".fsproj")]
    public async Task ResolveAsync_TrustedProject_UsesSourceSelectionAndActualEvaluatedPrimitives(string language, string extension)
    {
        string path = Path.Combine(_root, "Selected" + extension);
        File.WriteAllText(path, "<Project />");
        Setup(language);
        DotNetInvocationProjectResolver resolver = Create();

        DotNetInvocationProject project = await resolver.ResolveAsync(Context(language), CancellationToken.None);

        project.Properties.ProjectFilePath.Should().Be(path);
        project.Properties.TargetFramework.Should().Be("net10.0");
        project.Properties.RootNamespace.Should().Be("Evaluated.Namespace");
        project.ProjectSha256.Should().HaveLength(64);
        if (language == "F#")
        {
            project.CompileOrder.Should().Equal(Path.Combine(_root, "Program.fs"));
        }
    }

    [Theory]
    [InlineData("multiple")]
    [InlineData("outputOnly")]
    [InlineData("languageConflict")]
    [InlineData("metadataUnavailable")]
    [InlineData("wrongStack")]
    public async Task ResolveAsync_UntrustedOrAmbiguousSelection_RefusesInsteadOfScanningOrCastingWorkloadInternals(string invalid)
    {
        Setup("C#");
        if (invalid != "outputOnly")
        {
            File.WriteAllText(Path.Combine(_root, invalid == "languageConflict" ? "Selected.fsproj" : "Selected.csproj"), "<Project />");
        }
        if (invalid == "multiple")
        {
            File.WriteAllText(Path.Combine(_root, "Second.fsproj"), "<Project />");
        }
        if (invalid == "metadataUnavailable")
        {
            _projects.ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
                .Returns(ProjectResolutionResults.NotResolved("ambiguous source project"));
        }
        DotNetInvocationProjectResolver resolver = Create();
        TemplateEngineContext context = invalid == "wrongStack"
            ? new(WorkingDirectory.FromExplicit(_root), new(WorkingDirectory.FromExplicit(_root), "node", "C#"))
            : Context("C#");
        Func<Task> action = () => resolver.ResolveAsync(context, CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>();
        _runner.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_ProjectChangesDuringEvaluation_Refuses()
    {
        Setup("C#");
        string path = Path.Combine(_root, "Selected.csproj");
        File.WriteAllText(path, "<Project />");
        _runner.When(runner => runner.RunStreamingAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>(), Arg.Any<Action<string>>(),
            Arg.Any<Action<string>?>(), Arg.Any<CancellationToken>())).Do(_ => File.AppendAllText(path, "changed"));
        Func<Task> action = () => Create().ResolveAsync(Context("C#"), CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*changed*");
    }

    [Fact]
    public async Task ResolveAsync_PreCanceled_DoesNotResolveOrEvaluate()
    {
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();
        Func<Task> action = () => Create().ResolveAsync(Context("C#"), cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        _projects.ReceivedCalls().Should().BeEmpty();
        _runner.ReceivedCalls().Should().BeEmpty();
    }

    private DotNetInvocationProjectResolver Create() => new(_projects, new PhysicalFileSystem(),
        new DotNetTemplateProjectPreparation(_runner), _runner, new FSharpProjectFiles());

    private TemplateEngineContext Context(string language)
        => new(WorkingDirectory.FromExplicit(_root), new(WorkingDirectory.FromExplicit(_root), "dotnet", language));

    private void Setup(string language)
    {
        FunctionsProject project = Substitute.For<FunctionsProject>();
        project.StackName.Returns("dotnet");
        project.Language.Returns(language);
        project.WorkingDirectory.Returns(WorkingDirectory.FromExplicit(_root));
        _projects.ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(ProjectResolutionResults.Resolved(project, "trusted installed stack metadata"));
        _runner.RunStreamingAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>(), Arg.Any<Action<string>>(),
            Arg.Any<Action<string>?>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                IReadOnlyList<string> arguments = call.ArgAt<IReadOnlyList<string>>(0);
                string text = arguments.Contains("-getItem:Compile")
                    ? JsonSerializer.Serialize(new { Items = new { Compile = new[] { new { FullPath = Path.Combine(_root, "Program.fs") } } } })
                    : """{"Properties":{"TargetFramework":"net10.0","RootNamespace":"Evaluated.Namespace"}}""";
                call.ArgAt<Action<string>>(2)(text);
                return Task.CompletedTask;
            });
    }
}
