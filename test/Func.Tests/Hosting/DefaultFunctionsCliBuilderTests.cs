// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.PostActions;
using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Hosting;

public class DefaultFunctionsCliBuilderTests
{
    [Fact]
    public void Services_ReturnsBackingCollection()
    {
        var services = new ServiceCollection();
        var builder = new DefaultFunctionsCliBuilder(services);

        builder.Services.Should().BeSameAs(services);
    }

    [Fact]
    public void Ctor_NullServices_Throws()
    {
        FluentActions.Invoking(() => new DefaultFunctionsCliBuilder(null!)).Should().ThrowExactly<ArgumentNullException>();
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
        var external = resolved.OfType<ExternalCommand>().Should().ContainSingle().Subject;
        external.Workload.Should().BeSameAs(workload);
        external.Source.Should().BeSameAs(stub);
        external.Name.Should().Be("hello");
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
        var external = resolved.OfType<ExternalCommand>().Should().ContainSingle().Subject;
        external.Source.Should().BeOfType<GenericTestCommand>().Which.Payload.Value.Should().Be("from-di");
    }

    [Fact]
    public void RegisterCommand_Generic_DoesNotRegisterCommandTypeInDi()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new GenericTestPayload("from-di"));
        var builder = new DefaultFunctionsCliBuilder(services, TestWorkloads.CreateInfo());

        builder.RegisterCommand<GenericTestCommand>();

        var sp = services.BuildServiceProvider();
        sp.GetService<GenericTestCommand>().Should().BeNull();
    }

    [Fact]
    public void RegisterCommand_Generic_AbstractType_Throws()
    {
        var services = new ServiceCollection();
        var builder = new DefaultFunctionsCliBuilder(services, TestWorkloads.CreateInfo());

        var ex = FluentActions.Invoking(builder.RegisterCommand<AbstractFuncCommand>).Should().ThrowExactly<ArgumentException>().Which;
        ex.Message.Should().Contain("abstract command type");
        ex.Message.Should().Contain(nameof(AbstractFuncCommand));
    }

    [Fact]
    public void RegisterCommand_Type_ResolvesThroughDi()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new GenericTestPayload("from-di"));
        var workload = TestWorkloads.CreateInfo();
        var builder = new DefaultFunctionsCliBuilder(services, workload);

        builder.RegisterCommand(typeof(GenericTestCommand));

        var resolved = services.BuildServiceProvider().GetServices<FuncCliCommand>().ToList();
        var external = resolved.OfType<ExternalCommand>().Should().ContainSingle().Subject;
        external.Source.Should().BeOfType<GenericTestCommand>().Which.Payload.Value.Should().Be("from-di");
    }

    [Fact]
    public void RegisterCommand_Type_DoesNotRegisterCommandTypeInDi()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new GenericTestPayload("from-di"));
        var builder = new DefaultFunctionsCliBuilder(services, TestWorkloads.CreateInfo());

        builder.RegisterCommand(typeof(GenericTestCommand));

        var sp = services.BuildServiceProvider();
        sp.GetService<GenericTestCommand>().Should().BeNull();
    }

    [Fact]
    public void RegisterCommand_Type_NullType_Throws()
    {
        var builder = new DefaultFunctionsCliBuilder(new ServiceCollection(), TestWorkloads.CreateInfo());

        FluentActions.Invoking(() => builder.RegisterCommand((Type)null!)).Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public void RegisterCommand_Type_NotFuncCommand_Throws()
    {
        var builder = new DefaultFunctionsCliBuilder(new ServiceCollection(), TestWorkloads.CreateInfo());

        var ex = FluentActions.Invoking(() => builder.RegisterCommand(typeof(string))).Should().ThrowExactly<ArgumentException>().Which;
        ex.Message.Should().Contain("not assignable");
        ex.Message.Should().Contain(nameof(FuncCommand));
    }

    [Fact]
    public void RegisterCommand_Type_AbstractType_Throws()
    {
        var builder = new DefaultFunctionsCliBuilder(new ServiceCollection(), TestWorkloads.CreateInfo());

        var ex = FluentActions.Invoking(() => builder.RegisterCommand(typeof(AbstractFuncCommand))).Should().ThrowExactly<ArgumentException>().Which;
        ex.Message.Should().Contain("abstract command type");
        ex.Message.Should().Contain(nameof(AbstractFuncCommand));
    }

    [Fact]
    public void RegisterCommand_Instance_Null_Throws()
    {
        var builder = new DefaultFunctionsCliBuilder(new ServiceCollection(), TestWorkloads.CreateInfo());

        FluentActions.Invoking(() => builder.RegisterCommand((FuncCommand)null!)).Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public void RegisterCommand_WithoutWorkloadContext_Throws()
    {
        var builder = new DefaultFunctionsCliBuilder(new ServiceCollection());

        var ex = FluentActions.Invoking(() => builder.RegisterCommand(new TestWorkloads.StubFuncCommand("oops"))).Should().ThrowExactly<InvalidOperationException>().Which;
        ex.Message.Should().Contain("workload-scoped builder");
    }

    [Fact]
    public void RegisterCommand_Generic_WithoutWorkloadContext_Throws()
    {
        var builder = new DefaultFunctionsCliBuilder(new ServiceCollection());

        FluentActions.Invoking(builder.RegisterCommand<GenericTestCommand>).Should().ThrowExactly<InvalidOperationException>();
    }

    [Fact]
    public void RegisterCommand_Type_WithoutWorkloadContext_Throws()
    {
        var builder = new DefaultFunctionsCliBuilder(new ServiceCollection());

        FluentActions.Invoking(() => builder.RegisterCommand(typeof(GenericTestCommand))).Should().ThrowExactly<InvalidOperationException>();
    }

    [Fact]
    public void AddProjectFactory_ProducesRegistrationWithWorkloadInfo()
    {
        var services = new ServiceCollection();
        var workload = TestWorkloads.CreateInfo("My.Workload");
        var builder = new DefaultFunctionsCliBuilder(services, workload);
        var factory = Substitute.For<IFunctionsProjectFactory>();

        builder.AddProjectFactory(factory);

        var registration = services.BuildServiceProvider().GetServices<WorkloadProjectFactoryRegistration>().Should().ContainSingle().Subject;
        registration.Workload.Should().BeSameAs(workload);
        registration.Factory.Should().BeSameAs(factory);
    }

    [Fact]
    public void AddProjectFactory_NullFactory_Throws()
    {
        var builder = new DefaultFunctionsCliBuilder(new ServiceCollection(), TestWorkloads.CreateInfo());

        FluentActions.Invoking(() => builder.AddProjectFactory(null!)).Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public void AddProjectFactory_WithoutWorkloadContext_Throws()
    {
        var builder = new DefaultFunctionsCliBuilder(new ServiceCollection());
        var factory = Substitute.For<IFunctionsProjectFactory>();

        var ex = FluentActions.Invoking(() => builder.AddProjectFactory(factory)).Should().ThrowExactly<InvalidOperationException>().Which;
        ex.Message.Should().Contain("workload-scoped builder");
    }

    private sealed class GenericTestPayload(string value)
    {
        public string Value { get; } = value;
    }

    [Fact]
    public async Task AddTemplateComponent_Instance_RetainsOwnerAndReusesSingleton()
    {
        ServiceCollection services = new();
        var workload = TestWorkloads.CreateInfo();
        var builder = new DefaultFunctionsCliBuilder(services, workload);
        var processor = Substitute.For<IPostActionProcessor>();

        builder.AddTemplateComponent(processor);

        await using var provider = services.BuildServiceProvider();
        var registration = provider.GetServices<WorkloadTemplateComponentRegistration>().Single();
        registration.Workload.Should().BeSameAs(workload);
        registration.Component.Should().BeSameAs(processor);
        provider.GetServices<WorkloadTemplateComponentRegistration>().Single().Should().BeSameAs(registration);
    }

    [Fact]
    public void AddTemplateComponent_Null_Throws()
    {
        var builder = new DefaultFunctionsCliBuilder(new ServiceCollection(), TestWorkloads.CreateInfo());

        FluentActions.Invoking(() => builder.AddTemplateComponent(null!))
            .Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public async Task AddTemplateComponent_UnsupportedKind_RetainsInstanceAndGenericRegistrations()
    {
        ServiceCollection services = new();
        var workload = TestWorkloads.CreateInfo();
        var builder = new DefaultFunctionsCliBuilder(services, workload);
        var component = Substitute.For<ITemplateComponent>();

        builder.AddTemplateComponent(component);
        builder.AddTemplateComponent<UnsupportedComponent>();

        await using var provider = services.BuildServiceProvider();
        var registrations = provider.GetServices<WorkloadTemplateComponentRegistration>().ToArray();
        registrations.Should().HaveCount(2).And.AllSatisfy(registration => registration.Workload.Should().BeSameAs(workload));
        registrations[0].Component.Should().BeSameAs(component);
        registrations[1].Component.Should().BeOfType<UnsupportedComponent>();
    }

    [Fact]
    public void AddTemplateComponent_AbstractType_Throws()
    {
        var builder = new DefaultFunctionsCliBuilder(new ServiceCollection(), TestWorkloads.CreateInfo());

        FluentActions.Invoking(builder.AddTemplateComponent<AbstractProcessor>)
            .Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void AddTemplateComponent_WithoutWorkload_Throws()
    {
        var builder = new DefaultFunctionsCliBuilder(new ServiceCollection());

        FluentActions.Invoking(() => builder.AddTemplateComponent(Substitute.For<IPostActionProcessor>()))
            .Should().ThrowExactly<InvalidOperationException>();
        FluentActions.Invoking(builder.AddTemplateComponent<DisposableProcessor>)
            .Should().ThrowExactly<InvalidOperationException>();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task AddTemplateComponent_Generic_ResolvesDependenciesAndDisposesOnlyOwnedInstances(
        bool registerInDi, bool instanceRegistration)
    {
        ServiceCollection services = new();
        DisposalCounter counter = new();
        services.AddSingleton(counter);
        var builder = new DefaultFunctionsCliBuilder(services, TestWorkloads.CreateInfo());
        DisposableProcessor external = new(counter);
        if (registerInDi)
        {
            services.AddSingleton<DisposableProcessor>();
        }

        if (instanceRegistration)
        {
            builder.AddTemplateComponent(external);
        }
        else
        {
            builder.AddTemplateComponent<DisposableProcessor>();
        }

        await using (var provider = services.BuildServiceProvider())
        {
            var registration = provider.GetServices<WorkloadTemplateComponentRegistration>().Single();
            registration.Component.Should().BeOfType<DisposableProcessor>();
            provider.GetServices<WorkloadTemplateComponentRegistration>().Single().Should().BeSameAs(registration);
            if (registerInDi)
            {
                registration.Component.Should().BeSameAs(provider.GetRequiredService<DisposableProcessor>());
            }

            if (instanceRegistration)
            {
                registration.Component.Should().BeSameAs(external);
            }
        }

        counter.AsyncDisposals.Should().Be(instanceRegistration ? 0 : 1);
        counter.SyncDisposals.Should().Be(0);
    }

    private sealed class UnsupportedComponent : ITemplateComponent
    {
        public Guid Id => Guid.Empty;
    }

    private abstract class AbstractProcessor : IPostActionProcessor
    {
        public abstract Guid Id { get; }

        public abstract bool CanProcess(PostActionSelectionContext context);

        public abstract Task<bool> ProcessAsync(PostActionContext context, CancellationToken cancellationToken);
    }

    private sealed class DisposalCounter
    {
        public int AsyncDisposals { get; set; }

        public int SyncDisposals { get; set; }
    }

    private sealed class DisposableProcessor(DisposalCounter counter) : IPostActionProcessor, IAsyncDisposable, IDisposable
    {
        public Guid Id { get; } = Guid.NewGuid();

        public bool CanProcess(PostActionSelectionContext context) => true;

        public Task<bool> ProcessAsync(PostActionContext context, CancellationToken cancellationToken) => Task.FromResult(true);

        public ValueTask DisposeAsync()
        {
            counter.AsyncDisposals++;
            return ValueTask.CompletedTask;
        }

        public void Dispose() => counter.SyncDisposals++;
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
