// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AwesomeAssertions;
using Azure.Functions.Cli.Actions.LocalActions;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.ActionsTests
{
    public class CreateFunctionActionTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "CreateFunctionActionTests", Guid.NewGuid().ToString());

        public CreateFunctionActionTests()
        {
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            Directory.Delete(_dir, recursive: true);
        }

        [Fact]
        public void GetAvailableFunctionName_ReturnsDefault_WhenNoFolderExists()
        {
            CreateFunctionAction.GetAvailableFunctionName("HttpTrigger", _dir).Should().Be("HttpTrigger");
        }

        [Fact]
        public void GetAvailableFunctionName_AppendsFirstFreeNumber_WhenDefaultExists()
        {
            Directory.CreateDirectory(Path.Combine(_dir, "HttpTrigger"));
            CreateFunctionAction.GetAvailableFunctionName("HttpTrigger", _dir).Should().Be("HttpTrigger1");

            Directory.CreateDirectory(Path.Combine(_dir, "HttpTrigger1"));
            CreateFunctionAction.GetAvailableFunctionName("HttpTrigger", _dir).Should().Be("HttpTrigger2");
        }

        [Fact]
        public void GetAvailableFunctionName_IgnoresOtherTriggerFolders()
        {
            Directory.CreateDirectory(Path.Combine(_dir, "TimerTrigger"));
            CreateFunctionAction.GetAvailableFunctionName("HttpTrigger", _dir).Should().Be("HttpTrigger");
        }
    }
}
