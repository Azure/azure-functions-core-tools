// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Xunit;

namespace Azure.Functions.Cli.Workload.DotNet.Tests;

public class DotNetWorkloadTests
{
    // Some sample tests just to fill out the test project a bit
    [Fact]
    public void DisplayName_ReturnsDotNet()
    {
        // Arrange
        var workload = new DotNetWorkload();

        // Act & Assert
        Assert.Equal(".NET", workload.DisplayName);
    }

    [Fact]
    public void Description_ReturnsCorrectValue()
    {
        // Arrange
        var workload = new DotNetWorkload();

        // Act & Assert
        Assert.Equal("Azure Functions tooling for .NET (C#) projects.", workload.Description);
    }
}
