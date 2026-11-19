// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Commands;

public class VersionOptionActionTests
{
    [Fact]
    public void Constructor_NullVersionCommand_Throws()
    {
        Action act = () => new VersionOptionAction(null!);

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("versionCommand");
    }

    [Fact]
    public void ClearsParseErrors_ReturnsTrue()
    {
        var command = new VersionCommand(new TestInteractionService(), Substitute.For<ICliVersionProvider>());

        var action = new VersionOptionAction(command);

        action.ClearsParseErrors.Should().BeTrue();
    }
}
