// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class TemplateInvocationCoordinatorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvokeAsync_ValidSource_CapturesOnceBeforeRealWriteAdmission(bool dryRun)
    {
        var engine = Substitute.For<ITemplateInvocationEngine>();
        var template = Substitute.For<ITemplateInfo>();
        engine.ValidatePinnedPackageAsync(template, Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>(null));
        TemplateConfigurationSnapshot snapshot = new(
            """{"identity":"test","name":"test","shortName":"test","tags":{"type":"item"}}""", "fingerprint", TemplateType.Item, []);
        var request = new TemplateInvocationRequest(WorkingDirectory.FromExplicit(Path.GetTempPath()), new Dictionary<string, string?>(), DryRun: dryRun);
        engine.FileSystem.DirectoryExists(Path.GetFullPath(Path.Combine(request.OutputDirectory.Info.FullName, "."))).Returns(true);
        List<string> phases = [];
        engine.When(value => value.ThrowIfDisposed()).Do(_ => phases.Add("alive"));
        engine.ReadConfigurationAsync(template, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            phases.Add("read");
            return snapshot;
        });
        var effects = Substitute.For<ICreationEffects>();
        effects.FileChanges.Returns([]);
        var native = Substitute.For<ITemplateCreationResult>();
        native.Status.Returns(CreationResultStatus.Success);
        native.CreationEffects.Returns(effects);
        var creation = Substitute.For<ICreationResult>();
        creation.PostActions.Returns([]);
        native.CreationResult.Returns(creation);
        engine.RenderImageAsync(template, request, Arg.Any<InputDataSet>(), Arg.Any<TemplateOutputImage>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            phases.Add("render");
            return native;
        });
        var coordinator = new TemplateInvocationCoordinator(engine, new PhysicalFileSystem());
        using CancellationTokenSource cancellation = new();

        var result = await coordinator.InvokeAsync(template, snapshot, request, cancellation.Token);

        result.Status.Should().Be(TemplateInvocationStatus.Success);
        phases[0].Should().Be("alive");
        phases.Where(phase => phase != "alive").Should().Equal(dryRun ? ["read", "render", "read"] : ["read", "render", "read", "read"]);
        await engine.Received(dryRun ? 2 : 3).ReadConfigurationAsync(template, cancellation.Token);
        await engine.Received(1).RenderImageAsync(template, request, Arg.Any<InputDataSet>(), Arg.Any<TemplateOutputImage>(), cancellation.Token);
    }

    [Fact]
    public async Task InvokeAsync_ChangedSelectedSource_StopsBeforeParameterOrNativeEvaluation()
    {
        var engine = Substitute.For<ITemplateInvocationEngine>();
        var template = Substitute.For<ITemplateInfo>();
        engine.ValidatePinnedPackageAsync(template, Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>(null));
        TemplateConfigurationSnapshot selected = new("{}", "selected", TemplateType.Item, []);
        engine.ReadConfigurationAsync(template, Arg.Any<CancellationToken>()).Returns(new TemplateConfigurationSnapshot("{}", "changed", TemplateType.Item, []));
        var coordinator = new TemplateInvocationCoordinator(engine, new PhysicalFileSystem());
        var request = new TemplateInvocationRequest(WorkingDirectory.FromExplicit(Path.GetTempPath()), new Dictionary<string, string?>());

        var result = await coordinator.InvokeAsync(template, selected, request, CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.TemplateChanged);
        await engine.DidNotReceiveWithAnyArgs().RenderImageAsync(default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task InvokeAsync_DisposedEngine_StopsBeforeSourceReads()
    {
        var engine = Substitute.For<ITemplateInvocationEngine>();
        engine.When(value => value.ThrowIfDisposed()).Do(_ => throw new ObjectDisposedException("session"));
        var coordinator = new TemplateInvocationCoordinator(engine, new PhysicalFileSystem());
        var request = new TemplateInvocationRequest(WorkingDirectory.FromExplicit(Path.GetTempPath()), new Dictionary<string, string?>());

        Func<Task> invoke = () => coordinator.InvokeAsync(Substitute.For<ITemplateInfo>(), new("{}", "hash", TemplateType.Item, []), request, CancellationToken.None);

        await invoke.Should().ThrowAsync<ObjectDisposedException>();
        await engine.DidNotReceiveWithAnyArgs().ReadConfigurationAsync(default!, default);
    }

    [Fact]
    public void Constructor_NullEngine_Throws()
    {
        Action create = () => _ = new TemplateInvocationCoordinator(null!, new PhysicalFileSystem());

        create.Should().Throw<ArgumentNullException>().WithParameterName("engine");
    }

    [Fact]
    public void Constructor_NullFileSystem_Throws()
    {
        Action create = () => _ = new TemplateInvocationCoordinator(Substitute.For<ITemplateInvocationEngine>(), null!);

        create.Should().Throw<ArgumentNullException>().WithParameterName("fileSystem");
    }

    [Theory]
    [InlineData("template")]
    [InlineData("configuration")]
    [InlineData("request")]
    [InlineData("OutputDirectory")]
    [InlineData("Parameters")]
    public async Task InvokeAsync_NullArgument_ThrowsBeforeEngineOperations(string parameterName)
    {
        var engine = Substitute.For<ITemplateInvocationEngine>();
        var coordinator = new TemplateInvocationCoordinator(engine, new PhysicalFileSystem());
        var template = Substitute.For<ITemplateInfo>();
        TemplateConfigurationSnapshot configuration = new("{}", "hash", TemplateType.Item, []);
        var request = new TemplateInvocationRequest(
            parameterName == "OutputDirectory" ? null! : WorkingDirectory.FromExplicit(Path.GetTempPath()),
            parameterName == "Parameters" ? null! : new Dictionary<string, string?>());

        Func<Task> invoke = () => coordinator.InvokeAsync(parameterName == "template" ? null! : template,
            parameterName == "configuration" ? null! : configuration, parameterName == "request" ? null! : request, CancellationToken.None);

        await invoke.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName(parameterName is "OutputDirectory" or "Parameters" ? "request." + parameterName : parameterName);
        engine.DidNotReceive().ThrowIfDisposed();
        await engine.DidNotReceiveWithAnyArgs().ReadConfigurationAsync(default!, default);
    }

    [Fact]
    public async Task SelectedInvoker_DisposedOwner_StopsBeforeMountedReads()
    {
        var engine = Substitute.For<ITemplateInvocationEngine>();
        engine.When(value => value.ThrowIfDisposed()).Do(_ => throw new ObjectDisposedException("session"));
        var invoker = new SelectedTemplateInvoker(Substitute.For<ITemplateInfo>(), new("{}", "hash", TemplateType.Item, []), new(engine, new PhysicalFileSystem()));
        var request = new TemplateInvocationRequest(WorkingDirectory.FromExplicit(Path.GetTempPath()), new Dictionary<string, string?>());

        Func<Task> invoke = () => invoker.InvokeAsync(request, CancellationToken.None);

        await invoke.Should().ThrowAsync<ObjectDisposedException>();
        await engine.DidNotReceiveWithAnyArgs().ReadConfigurationAsync(default!, default);
    }

    [Theory]
    [InlineData(0, "template")]
    [InlineData(1, "configuration")]
    [InlineData(2, "coordinator")]
    public void SelectedInvoker_NullDependency_Throws(int missing, string parameterName)
    {
        Action create = () => _ = new SelectedTemplateInvoker(missing == 0 ? null! : Substitute.For<ITemplateInfo>(),
            missing == 1 ? null! : new("{}", "hash", TemplateType.Item, []),
            missing == 2 ? null! : new(Substitute.For<ITemplateInvocationEngine>(), new PhysicalFileSystem()));

        create.Should().Throw<ArgumentNullException>().WithParameterName(parameterName);
    }
}
