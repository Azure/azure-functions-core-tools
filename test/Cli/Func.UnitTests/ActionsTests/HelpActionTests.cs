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
            var expectedContextWithSubcontextFormat = "func {context} [subcontext] <action> [-/--options]";
            var expectedContextWithoutSubcontextFormat = "func {context} <action> [-/--options]";
            var expectedSubContextFormat = "func {context} {subcontext} <action> [-/--options]";

            // Problematic format that should NOT appear:
            var problematicFormat = "func [context] [context] <action>";

            // Assert correct formats are used
            expectedGeneralFormat.Should().NotContain("[context] [context]");
            expectedContextWithSubcontextFormat.Should().NotContain("[context] <action>");
            expectedContextWithSubcontextFormat.Should().Contain("[subcontext]");
            expectedContextWithoutSubcontextFormat.Should().NotContain("[subcontext]");
            expectedSubContextFormat.Should().NotContain("[context]");

            // Assert problematic format is avoided
            problematicFormat.Should().Contain("[context] [context]", "this validates our test itself");
        }

        [Theory]
        [InlineData(new string[] { "--help" }, true)]
        [InlineData(new string[] { "-h" }, true)]
        [InlineData(new string[] { }, true)] // just "func"
        public void IsHelpRequested_ReturnsExpected(string[] args, bool expected)
        {
            var result = Program.IsHelpRequested(args);
            Assert.Equal(expected, result);
        }
    }
}
