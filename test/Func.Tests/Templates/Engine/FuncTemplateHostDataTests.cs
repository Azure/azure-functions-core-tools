// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class FuncTemplateHostDataTests
{
    [Fact]
    public void Parse_NullJson_Throws()
    {
        Action act = () => FuncTemplateHostData.Parse(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("json");
    }
}
