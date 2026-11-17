// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.TemplateEngine.Abstractions;

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
            descriptor.ServiceType == typeof(IWorkloadTemplateSource)
            && descriptor.ImplementationType == typeof(WorkloadTemplateSource)
            && descriptor.Lifetime == ServiceLifetime.Singleton);
        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(ITemplateComponent)
            && descriptor.ImplementationFactory != null
            && descriptor.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void Get_NoCustomComponents_IncludesEdgeComponents()
    {
        IReadOnlyList<(Type Type, IIdentifiedComponent Instance)> components = FuncTemplateComponents.Get([]);

        foreach ((Type type, IIdentifiedComponent instance) in Microsoft.TemplateEngine.Edge.Components.AllComponents)
        {
            components.Should().Contain(component => component.Type == type && component.Instance.Id == instance.Id);
        }
    }

    [Fact]
    public void Get_NoCustomComponents_IncludesRunnableProjectComponents()
    {
        IReadOnlyList<(Type Type, IIdentifiedComponent Instance)> components = FuncTemplateComponents.Get([]);

        foreach ((Type type, IIdentifiedComponent instance) in Microsoft.TemplateEngine.Orchestrator.RunnableProjects.Components.AllComponents)
        {
            components.Should().Contain(component => component.Type == type && component.Instance.Id == instance.Id);
        }
    }

    [Fact]
    public async Task AddTemplateComponent_RegistersSingletonWithDeclaredInterfaceAndInstance()
    {
        ServiceCollection services = new();
        Component component = new();
        services.AddSingleton(component);
        services.AddTemplateComponent<ITestComponent, Component>();

        await using ServiceProvider provider = services.BuildServiceProvider();
        ITemplateComponent registration = provider.GetServices<ITemplateComponent>().Single();
        registration.InterfaceType.Should().Be<ITestComponent>();
        registration.Instance.Should().BeSameAs(component);
        provider.GetServices<ITemplateComponent>().Single().Should().BeSameAs(registration);
    }

    [Fact]
    public async Task AddTemplateComponent_UnregisteredImplementation_ActivatesWithDependencies()
    {
        ServiceCollection services = new();
        services.AddTemplateComponent<IIdentifiedComponent, Component>();

        await using ServiceProvider provider = services.BuildServiceProvider();
        ITemplateComponent registration = provider.GetServices<ITemplateComponent>().Single();
        registration.InterfaceType.Should().Be<IIdentifiedComponent>();
        registration.Instance.Should().BeOfType<Component>();
        provider.GetServices<ITemplateComponent>().Single().Should().BeSameAs(registration);
    }

    private interface ITestComponent
    {
    }

    private sealed class Component : IIdentifiedComponent, ITestComponent
    {
        public Guid Id { get; } = Guid.NewGuid();
    }
}
