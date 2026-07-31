// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public class FuncTemplateEngineHostTests
{
    private static IFuncTemplateEngineVersion Version(string value = "1.0.0-test")
    {
        var version = Substitute.For<IFuncTemplateEngineVersion>();
        version.Version.Returns(value);
        return version;
    }

    [Fact]
    public void HostIdentifier_IsFunc()
    {
        using var host = new FuncTemplateEngineHost(NullLoggerFactory.Instance, Version(), []);

        host.HostIdentifier.Should().Be("func");
    }

    [Fact]
    public void Version_IsBridgedFromVersionProvider()
    {
        using var host = new FuncTemplateEngineHost(NullLoggerFactory.Instance, Version("5.0.0-preview.2"), []);

        host.Version.Should().Be("5.0.0-preview.2");
    }

    [Fact]
    public void FallbackHostTemplateConfigNames_IsEmpty()
    {
        using var host = new FuncTemplateEngineHost(NullLoggerFactory.Instance, Version(), []);

        host.FallbackHostTemplateConfigNames.Should().BeEmpty();
    }

    [Fact]
    public void BuiltInComponents_IncludeEngineDefaultsAndFuncContributions()
    {
        var marker = Substitute.For<Microsoft.TemplateEngine.Abstractions.IIdentifiedComponent>();
        var contribution = new FuncEngineComponent(typeof(Microsoft.TemplateEngine.Abstractions.IIdentifiedComponent), marker);

        using var host = new FuncTemplateEngineHost(NullLoggerFactory.Instance, Version(), [contribution]);

        host.BuiltInComponents.Should().NotBeEmpty();
        host.BuiltInComponents.Should().Contain(c => ReferenceEquals(c.Instance, marker));
    }

    [Fact]
    public void TryGetHostParamDefault_ReturnsHostIdentifier()
    {
        using var host = new FuncTemplateEngineHost(NullLoggerFactory.Instance, Version(), []);

        host.TryGetHostParamDefault("HostIdentifier", out var value).Should().BeTrue();
        value.Should().Be("func");
    }

    [Fact]
    public void TryGetHostParamDefault_ReturnsFalseForUnknownParam()
    {
        using var host = new FuncTemplateEngineHost(NullLoggerFactory.Instance, Version(), []);

        host.TryGetHostParamDefault("something-else", out var value).Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public void Constructor_ThrowsForNullLoggerFactory()
    {
        FluentActions.Invoking(() => new FuncTemplateEngineHost(null!, Version(), []))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ThrowsForNullVersion()
    {
        FluentActions.Invoking(() => new FuncTemplateEngineHost(NullLoggerFactory.Instance, null!, []))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ThrowsForNullComponents()
    {
        FluentActions.Invoking(() => new FuncTemplateEngineHost(NullLoggerFactory.Instance, Version(), null!))
            .Should().Throw<ArgumentNullException>();
    }
}
