// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using Azure.Functions.Cli.Actions;
using Azure.Functions.Cli.Interfaces;
using FluentAssertions;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.ActionsTests
{
    public class HelpActionTests
    {
        [Fact]
        public void UsageFormat_ShouldNotContainDuplicateContext()
        {
            var expectedGeneralFormat = "Usage: func [context] <action> [-/--options]";
            var expectedContextWithSubcontextFormat = "func {context} [subcontext] <action> [-/--options]";
            var expectedContextWithoutSubcontextFormat = "func {context} <action> [-/--options]";
            var expectedSubContextFormat = "func {context} {subcontext} <action> [-/--options]";
            var problematicFormat = "func [context] [context] <action>";

            expectedGeneralFormat.Should().NotContain("[context] [context]");
            expectedContextWithSubcontextFormat.Should().NotContain("[context] <action>");
            expectedContextWithSubcontextFormat.Should().Contain("[subcontext]");
            expectedContextWithoutSubcontextFormat.Should().NotContain("[subcontext]");
            expectedSubContextFormat.Should().NotContain("[context]");
            problematicFormat.Should().Contain("[context] [context]");
        }
    }
}
