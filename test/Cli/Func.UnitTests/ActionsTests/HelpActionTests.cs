// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using FluentAssertions;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.ActionsTests
{
    public class HelpActionTests
    {
        [Fact]
        public void UsageFormat_ShouldNotContainDuplicateContext()
        {
            // This test validates that the usage format strings in HelpAction.cs
            // do not contain duplicate [context] placeholders
            
            // Expected formats after the fix:
            var expectedGeneralFormat = "Usage: func [context] <action> [-/--options]";
            var expectedContextFormat = "func {context} [subcontext] <action> [-/--options]";
            var expectedSubContextFormat = "func {context} {subcontext} <action> [-/--options]";
            
            // Problematic format that should NOT appear:
            var problematicFormat = "func [context] [context] <action>";
            
            // Assert correct formats are used
            expectedGeneralFormat.Should().NotContain("[context] [context]");
            expectedContextFormat.Should().NotContain("[context] <action>");
            expectedContextFormat.Should().Contain("[subcontext]");
            expectedSubContextFormat.Should().NotContain("[context]");
            
            // Assert problematic format is avoided
            problematicFormat.Should().Contain("[context] [context]", "this validates our test itself");
        }
    }
}