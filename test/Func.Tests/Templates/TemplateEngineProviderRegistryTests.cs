// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public class TemplateEngineProviderRegistryTests
{
    [Fact]
    public void Empty_Registry_TryGet_Returns_Null()
    {
        var registry = new TemplateEngineProviderRegistry([]);

        registry.TryGet(EngineIds.V2).Should().BeNull();
        registry.TryGet(EngineIds.DotNet).Should().BeNull();
        registry.Providers.Should().BeEmpty();
    }

    [Fact]
    public void TryGet_Returns_Registered_Provider()
    {
        ITemplateEngineProvider v2 = FakeProvider(EngineIds.V2);
        ITemplateEngineProvider dotnet = FakeProvider(EngineIds.DotNet);
        var registry = new TemplateEngineProviderRegistry([v2, dotnet]);

        registry.TryGet(EngineIds.V2).Should().BeSameAs(v2);
        registry.TryGet(EngineIds.DotNet).Should().BeSameAs(dotnet);
    }

    [Fact]
    public void TryGet_Is_Case_Insensitive()
    {
        ITemplateEngineProvider v2 = FakeProvider(EngineIds.V2);
        var registry = new TemplateEngineProviderRegistry([v2]);

        registry.TryGet("V2").Should().BeSameAs(v2);
        registry.TryGet("v2").Should().BeSameAs(v2);
    }

    [Fact]
    public void Duplicate_EngineId_Throws()
    {
        ITemplateEngineProvider a = FakeProvider(EngineIds.V2);
        ITemplateEngineProvider b = FakeProvider(EngineIds.V2);

        FluentActions.Invoking(() => new TemplateEngineProviderRegistry([a, b])).Should().ThrowExactly<InvalidOperationException>();
    }

    [Fact]
    public void Empty_EngineId_Throws()
    {
        ITemplateEngineProvider bad = FakeProvider(string.Empty);

        FluentActions.Invoking(() => new TemplateEngineProviderRegistry([bad])).Should().ThrowExactly<InvalidOperationException>();
    }

    private static ITemplateEngineProvider FakeProvider(string engineId)
    {
        var provider = Substitute.For<ITemplateEngineProvider>();
        provider.EngineId.Returns(engineId);
        return provider;
    }
}
