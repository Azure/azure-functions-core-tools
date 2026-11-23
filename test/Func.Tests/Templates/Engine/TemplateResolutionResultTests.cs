// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class TemplateResolutionResultTests
{
    [Fact]
    public void Constructor_NullTemplates_Throws()
    {
        Action act = () => _ = new TemplateResolutionResult(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("templates");
    }
}
