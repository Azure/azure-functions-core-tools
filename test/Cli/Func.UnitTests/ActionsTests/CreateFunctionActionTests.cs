// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using Azure.Functions.Cli.Actions.LocalActions;
using Xunit;

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
    }
}
