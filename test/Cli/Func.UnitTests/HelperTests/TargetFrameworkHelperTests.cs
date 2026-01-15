// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using FluentAssertions;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.HelperTests
{
    /// <summary>
    /// Unit tests for TargetFrameworkHelper validation methods.
    /// These tests cover target framework validation logic that was previously tested via E2E tests
    /// by spawning CLI processes, which is unnecessary for testing pure validation logic.
    /// </summary>
    public class TargetFrameworkHelperTests
    {
        [Fact]
        public void GetSupportedTargetFrameworks_ReturnsNet10()
        {
            var result = TargetFrameworkHelper.GetSupportedTargetFrameworks();

            result.Should().Contain(TargetFramework.Net10);
        }

        [Fact]
        public void GetSupportedTargetFrameworks_ReturnsNet9()
        {
            var result = TargetFrameworkHelper.GetSupportedTargetFrameworks();

            result.Should().Contain(TargetFramework.Net9);
        }

        [Fact]
        public void GetSupportedTargetFrameworks_ReturnsNet8()
        {
            var result = TargetFrameworkHelper.GetSupportedTargetFrameworks();

            result.Should().Contain(TargetFramework.Net8);
        }

        [Fact]
        public void GetSupportedTargetFrameworks_ReturnsNet7()
        {
            var result = TargetFrameworkHelper.GetSupportedTargetFrameworks();

            result.Should().Contain(TargetFramework.Net7);
        }

        [Fact]
        public void GetSupportedTargetFrameworks_ReturnsNet6()
        {
            var result = TargetFrameworkHelper.GetSupportedTargetFrameworks();

            result.Should().Contain(TargetFramework.Net6);
        }

        [Fact]
        public void GetSupportedTargetFrameworks_ReturnsNet48()
        {
            var result = TargetFrameworkHelper.GetSupportedTargetFrameworks();

            result.Should().Contain(TargetFramework.Net48);
        }

        [Fact]
        public void GetSupportedTargetFrameworks_DoesNotContainUnsupportedFrameworks()
        {
            var result = TargetFrameworkHelper.GetSupportedTargetFrameworks();

            // net5.0 and net7.0 are EOL, net5.0 should not be in the list
            result.Should().NotContain("net5.0");
        }

        [Fact]
        public void GetSupportedInProcTargetFrameworks_ReturnsNet8()
        {
            var result = TargetFrameworkHelper.GetSupportedInProcTargetFrameworks();

            result.Should().Contain(TargetFramework.Net8);
        }

        [Fact]
        public void GetSupportedInProcTargetFrameworks_ReturnsNet6()
        {
            var result = TargetFrameworkHelper.GetSupportedInProcTargetFrameworks();

            result.Should().Contain(TargetFramework.Net6);
        }

        [Fact]
        public void GetSupportedInProcTargetFrameworks_DoesNotContainNet9()
        {
            // In-proc model doesn't support .NET 9
            var result = TargetFrameworkHelper.GetSupportedInProcTargetFrameworks();

            result.Should().NotContain(TargetFramework.Net9);
        }

        [Fact]
        public void GetSupportedInProcTargetFrameworks_DoesNotContainNet7()
        {
            // .NET 7 is not supported for in-proc
            var result = TargetFrameworkHelper.GetSupportedInProcTargetFrameworks();

            result.Should().NotContain(TargetFramework.Net7);
        }

        [Fact]
        public void GetSupportedInProcTargetFrameworks_OnlyContainsTwoFrameworks()
        {
            // In-proc only supports net8.0 and net6.0
            var result = TargetFrameworkHelper.GetSupportedInProcTargetFrameworks();

            result.Should().HaveCount(2);
        }

        [Theory]
        [InlineData("net6.0", true)]
        [InlineData("net7.0", true)]
        [InlineData("net8.0", true)]
        [InlineData("net9.0", true)]
        [InlineData("net10.0", true)]
        [InlineData("net8.0-windows", true)]
        [InlineData("net6.0-android", true)]
        [InlineData("netstandard2.0", true)]
        [InlineData("netstandard2.1", true)]
        [InlineData("netcoreapp3.1", true)]
        [InlineData("net45", true)]
        [InlineData("net48", true)]
        [InlineData("net481", true)]
        [InlineData("netcoreapp2.1", true)]
        public void TfmRegex_MatchesValidTfms(string tfm, bool shouldMatch)
        {
            var result = TargetFrameworkHelper.TfmRegex.IsMatch(tfm);

            result.Should().Be(shouldMatch);
        }

        [Theory]
        [InlineData("invalid")]
        [InlineData("dotnet")]
        [InlineData("framework")]
        [InlineData("")]
        public void TfmRegex_DoesNotMatchInvalidTfms(string tfm)
        {
            var result = TargetFrameworkHelper.TfmRegex.IsMatch(tfm);

            result.Should().BeFalse();
        }

        /// <summary>
        /// These tests validate the scenarios that would cause errors in func init.
        /// Instead of spawning a CLI process, we directly test the validation logic.
        /// </summary>
        [Theory]
        [InlineData("net7.0", false)]
        [InlineData("net9.0", false)]
        [InlineData("net5.0", false)]
        [InlineData("net6.0", true)]
        [InlineData("net8.0", true)]
        public void TargetFramework_ForDotnetInProc_ValidatesCorrectly(string targetFramework, bool isValid)
        {
            var supportedFrameworks = TargetFrameworkHelper.GetSupportedInProcTargetFrameworks();
            var result = supportedFrameworks.Contains(targetFramework, StringComparer.InvariantCultureIgnoreCase);

            result.Should().Be(isValid);
        }

        [Theory]
        [InlineData("net6.0", true)]
        [InlineData("net7.0", true)]
        [InlineData("net8.0", true)]
        [InlineData("net9.0", true)]
        [InlineData("net10.0", true)]
        [InlineData("net48", true)]
        [InlineData("net5.0", false)]
        [InlineData("net47", false)]
        [InlineData("netcoreapp3.1", false)]
        public void TargetFramework_ForDotnetIsolated_ValidatesCorrectly(string targetFramework, bool isValid)
        {
            var supportedFrameworks = TargetFrameworkHelper.GetSupportedTargetFrameworks();
            var result = supportedFrameworks.Contains(targetFramework, StringComparer.InvariantCultureIgnoreCase);

            result.Should().Be(isValid);
        }
    }
}
