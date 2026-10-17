// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class NativeTemplateConfiguredStackTests
{
    [Fact]
    public void SelectConfiguredStacks_StackOrigins_RetainsOnlyActualSuccessfulContributions()
    {
        ServiceCollection services = new();
        var preBoot = Stack("preboot");
        var successful = Stack("successful");
        var partial = Stack("partial");
        services.AddSingleton(preBoot);
        NativeTemplateWorkloadCapture capture = new();
        capture.Begin();
        capture.ObserveStackDescriptors(services, configured: false);
        services.AddSingleton(successful);
        capture.ObserveStackDescriptors(services, configured: true);
        services.AddSingleton(partial);
        capture.ObserveStackDescriptors(services, configured: false);
        capture.Complete(services);
        using var provider = services.BuildServiceProvider();

        var selected = capture.SelectConfiguredStacks(provider.GetServices<IProjectStack>().ToArray(), services);

        selected.Should().ContainSingle().Which.Should().BeSameAs(successful);
        provider.GetServices<IProjectStack>().Should().HaveCount(3);
    }

    [Fact]
    public void SelectConfiguredStacks_ReaddedFailedDescriptor_DoesNotRewriteItsConfigurationOrigin()
    {
        IServiceCollection services = new ServiceCollection();
        var failed = ServiceDescriptor.Singleton(Stack("failed"));
        var successful = ServiceDescriptor.Singleton(Stack("successful"));
        NativeTemplateWorkloadCapture capture = new();
        capture.Begin();
        capture.ObserveStackDescriptors(services, configured: false);
        services.Add(failed);
        capture.ObserveStackDescriptors(services, configured: false);
        services.Remove(failed);
        services.Add(successful);
        services.Add(failed);
        capture.ObserveStackDescriptors(services, configured: true);
        capture.Complete(services);
        using var provider = services.BuildServiceProvider();

        var selected = capture.SelectConfiguredStacks(provider.GetServices<IProjectStack>().ToArray(), services);

        selected.Should().ContainSingle().Which.Stack.Should().Be("successful");
    }

    [Fact]
    public void SelectConfiguredStacks_ConflictingPartialMetadata_IsExcludedBeforeSingleCatalogValidation()
    {
        ServiceCollection services = new();
        NativeTemplateWorkloadCapture capture = new();
        capture.Begin();
        services.AddSingleton(Stack("same"));
        capture.ObserveStackDescriptors(services, configured: true);
        services.AddSingleton(Stack("same"));
        capture.ObserveStackDescriptors(services, configured: false);
        capture.Complete(services);
        using var provider = services.BuildServiceProvider();
        var all = provider.GetServices<IProjectStack>().ToArray();

        var catalog = new InstalledProjectStackCatalog(capture.SelectConfiguredStacks(all, services));

        catalog.Stacks.Should().ContainSingle();
        catalog.FindStack("same").Should().NotBeNull();
        Action unfiltered = () => new InstalledProjectStackCatalog(all);
        unfiltered.Should().Throw<ConflictingProjectStackMetadataException>();
    }

    [Fact]
    public void SelectConfiguredStacks_SameCountDescriptorReplacement_RefusesPostBootMutation()
    {
        ServiceCollection services = new();
        var stack = Stack("configured");
        services.AddSingleton(stack);
        var capture = Completed(services);
        services[0] = ServiceDescriptor.Singleton(stack);

        Action select = () => capture.SelectConfiguredStacks([stack], services);

        select.Should().Throw<InvalidOperationException>().WithMessage("*changed after workload boot*");
    }

    [Fact]
    public void SelectConfiguredStacks_ReorderedDescriptors_RefusesPostBootMutation()
    {
        ServiceCollection services = new();
        var first = Stack("first");
        var second = Stack("second");
        services.AddSingleton(first);
        services.AddSingleton(second);
        var capture = Completed(services);
        (services[0], services[1]) = (services[1], services[0]);

        Action select = () => capture.SelectConfiguredStacks([second, first], services);

        select.Should().Throw<InvalidOperationException>().WithMessage("*changed after workload boot*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SelectConfiguredStacks_EnumerationCountMismatch_RefusesSelection(int count)
    {
        ServiceCollection services = new();
        var stack = Stack("configured");
        services.AddSingleton(stack);
        var capture = Completed(services);
        IProjectStack[] resolved = [.. Enumerable.Repeat(stack, count)];

        Action select = () => capture.SelectConfiguredStacks(resolved, services);

        select.Should().Throw<InvalidOperationException>().WithMessage("*enumeration does not match*");
    }

    [Fact]
    public void SelectConfiguredStacks_WrongResolvedInstance_RefusesEvenWithMatchingCount()
    {
        ServiceCollection services = new();
        services.AddSingleton(Stack("configured"));
        var capture = Completed(services);

        Action select = () => capture.SelectConfiguredStacks([Stack("foreign")], services);

        select.Should().Throw<InvalidOperationException>().WithMessage("*instances do not match*");
    }

    [Fact]
    public void SelectConfiguredStacks_ForeignServiceCollection_RefusesCopiedDescriptors()
    {
        ServiceCollection services = new();
        var stack = Stack("configured");
        services.AddSingleton(stack);
        var capture = Completed(services);
        IServiceCollection other = new ServiceCollection();
        other.Add(services[0]);

        Action select = () => capture.SelectConfiguredStacks([stack], other);

        select.Should().Throw<InvalidOperationException>().WithMessage("*service collection used by the captured boot*");
    }

    [Fact]
    public void SelectConfiguredStacks_NoDescriptorProof_RefusesRuntimeOnlyCapture()
    {
        NativeTemplateWorkloadCapture capture = new();
        capture.Begin();
        capture.Complete();

        Action select = () => capture.SelectConfiguredStacks([], new ServiceCollection());

        select.Should().Throw<InvalidOperationException>().WithMessage("*descriptor provenance*");
    }

    [Fact]
    public void SelectConfiguredStacks_KeyedOrUnrelatedServices_DoNotChangeUnkeyedStackInventory()
    {
        ServiceCollection services = new();
        var stack = Stack("configured");
        services.AddSingleton(stack);
        var capture = Completed(services);
        services.AddKeyedSingleton<IProjectStack>("other", Stack("keyed"));
        services.AddSingleton(new object());
        using var provider = services.BuildServiceProvider();

        var selected = capture.SelectConfiguredStacks(provider.GetServices<IProjectStack>().ToArray(), services);

        selected.Should().ContainSingle().Which.Should().BeSameAs(stack);
    }

    [Fact]
    public void SelectConfiguredStacks_NullInputs_Throws()
    {
        ServiceCollection services = new();
        var capture = Completed(services);
        Action nullStacks = () => capture.SelectConfiguredStacks(null!, services);
        Action nullServices = () => capture.SelectConfiguredStacks([], null!);

        nullStacks.Should().Throw<ArgumentNullException>().WithParameterName("stacks");
        nullServices.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    private static NativeTemplateWorkloadCapture Completed(IServiceCollection services)
    {
        NativeTemplateWorkloadCapture capture = new();
        capture.Begin();
        capture.ObserveStackDescriptors(services, configured: true);
        capture.Complete(services);
        return capture;
    }

    private static IProjectStack Stack(string name)
    {
        var stack = Substitute.For<IProjectStack>();
        stack.Stack.Returns(name);
        stack.DisplayName.Returns(name);
        stack.SupportedLanguages.Returns([name]);
        stack.SupportedLanguageAliases.Returns(new Dictionary<string, IReadOnlyList<string>>());
        return stack;
    }
}
