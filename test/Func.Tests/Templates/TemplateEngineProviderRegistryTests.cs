// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Templates;

public class TemplateEngineProviderRegistryTests
{
    [Fact]
    public void Empty_Registry_TryGet_Returns_Null()
    {
        var registry = new TemplateEngineProviderRegistry([]);

        Assert.Null(registry.TryGet(EngineIds.V2));
        Assert.Null(registry.TryGet(EngineIds.DotNet));
        Assert.Empty(registry.Providers);
    }

    [Fact]
    public void TryGet_Returns_Registered_Provider()
    {
        ITemplateEngineProvider v2 = FakeProvider(EngineIds.V2);
        ITemplateEngineProvider dotnet = FakeProvider(EngineIds.DotNet);
        var registry = new TemplateEngineProviderRegistry([v2, dotnet]);

        Assert.Same(v2, registry.TryGet(EngineIds.V2));
        Assert.Same(dotnet, registry.TryGet(EngineIds.DotNet));
    }

    [Fact]
    public void TryGet_Is_Case_Insensitive()
    {
        ITemplateEngineProvider v2 = FakeProvider(EngineIds.V2);
        var registry = new TemplateEngineProviderRegistry([v2]);

        Assert.Same(v2, registry.TryGet("V2"));
        Assert.Same(v2, registry.TryGet("v2"));
    }

    [Fact]
    public void Duplicate_EngineId_Throws()
    {
        ITemplateEngineProvider a = FakeProvider(EngineIds.V2);
        ITemplateEngineProvider b = FakeProvider(EngineIds.V2);

        Assert.Throws<InvalidOperationException>(() => new TemplateEngineProviderRegistry([a, b]));
    }

    [Fact]
    public void Empty_EngineId_Throws()
    {
        ITemplateEngineProvider bad = FakeProvider(string.Empty);

        Assert.Throws<InvalidOperationException>(() => new TemplateEngineProviderRegistry([bad]));
    }

    private static ITemplateEngineProvider FakeProvider(string engineId)
    {
        var provider = Substitute.For<ITemplateEngineProvider>();
        provider.EngineId.Returns(engineId);
        return provider;
    }
}
