// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using Azure.Functions.Cli.Actions.LocalActions;
using Azure.Functions.Cli.Common;
using NSubstitute;
using Xunit;
using static Azure.Functions.Cli.Common.Constants;

namespace Azure.Functions.Cli.UnitTests.ActionsTests
{
    public class CreateFunctionActionTests
    {
        [Fact]
        public void GetUniqueDefaultFunctionName_ReturnsDefault_WhenAvailable()
        {
            var result = CreateFunctionAction.GetUniqueDefaultFunctionName("HttpTrigger", _ => false);

            Assert.Equal("HttpTrigger", result);
        }

        [Fact]
        public void GetUniqueDefaultFunctionName_IncrementsUntilNameIsAvailable()
        {
            var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "HttpTrigger",
                "HttpTrigger1",
                "HttpTrigger2"
            };

            var result = CreateFunctionAction.GetUniqueDefaultFunctionName("HttpTrigger", existingNames.Contains);

            Assert.Equal("HttpTrigger3", result);
        }

        [Fact]
        public void ShouldUseUniqueDefaultFunctionName_ReturnsFalse_ForNodeV4WithFixedFileName()
        {
            var template = new Template { Id = "HttpTrigger-JavaScript-4.x" };

            var result = CreateFunctionAction.ShouldUseUniqueDefaultFunctionName("functions.js", template);

            Assert.False(result);
        }

        [Fact]
        public void ShouldUseUniqueDefaultFunctionName_ReturnsTrue_ForNodeV4WithoutFixedFileName()
        {
            var template = new Template { Id = "HttpTrigger-JavaScript-4.x" };

            var result = CreateFunctionAction.ShouldUseUniqueDefaultFunctionName(null, template);

            Assert.True(result);
        }

        [Theory]
        [InlineData(Languages.CSharp, "HttpTrigger.cs")]
        [InlineData(Languages.FSharp, "HttpTrigger.fs")]
        public void DotnetFunctionArtifactExists_ChecksGeneratedFunctionFile(string language, string expectedFileName)
        {
            var fileSystem = Substitute.For<System.IO.Abstractions.IFileSystem>();
            fileSystem.File.Exists(Arg.Is<string>(path => path.EndsWith(expectedFileName, StringComparison.Ordinal))).Returns(true);

            using (FileSystemHelpers.Override(fileSystem))
            {
                var result = CreateFunctionAction.DotnetFunctionArtifactExists("HttpTrigger", language);

                Assert.True(result);
            }
        }

        [Fact]
        public void PythonV2FunctionArtifactExists_ChecksExistingFunctionDefinition()
        {
            var fileSystem = Substitute.For<System.IO.Abstractions.IFileSystem>();
            fileSystem.File.Exists(Arg.Any<string>()).Returns(true);
            fileSystem.File.ReadAllText(Arg.Any<string>()).Returns(
                "import azure.functions as func\n" +
                "\n" +
                "app = func.FunctionApp()\n" +
                "\n" +
                "@app.route(route=\"HttpTrigger\")\n" +
                "def HttpTrigger(req: func.HttpRequest) -> func.HttpResponse:\n" +
                "    pass\n");

            using (FileSystemHelpers.Override(fileSystem))
            {
                var result = CreateFunctionAction.PythonV2FunctionArtifactExists("HttpTrigger", PySteinFunctionAppPy);

                Assert.True(result);
            }
        }
    }
}
