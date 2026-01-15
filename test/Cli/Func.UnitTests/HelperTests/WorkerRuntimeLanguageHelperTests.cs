// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using Azure.Functions.Cli.Helpers;
using FluentAssertions;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.HelperTests
{
    /// <summary>
    /// Unit tests for WorkerRuntimeLanguageHelper validation methods.
    /// These tests cover validation logic that was previously tested via E2E tests
    /// by spawning CLI processes, which is unnecessary for testing pure validation logic.
    /// </summary>
    public class WorkerRuntimeLanguageHelperTests
    {
        [Theory]
        [InlineData("dotnet", WorkerRuntime.Dotnet)]
        [InlineData("Dotnet", WorkerRuntime.Dotnet)]
        [InlineData("DOTNET", WorkerRuntime.Dotnet)]
        [InlineData("c#", WorkerRuntime.Dotnet)]
        [InlineData("csharp", WorkerRuntime.Dotnet)]
        [InlineData("f#", WorkerRuntime.Dotnet)]
        [InlineData("fsharp", WorkerRuntime.Dotnet)]
        public void NormalizeWorkerRuntime_WithValidDotnetInputs_ReturnsDotnetRuntime(string input, WorkerRuntime expected)
        {
            var result = WorkerRuntimeLanguageHelper.NormalizeWorkerRuntime(input);

            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("dotnet-isolated", WorkerRuntime.DotnetIsolated)]
        [InlineData("Dotnet-Isolated", WorkerRuntime.DotnetIsolated)]
        [InlineData("c#-isolated", WorkerRuntime.DotnetIsolated)]
        [InlineData("csharp-isolated", WorkerRuntime.DotnetIsolated)]
        [InlineData("f#-isolated", WorkerRuntime.DotnetIsolated)]
        [InlineData("fsharp-isolated", WorkerRuntime.DotnetIsolated)]
        public void NormalizeWorkerRuntime_WithValidDotnetIsolatedInputs_ReturnsDotnetIsolatedRuntime(string input, WorkerRuntime expected)
        {
            var result = WorkerRuntimeLanguageHelper.NormalizeWorkerRuntime(input);

            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("node", WorkerRuntime.Node)]
        [InlineData("Node", WorkerRuntime.Node)]
        [InlineData("NODE", WorkerRuntime.Node)]
        [InlineData("js", WorkerRuntime.Node)]
        [InlineData("javascript", WorkerRuntime.Node)]
        [InlineData("typescript", WorkerRuntime.Node)]
        [InlineData("ts", WorkerRuntime.Node)]
        public void NormalizeWorkerRuntime_WithValidNodeInputs_ReturnsNodeRuntime(string input, WorkerRuntime expected)
        {
            var result = WorkerRuntimeLanguageHelper.NormalizeWorkerRuntime(input);

            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("python", WorkerRuntime.Python)]
        [InlineData("Python", WorkerRuntime.Python)]
        [InlineData("PYTHON", WorkerRuntime.Python)]
        [InlineData("py", WorkerRuntime.Python)]
        public void NormalizeWorkerRuntime_WithValidPythonInputs_ReturnsPythonRuntime(string input, WorkerRuntime expected)
        {
            var result = WorkerRuntimeLanguageHelper.NormalizeWorkerRuntime(input);

            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("powershell", WorkerRuntime.Powershell)]
        [InlineData("Powershell", WorkerRuntime.Powershell)]
        [InlineData("POWERSHELL", WorkerRuntime.Powershell)]
        [InlineData("pwsh", WorkerRuntime.Powershell)]
        public void NormalizeWorkerRuntime_WithValidPowershellInputs_ReturnsPowershellRuntime(string input, WorkerRuntime expected)
        {
            var result = WorkerRuntimeLanguageHelper.NormalizeWorkerRuntime(input);

            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("custom", WorkerRuntime.Custom)]
        [InlineData("Custom", WorkerRuntime.Custom)]
        [InlineData("CUSTOM", WorkerRuntime.Custom)]
        public void NormalizeWorkerRuntime_WithValidCustomInputs_ReturnsCustomRuntime(string input, WorkerRuntime expected)
        {
            var result = WorkerRuntimeLanguageHelper.NormalizeWorkerRuntime(input);

            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("foo")]
        [InlineData("bar")]
        [InlineData("unknown")]
        [InlineData("ruby")]
        [InlineData("go")]
        [InlineData("rust")]
        [InlineData("dotnet5")]
        [InlineData("dotnet6")]
        [InlineData("node16")]
        [InlineData("python3")]
        [InlineData("")]
        public void NormalizeWorkerRuntime_WithInvalidInputs_ThrowsArgumentException(string input)
        {
            Action act = () => WorkerRuntimeLanguageHelper.NormalizeWorkerRuntime(input);

            if (string.IsNullOrWhiteSpace(input))
            {
                act.Should().Throw<ArgumentNullException>()
                    .WithMessage("*Worker runtime cannot be null or empty*");
            }
            else
            {
                act.Should().Throw<ArgumentException>()
                    .WithMessage($"Worker runtime '{input}' is not a valid option.*");
            }
        }

        [Fact]
        public void NormalizeWorkerRuntime_WithNull_ThrowsArgumentNullException()
        {
            Action act = () => WorkerRuntimeLanguageHelper.NormalizeWorkerRuntime(null!);

            act.Should().Throw<ArgumentNullException>()
                .WithMessage("*Worker runtime cannot be null or empty*");
        }

        [Fact]
        public void NormalizeWorkerRuntime_WithWhitespace_ThrowsArgumentNullException()
        {
            Action act = () => WorkerRuntimeLanguageHelper.NormalizeWorkerRuntime("   ");

            act.Should().Throw<ArgumentNullException>()
                .WithMessage("*Worker runtime cannot be null or empty*");
        }

        [Theory]
        [InlineData(WorkerRuntime.Dotnet, "dotnet")]
        [InlineData(WorkerRuntime.DotnetIsolated, "dotnet-isolated")]
        [InlineData(WorkerRuntime.Node, "node")]
        [InlineData(WorkerRuntime.Python, "python")]
        [InlineData(WorkerRuntime.Java, "java")]
        [InlineData(WorkerRuntime.Powershell, "powershell")]
        [InlineData(WorkerRuntime.Custom, "custom")]
        [InlineData(WorkerRuntime.None, "None")]
        public void GetRuntimeMoniker_ReturnsExpectedMoniker(WorkerRuntime runtime, string expectedMoniker)
        {
            var result = WorkerRuntimeLanguageHelper.GetRuntimeMoniker(runtime);

            result.Should().Be(expectedMoniker);
        }

        [Theory]
        [InlineData(WorkerRuntime.Dotnet, true)]
        [InlineData(WorkerRuntime.DotnetIsolated, true)]
        [InlineData(WorkerRuntime.Node, false)]
        [InlineData(WorkerRuntime.Python, false)]
        [InlineData(WorkerRuntime.Powershell, false)]
        [InlineData(WorkerRuntime.Custom, false)]
        [InlineData(WorkerRuntime.None, false)]
        public void IsDotnet_ReturnsExpectedResult(WorkerRuntime runtime, bool expected)
        {
            var result = WorkerRuntimeLanguageHelper.IsDotnet(runtime);

            result.Should().Be(expected);
        }

        [Theory]
        [InlineData(WorkerRuntime.DotnetIsolated, true)]
        [InlineData(WorkerRuntime.Dotnet, false)]
        [InlineData(WorkerRuntime.Node, false)]
        [InlineData(WorkerRuntime.Python, false)]
        [InlineData(WorkerRuntime.Powershell, false)]
        [InlineData(WorkerRuntime.Custom, false)]
        [InlineData(WorkerRuntime.None, false)]
        public void IsDotnetIsolated_ReturnsExpectedResult(WorkerRuntime runtime, bool expected)
        {
            var result = WorkerRuntimeLanguageHelper.IsDotnetIsolated(runtime);

            result.Should().Be(expected);
        }

        [Fact]
        public void AvailableWorkersRuntimeString_ContainsExpectedRuntimes()
        {
            var result = WorkerRuntimeLanguageHelper.AvailableWorkersRuntimeString;

            result.Should().Contain("dotnet");
            result.Should().Contain("dotnet-isolated");
            result.Should().Contain("node");
            result.Should().Contain("python");
            result.Should().Contain("powershell");
            result.Should().Contain("custom");

            // Java is excluded from the available workers list
            result.Should().NotContain("java");
        }

        [Fact]
        public void AvailableWorkersList_ContainsExpectedRuntimes()
        {
            var result = WorkerRuntimeLanguageHelper.AvailableWorkersList;

            result.Should().Contain(WorkerRuntime.Dotnet);
            result.Should().Contain(WorkerRuntime.DotnetIsolated);
            result.Should().Contain(WorkerRuntime.Node);
            result.Should().Contain(WorkerRuntime.Python);
            result.Should().Contain(WorkerRuntime.Powershell);
            result.Should().Contain(WorkerRuntime.Custom);

            // Java is excluded
            result.Should().NotContain(WorkerRuntime.Java);
        }

        [Theory]
        [InlineData("js", "javascript")]
        [InlineData("javascript", "javascript")]
        [InlineData("node", "javascript")]
        [InlineData("ts", "typescript")]
        [InlineData("typescript", "typescript")]
        [InlineData("py", "python")]
        [InlineData("python", "python")]
        [InlineData("pwsh", "powershell")]
        [InlineData("powershell", "powershell")]
        [InlineData("csharp", "c#")]
        [InlineData("dotnet", "c#")]
        [InlineData("dotnet-isolated", "c#")]
        [InlineData("fsharp", "f#")]
        public void NormalizeLanguage_WithValidInputs_ReturnsExpectedLanguage(string input, string expected)
        {
            var result = WorkerRuntimeLanguageHelper.NormalizeLanguage(input);

            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("invalid")]
        [InlineData("unknown")]
        [InlineData("ruby")]
        public void NormalizeLanguage_WithInvalidInputs_ThrowsArgumentException(string input)
        {
            Action act = () => WorkerRuntimeLanguageHelper.NormalizeLanguage(input);

            act.Should().Throw<ArgumentException>()
                .WithMessage($"Language '{input}' is not available*");
        }

        [Fact]
        public void NormalizeLanguage_WithNull_ThrowsArgumentNullException()
        {
            Action act = () => WorkerRuntimeLanguageHelper.NormalizeLanguage(null!);

            act.Should().Throw<ArgumentNullException>()
                .WithMessage("*language can't be empty*");
        }

        [Fact]
        public void WorkerToSupportedLanguages_Node_SupportsJavaScriptAndTypeScript()
        {
            var languages = WorkerRuntimeLanguageHelper.WorkerToSupportedLanguages[WorkerRuntime.Node];

            languages.Should().Contain("javascript");
            languages.Should().Contain("typescript");
        }

        [Fact]
        public void WorkerToSupportedLanguages_Dotnet_SupportsCSharpAndFSharp()
        {
            var languages = WorkerRuntimeLanguageHelper.WorkerToSupportedLanguages[WorkerRuntime.Dotnet];

            languages.Should().Contain("c#");
            languages.Should().Contain("f#");
        }

        [Fact]
        public void WorkerToSupportedLanguages_DotnetIsolated_SupportsCSharpAndFSharp()
        {
            var languages = WorkerRuntimeLanguageHelper.WorkerToSupportedLanguages[WorkerRuntime.DotnetIsolated];

            languages.Should().Contain("c#");
            languages.Should().Contain("f#");
        }
    }
}
