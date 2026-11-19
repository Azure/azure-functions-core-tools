// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Settings;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class FuncTemplateInfoTests
{
    [Fact]
    public void Constructor_NullTemplate_Throws()
    {
        Action act = () => _ = new FuncTemplateInfo(null!, FuncTemplateHostData.Empty);

        act.Should().Throw<ArgumentNullException>().WithParameterName("template");
    }

    [Fact]
    public void Constructor_NullHostData_Throws()
    {
        Action act = () => _ = new FuncTemplateInfo(Substitute.For<ITemplateInfo>(), null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("hostData");
    }

    [Fact]
    public void Constructor_UsesProvidedHostDataWithoutReadingTemplate()
    {
        var template = Substitute.For<ITemplateInfo>();
        FuncTemplateHostData hostData = FuncTemplateHostData.Empty;

        FuncTemplateInfo info = new(template, hostData);

        info.Template.Should().BeSameAs(template);
        info.HostData.Should().BeSameAs(hostData);
        template.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void Create_NullTemplate_Throws()
    {
        Action act = () => FuncTemplateInfo.Create(null!, Substitute.For<IEngineEnvironmentSettings>());

        act.Should().Throw<ArgumentNullException>().WithParameterName("template");
    }

    [Fact]
    public void Create_NullSettings_Throws()
    {
        Action act = () => FuncTemplateInfo.Create(Substitute.For<ITemplateInfo>(), null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("settings");
    }

    [Fact]
    public void Create_TemplateWithCachedHostData_WrapsTemplateAndParsedHostData()
    {
        var template = Substitute.For<ITemplateInfo, ITemplateInfoHostJsonCache>();
        ((ITemplateInfoHostJsonCache)template).HostData.Returns("""{"isHidden":true}""");
        var settings = Substitute.For<IEngineEnvironmentSettings>();

        var info = FuncTemplateInfo.Create(template, settings);

        info.Template.Should().BeSameAs(template);
        info.HostData.IsHidden.Should().BeTrue();
    }

    [Fact]
    public void Properties_ForwardToTemplate()
    {
        ITemplateInfo template = Substitute.For<ITemplateInfo>();
        template.Identity.Returns("Func.Tests.Template");
        FuncTemplateInfo info = new(template, FuncTemplateHostData.Empty);

        info.Template.Should().BeSameAs(template);
        info.Identity.Should().Be(template.Identity);
        info.HostConfigPlace.Should().Be(template.HostConfigPlace);
    }
}
