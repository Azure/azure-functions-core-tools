// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.PostActions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.TemplateEngine.Abstractions;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class FuncTemplateComponentsTests
{
    [Fact]
    public void AddTemplating_RegistersTemplatingServices()
    {
        ServiceCollection services = new();

        services.AddTemplating();

        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(ITemplaterFactory)
            && descriptor.ImplementationType == typeof(TemplaterFactory)
            && descriptor.Lifetime == ServiceLifetime.Singleton);
        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(IFuncTemplateComponentProvider)
            && descriptor.ImplementationType == typeof(FuncTemplateComponents)
            && descriptor.Lifetime == ServiceLifetime.Singleton);
        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(IWorkloadTemplateSource)
            && descriptor.ImplementationType == typeof(WorkloadTemplateSource)
            && descriptor.Lifetime == ServiceLifetime.Singleton);
        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(PostActionDispatcher)
            && descriptor.Lifetime == ServiceLifetime.Singleton);
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(ICoreTemplateComponent)
            && descriptor.ImplementationFactory != null
            && descriptor.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void Get_NoCustomComponents_IncludesEdgeComponents()
    {
        IReadOnlyList<ICoreTemplateComponent> components =
            new FuncTemplateComponents([], [], Substitute.For<IInteractionService>()).Get();

        foreach ((Type type, IIdentifiedComponent instance) in Microsoft.TemplateEngine.Edge.Components.AllComponents)
        {
            components.Should().Contain(component => component.InterfaceType == type && component.Instance.Id == instance.Id);
        }
    }

    [Fact]
    public void Get_NoCustomComponents_IncludesRunnableProjectComponents()
    {
        IReadOnlyList<ICoreTemplateComponent> components =
            new FuncTemplateComponents([], [], Substitute.For<IInteractionService>()).Get();

        foreach ((Type type, IIdentifiedComponent instance) in Microsoft.TemplateEngine.Orchestrator.RunnableProjects.Components.AllComponents)
        {
            components.Should().Contain(component => component.InterfaceType == type && component.Instance.Id == instance.Id);
        }
    }

    /// <summary>
    /// Verifies that shared action IDs produce one bridge and unsupported contracts are ignored.
    /// </summary>
    [Fact]
    public void Get_WorkloadsSharingActionId_ReturnsOneBridgeAndIgnoresUnsupportedContracts()
    {
        var actionId = Guid.NewGuid();
        var first = Substitute.For<IPostActionProcessor>();
        first.Id.Returns(actionId);
        var second = Substitute.For<IPostActionProcessor>();
        second.Id.Returns(actionId);
        var unsupported = Substitute.For<ITemplateComponent>();
        unsupported.Id.Returns(Guid.NewGuid());
        FuncTemplateComponents provider = new(
            [],
            [
                new(TestWorkloads.CreateInfo("First.Workload"), first, false),
                new(TestWorkloads.CreateInfo("Second.Workload"), second, false),
                new(TestWorkloads.CreateInfo("Future.Workload"), unsupported, false),
            ],
            Substitute.For<IInteractionService>());

        var components = provider.Get();

        components.Where(component => component.InterfaceType == typeof(ICorePostActionProcessor))
            .Should().ContainSingle().Which.Instance.Should().BeOfType<WorkloadPostActionProcessor>()
            .Which.Id.Should().Be(actionId);
        components.Should().NotContain(component => ReferenceEquals(component.Instance, unsupported));
        first.DidNotReceiveWithAnyArgs().CanProcess(default!);
        second.DidNotReceiveWithAnyArgs().CanProcess(default!);
    }

    /// <summary>
    /// Verifies that a matching core processor takes precedence over a workload bridge.
    /// </summary>
    [Fact]
    public void Get_CoreComponent_ReturnsOriginalInstanceInsteadOfWorkloadBridge()
    {
        var actionId = Guid.NewGuid();
        var core = Substitute.For<ICorePostActionProcessor>();
        core.Id.Returns(actionId);
        var registration = Substitute.For<ICoreTemplateComponent>();
        registration.InterfaceType.Returns(typeof(ICorePostActionProcessor));
        registration.Instance.Returns(core);
        var workload = Substitute.For<IPostActionProcessor>();
        workload.Id.Returns(actionId);
        FuncTemplateComponents provider = new(
            [registration],
            [new(TestWorkloads.CreateInfo(), workload, false)],
            Substitute.For<IInteractionService>());

        var components = provider.Get();

        components.Where(component => component.InterfaceType == typeof(ICorePostActionProcessor))
            .Should().ContainSingle().Which.Instance.Should().BeSameAs(core);
    }

    /// <summary>
    /// Verifies that workload IDs cannot conflict with a core component of another kind.
    /// </summary>
    [Fact]
    public void Get_WorkloadIdMatchingDifferentCoreComponent_IsIgnored()
    {
        var id = Guid.NewGuid();
        var core = Substitute.For<IIdentifiedComponent>();
        core.Id.Returns(id);
        var registration = Substitute.For<ICoreTemplateComponent>();
        registration.InterfaceType.Returns(typeof(IIdentifiedComponent));
        registration.Instance.Returns(core);
        var workload = Substitute.For<IPostActionProcessor>();
        workload.Id.Returns(id);
        FuncTemplateComponents provider = new(
            [registration],
            [new(TestWorkloads.CreateInfo(), workload, false)],
            Substitute.For<IInteractionService>());

        var components = provider.Get();

        components.Where(component => component.InterfaceType == typeof(ICorePostActionProcessor))
            .Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that the component provider rejects null dependencies.
    /// </summary>
    [Fact]
    public void Constructor_NullDependencies_Throws()
    {
        var interaction = Substitute.For<IInteractionService>();

        FluentActions.Invoking(() => new FuncTemplateComponents(null!, [], interaction))
            .Should().Throw<ArgumentNullException>().WithParameterName("components");
        FluentActions.Invoking(() => new FuncTemplateComponents([], null!, interaction))
            .Should().Throw<ArgumentNullException>().WithParameterName("workloadComponents");
        FluentActions.Invoking(() => new FuncTemplateComponents([], [], null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("interaction");
    }

    [Fact]
    public async Task AddTemplateComponent_RegistersSingletonWithDeclaredInterfaceAndInstance()
    {
        ServiceCollection services = new();
        Component component = new();
        services.AddSingleton(component);
        services.AddTemplateComponent<ITestComponent, Component>();

        await using ServiceProvider provider = services.BuildServiceProvider();
        ICoreTemplateComponent registration = provider.GetServices<ICoreTemplateComponent>().Single();
        registration.InterfaceType.Should().Be<ITestComponent>();
        registration.Instance.Should().BeSameAs(component);
        provider.GetServices<ICoreTemplateComponent>().Single().Should().BeSameAs(registration);
    }

    [Fact]
    public async Task AddTemplateComponent_UnregisteredImplementation_ActivatesWithDependencies()
    {
        ServiceCollection services = new();
        services.AddTemplateComponent<IIdentifiedComponent, Component>();

        await using ServiceProvider provider = services.BuildServiceProvider();
        ICoreTemplateComponent registration = provider.GetServices<ICoreTemplateComponent>().Single();
        registration.InterfaceType.Should().Be<IIdentifiedComponent>();
        registration.Instance.Should().BeOfType<Component>();
        provider.GetServices<ICoreTemplateComponent>().Single().Should().BeSameAs(registration);
    }

    private interface ITestComponent
    {
    }

    private sealed class Component : IIdentifiedComponent, ITestComponent
    {
        public Guid Id { get; } = Guid.NewGuid();
    }
}
