// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class FuncTemplateInfoTests
{
    [Fact]
    public void Constructor_NullTemplate_Throws()
    {
        Action act = () => _ = new FuncTemplateInfo(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("template");
    }

    [Fact]
    public void Properties_ForwardToTemplate()
    {
        ITemplateInfo template = Substitute.For<ITemplateInfo>();
        template.Identity.Returns("Func.Tests.Template");
        template.HostConfigPlace.Returns(".template.config/host.json");
        FuncTemplateInfo info = new(template);

        info.Template.Should().BeSameAs(template);
        info.Identity.Should().Be(template.Identity);
        info.HostConfigPlace.Should().Be(template.HostConfigPlace);
    }
}
