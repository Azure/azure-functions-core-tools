// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Azure.Functions.Cli.Tests.Hosting;

public class DefaultFunctionsCliBuilderTests
{
    [Fact]
    public void Services_ReturnsBackingCollection()
    {
        var services = new ServiceCollection();
        var builder = new DefaultFunctionsCliBuilder(services);

        Assert.Same(services, builder.Services);
    }

    [Fact]
    public void Ctor_NullServices_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DefaultFunctionsCliBuilder(null!));
    }

    [Fact]
    public void RegisterCommand_Instance_ProducesExternalCommandWithWorkloadInfo()
    {
        var services = new ServiceCollection();
        var workload = TestWorkloads.CreateInfo("My.Workload");
        var builder = new DefaultFunctionsCliBuilder(services, workload);
        var stub = new TestWorkloads.StubFuncCommand("hello");

        builder.RegisterCommand(stub);

        var resolved = services.BuildServiceProvider().GetServices<FuncCliCommand>().ToList();
        var external = Assert.Single(resolved.OfType<ExternalCommand>());
        Assert.Same(workload, external.Workload);
        Assert.Same(stub, external.Source);
        Assert.Equal("hello", external.Name);
    }

    [Fact]
    public void RegisterCommand_Generic_ResolvesThroughDi()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new GenericTestPayload("from-di"));
        var workload = TestWorkloads.CreateInfo();
        var builder = new DefaultFunctionsCliBuilder(services, workload);

        builder.RegisterCommand<GenericTestCommand>();

        var resolved = services.BuildServiceProvider().GetServices<FuncCliCommand>().ToList();
        var external = Assert.Single(resolved.OfType<ExternalCommand>());
        var source = Assert.IsType<GenericTestCommand>(external.Source);
        Assert.Equal("from-di", source.Payload.Value);
    }

    [Fact]
    public void RegisterCommand_Generic_AbstractType_Throws()
    {
        var services = new ServiceCollection();
        var builder = new DefaultFunctionsCliBuilder(services, TestWorkloads.CreateInfo());

        var ex = Assert.Throws<ArgumentException>(() => builder.RegisterCommand<AbstractFuncCommand>());
        Assert.Contains("abstract command type", ex.Message);
        Assert.Contains(nameof(AbstractFuncCommand), ex.Message);
    }

    [Fact]
    public void RegisterCommand_Factory_InvokedPerResolution()
    {
        var services = new ServiceCollection();
        var workload = TestWorkloads.CreateInfo();
        var builder = new DefaultFunctionsCliBuilder(services, workload);

        builder.RegisterCommand(_ => new TestWorkloads.StubFuncCommand("from-factory"));

        var resolved = services.BuildServiceProvider().GetServices<FuncCliCommand>().ToList();
        var external = Assert.Single(resolved.OfType<ExternalCommand>());
        Assert.Equal("from-factory", external.Name);
    }

    [Fact]
    public void RegisterCommand_Factory_Null_Throws()
    {
        var builder = new DefaultFunctionsCliBuilder(new ServiceCollection(), TestWorkloads.CreateInfo());

        Assert.Throws<ArgumentNullException>(
            () => builder.RegisterCommand((Func<IServiceProvider, FuncCommand>)null!));
    }

    [Fact]
    public void RegisterCommand_Factory_ReturnsNull_FailsAtResolutionTime()
    {
        var services = new ServiceCollection();
        var workload = TestWorkloads.CreateInfo("Workload.With.NullFactory");
        var builder = new DefaultFunctionsCliBuilder(services, workload);

        builder.RegisterCommand(_ => null!);

        var sp = services.BuildServiceProvider();
        var ex = Assert.Throws<InvalidOperationException>(() => sp.GetServices<FuncCliCommand>().ToList());
        Assert.Contains("Workload.With.NullFactory", ex.Message);
    }

    [Fact]
    public void RegisterCommand_Instance_Null_Throws()
    {
        var builder = new DefaultFunctionsCliBuilder(new ServiceCollection(), TestWorkloads.CreateInfo());

        Assert.Throws<ArgumentNullException>(() => builder.RegisterCommand((FuncCommand)null!));
    }

    [Fact]
    public void RegisterCommand_WithoutWorkloadContext_Throws()
    {
        var builder = new DefaultFunctionsCliBuilder(new ServiceCollection());

        var ex = Assert.Throws<InvalidOperationException>(
            () => builder.RegisterCommand(new TestWorkloads.StubFuncCommand("oops")));
        Assert.Contains("workload-scoped builder", ex.Message);
    }

    [Fact]
    public void RegisterCommand_Generic_WithoutWorkloadContext_Throws()
    {
        var builder = new DefaultFunctionsCliBuilder(new ServiceCollection());

        Assert.Throws<InvalidOperationException>(() => builder.RegisterCommand<GenericTestCommand>());
    }

    [Fact]
    public void RegisterCommand_Factory_WithoutWorkloadContext_Throws()
    {
        var builder = new DefaultFunctionsCliBuilder(new ServiceCollection());

        Assert.Throws<InvalidOperationException>(
            () => builder.RegisterCommand(_ => new TestWorkloads.StubFuncCommand("oops")));
    }

    private sealed class GenericTestPayload(string value)
    {
        public string Value { get; } = value;
    }

    private sealed class GenericTestCommand(GenericTestPayload payload) : FuncCommand
    {
        public GenericTestPayload Payload { get; } = payload;

        public override string Name => "generic";

        public override string Description => "Generic test command.";

        public override Task<int> ExecuteAsync(
            FuncCommandInvocationContext context,
            CancellationToken cancellationToken) => Task.FromResult(0);
    }

    private abstract class AbstractFuncCommand : FuncCommand
    {
    }
}
