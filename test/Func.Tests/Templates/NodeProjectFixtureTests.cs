// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class NodeProjectFixtureTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "func-node-fixture-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task ExportAsync_BlankDestination_RejectsArgument(string? destination)
    {
        Func<Task> action = () => NodeProjectFixture.ExportAsync(destination!, CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>().WithParameterName("parentDirectory");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExportAsync_NonemptyDestination_PreservesExistingContent(bool directory)
    {
        var destination = Path.Combine(_root, "export");
        Directory.CreateDirectory(destination);
        var sentinel = Path.Combine(destination, "sentinel");
        if (directory)
        {
            Directory.CreateDirectory(sentinel);
        }
        else
        {
            File.WriteAllText(sentinel, "user content");
        }

        Func<Task> action = () => NodeProjectFixture.ExportAsync(destination, CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>().WithParameterName("parentDirectory");
        Directory.EnumerateFileSystemEntries(destination).Should().ContainSingle().Which.Should().Be(sentinel);
        if (!directory)
        {
            File.ReadAllText(sentinel).Should().Be("user content");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExportAsync_EmptyDestination_GeneratesNodeProject(bool existingDirectory)
    {
        if (existingDirectory)
        {
            Directory.CreateDirectory(_root);
        }

        var output = await NodeProjectFixture.ExportAsync(_root, CancellationToken.None);

        output.Should().Be(Path.Combine(_root, "generated"));
        File.ReadAllText(Path.Combine(output, "Hello", "index.js")).Should().Contain("Hello from GeneratedApp").And.NotContain("GREETING");
        File.Exists(Path.Combine(output, ".func", "config.json")).Should().BeTrue();
    }

    [Fact]
    public async Task ExportAsync_PreCancelled_DoesNotWriteFixture()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Func<Task> action = () => NodeProjectFixture.ExportAsync(_root, cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(_root).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExportAsync_InvalidPreview_DoesNotInvokeCreation(bool writesDirectory)
    {
        var invoker = Substitute.For<ISelectedTemplateInvoker>();
        using var fixture = ControlledFixture(invoker);
        using CancellationTokenSource cancellation = new();
        invoker.InvokeAsync(Arg.Any<TemplateInvocationRequest>(), cancellation.Token).Returns(_ =>
        {
            if (writesDirectory)
            {
                Directory.CreateDirectory(fixture.Output.Info.FullName);
            }

            return new TemplateInvocationResult(writesDirectory ? TemplateInvocationStatus.Success : TemplateInvocationStatus.EngineFailure,
                [], "controlled preview");
        });

        Func<Task> action = () => fixture.ExportAsync(cancellation.Token);

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("Smoke project preview failed:*");
        await invoker.Received(1).InvokeAsync(Arg.Is<TemplateInvocationRequest>(request => request.DryRun), cancellation.Token);
        await invoker.DidNotReceive().InvokeAsync(Arg.Is<TemplateInvocationRequest>(request => !request.DryRun), cancellation.Token);
    }

    [Fact]
    public async Task ExportAsync_CreationFailure_PreservesDiagnostic()
    {
        var invoker = Substitute.For<ISelectedTemplateInvoker>();
        using var fixture = ControlledFixture(invoker);
        invoker.InvokeAsync(Arg.Any<TemplateInvocationRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
            new TemplateInvocationResult(((TemplateInvocationRequest)call[0]).DryRun ? TemplateInvocationStatus.Success : TemplateInvocationStatus.ConfigurationFailure,
                [], "controlled configuration failure"));

        Func<Task> action = () => fixture.ExportAsync(CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*controlled configuration failure*");
        await invoker.Received(1).InvokeAsync(Arg.Is<TemplateInvocationRequest>(request => !request.DryRun), CancellationToken.None);
    }

    [Fact]
    public async Task ExportAsync_CancellationAfterPreview_DoesNotInvokeCreation()
    {
        var invoker = Substitute.For<ISelectedTemplateInvoker>();
        using var fixture = ControlledFixture(invoker);
        using CancellationTokenSource cancellation = new();
        invoker.InvokeAsync(Arg.Any<TemplateInvocationRequest>(), cancellation.Token).Returns(_ =>
        {
            cancellation.Cancel();
            return new TemplateInvocationResult(TemplateInvocationStatus.Success, []);
        });

        Func<Task> action = () => fixture.ExportAsync(cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        await invoker.Received(1).InvokeAsync(Arg.Is<TemplateInvocationRequest>(request => request.DryRun), cancellation.Token);
        await invoker.DidNotReceive().InvokeAsync(Arg.Is<TemplateInvocationRequest>(request => !request.DryRun), cancellation.Token);
    }

    [Theory]
    [InlineData("templater")]
    [InlineData("template")]
    [InlineData("output")]
    public void Constructor_NullDependency_Throws(string dependency)
    {
        var output = WorkingDirectory.FromExplicit(_root);
        using var session = new TemplateEngineSession(new(output), Path.Combine(_root, "home"));
        using var templater = TemplateFixtures.CreateTemplater(session);
        var template = Selected(Substitute.For<ISelectedTemplateInvoker>());

        Action create = () => _ = new NodeProjectFixture(dependency == "templater" ? null! : templater,
            dependency == "template" ? null! : template, dependency == "output" ? null! : output);

        create.Should().Throw<ArgumentNullException>().WithParameterName(dependency);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task CreateAsync_BlankAliasKey_DoesNotWriteFixture(string? aliasKey)
    {
        Func<Task> create = () => NodeProjectFixture.CreateAsync(_root, CancellationToken.None, aliasKey!);

        await create.Should().ThrowAsync<ArgumentException>().WithParameterName("aliasKey");
        Directory.Exists(_root).Should().BeFalse();
    }

    [Fact]
    public void WriteRunnableNodeProjectPackage_DoesNotModifyPythonFixture()
    {
        var python = TemplateFixtures.WriteProjectJourneyPackage(_root);
        var original = File.ReadAllBytes(Path.Combine(python, "project", ".template.config", "template.json"));

        var node = TemplateFixtures.WriteRunnableNodeProjectPackage(_root);

        node.Should().NotBe(python);
        File.ReadAllBytes(Path.Combine(python, "project", ".template.config", "template.json")).Should().Equal(original);
        File.ReadAllText(Path.Combine(python, "project", "FuncProject.txt")).Should().Be("FuncProject GREETING");
        File.Exists(Path.Combine(node, "project", "FuncProject.txt")).Should().BeFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private NodeProjectFixture ControlledFixture(ISelectedTemplateInvoker invoker)
    {
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "generated"));
        var session = new TemplateEngineSession(new(output), Path.Combine(_root, "home"));
        var templater = TemplateFixtures.CreateTemplater(session);
        return new(templater, Selected(invoker), output);
    }

    private static ResolvedTemplate Selected(ISelectedTemplateInvoker invoker) => new(
        new("node-fixture", "Node fixture", null, TemplateType.Project, [], null, "JavaScript", 0, null, false,
            [new("Greeting", "Greeting", null, null, "string", [], null, false, false, false)], null)
        {
            ConstraintEvaluation = TemplateConstraintEvaluation.Eligible,
        }, invoker);
}
